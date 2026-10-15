using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Deleguard.Application.Notifications;
using Deleguard.Domain.Notifications;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Deleguard.IntegrationTests;

public sealed class WebhookDeliveryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("deleguard")
        .WithUsername("deleguard")
        .WithPassword("deleguard")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _agent;
    private HttpClient? _console;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                    ["DemoAuth:ApiKeyPepper"] = "deleguard-dev-pepper",
                    ["DemoAuth:ConsoleToken"] = "dgc_demo_console_local_dev_only",
                    ["DemoAuth:DemoOrganizationId"] = DemoSeeder.OrganizationId,
                    ["Permits:SigningKey"] = "deleguard-dev-permit-hmac-key-change-in-prod-32",
                    ["Webhooks:ProtectionKey"] = "deleguard-dev-webhook-protect-key-32",
                    ["Webhooks:AllowLoopback"] = "true",
                    ["Webhooks:AllowInsecureHttp"] = "true"
                });
            });
        });
        _agent = _factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        _agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoAgentKey);
        _console = _factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        _console.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
    }

    public async Task DisposeAsync()
    {
        _agent?.Dispose();
        _console?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Recovery_excludes_messages_created_after_the_reviewed_cutoff()
    {
        var received = new ConcurrentBag<CapturedWebhook>();
        using var listener = StartReceiver(received, _ => HttpStatusCode.OK);
        var created = await CreateEndpointAsync(listener.Url, []);
        var endpoint = created.GetProperty("endpoint_id").GetString()!;
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
        WebhookMessage Message(DateTimeOffset at) => new() {
            Id = "msg_" + Guid.NewGuid().ToString("N"), OrganizationId = DemoSeeder.OrganizationId,
            EndpointId = endpoint, OutboxEventId = "evt_" + Guid.NewGuid().ToString("N"),
            EventType = "authorization.decided", PayloadJson = "{}", DestinationUrl = listener.Url,
            Status = WebhookMessageStatuses.Failed, CreatedAt = at, NextAttemptAt = at };
        var reviewed = Message(cutoff.AddSeconds(-1));
        var later = Message(cutoff.AddSeconds(1));
        db.WebhookMessages.AddRange(reviewed, later);
        await db.SaveChangesAsync();
        var count = await scope.ServiceProvider.GetRequiredService<WebhookAdminHandler>()
            .RecoverAsync(DemoSeeder.OrganizationId, endpoint, cutoff.AddDays(-1), cutoff, default);
        Assert.Equal(1, count);
        Assert.Equal(WebhookMessageStatuses.Pending, reviewed.Status);
        Assert.Equal(WebhookMessageStatuses.Failed, later.Status);
    }

    [Fact]
    public async Task Authorize_does_not_http_until_worker_fanout()
    {
        var received = new ConcurrentBag<CapturedWebhook>();
        using var listener = StartReceiver(received, _ => HttpStatusCode.OK);
        var created = await CreateEndpointAsync(listener.Url, ["authorization.decided"]);
        var secret = created.GetProperty("secret").GetString()!;

        await AuthorizeDraftAsync("FAC-WH-1");
        Assert.Empty(received);

        await ProcessOutboxAsync();
        var capture = Assert.Single(received, item => item.Body.Contains("FAC-WH-1", StringComparison.Ordinal));
        Assert.Equal("authorization.decided", capture.Type);
        Assert.True(StandardWebhookSignature.Verify(
            secret,
            capture.Id,
            capture.Timestamp,
            capture.Body,
            capture.Signature,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5)));
        Assert.Contains("\"decision\":\"ALLOW\"", capture.Body);
    }

    [Fact]
    public async Task Failed_delivery_can_be_resent()
    {
        var hits = 0;
        var received = new ConcurrentBag<CapturedWebhook>();
        using var listener = StartReceiver(received, body =>
        {
            if (!body.Contains("FAC-WH-500", StringComparison.Ordinal))
            {
                return HttpStatusCode.OK;
            }

            hits++;
            return hits == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;
        });
        var created = await CreateEndpointAsync(listener.Url, ["authorization.decided"]);
        var endpointId = created.GetProperty("endpoint_id").GetString();
        await AuthorizeDraftAsync("FAC-WH-500");
        await ProcessOutboxAsync();
        Assert.Contains(received, x => x.StatusCode == 500 && x.Body.Contains("FAC-WH-500", StringComparison.Ordinal));
        var failed = received.First(x => x.StatusCode == 500 && x.Body.Contains("FAC-WH-500", StringComparison.Ordinal));
        var resend = await _console!.PostAsync($"/v1/webhooks/{endpointId}/messages/{failed.Id}/resend", null);
        resend.EnsureSuccessStatusCode();
        Assert.True(hits >= 2);
        Assert.Contains(received, x => x.StatusCode == 200 && x.Type == "authorization.decided" && x.Body.Contains("FAC-WH-500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gone_disables_endpoint()
    {
        var received = new ConcurrentBag<CapturedWebhook>();
        using var listener = StartReceiver(received, _ => HttpStatusCode.Gone);
        var created = await CreateEndpointAsync(listener.Url, []);
        var id = created.GetProperty("endpoint_id").GetString();
        await _console!.PostAsync($"/v1/webhooks/{id}/ping", null);
        var endpoint = await _console.GetFromJsonAsync<JsonElement>($"/v1/webhooks/{id}");
        Assert.Equal("disabled", endpoint.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Console_webhook_pages_render()
    {
        var list = await _console!.GetAsync("/webhooks");
        list.EnsureSuccessStatusCode();
        var html = await list.Content.ReadAsStringAsync();
        Assert.Contains("id=\"webhook-create\"", html);
    }

    private async Task<JsonElement> CreateEndpointAsync(string url, IReadOnlyList<string> types)
    {
        var response = await _console!.PostAsJsonAsync("/v1/webhooks", new
        {
            description = "test",
            url,
            event_types = types
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task AuthorizeDraftAsync(string invoiceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                action = "invoice.cancel",
                resource = new { type = "invoice", id = invoiceId, attributes = new { status = "draft", amount = 1 } }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _agent!.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task ProcessOutboxAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var fanout = scope.ServiceProvider.GetRequiredService<FanoutOutboxHandler>();
        await fanout.HandleAsync(50, CancellationToken.None);
        var delivery = scope.ServiceProvider.GetRequiredService<DeliverWebhooksHandler>();
        await delivery.HandleDueAsync(20, CancellationToken.None);
    }

    private static Receiver StartReceiver(ConcurrentBag<CapturedWebhook> received, Func<string, HttpStatusCode> status)
    {
        var listener = new HttpListener();
        var port = FreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var code = status(body);
                var type = "";
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                    type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                }
                catch (System.Text.Json.JsonException)
                {
                    type = "";
                }

                received.Add(new CapturedWebhook(
                    context.Request.Headers["webhook-id"] ?? "",
                    context.Request.Headers["webhook-timestamp"] ?? "",
                    context.Request.Headers["webhook-signature"] ?? "",
                    body,
                    type,
                    (int)code));
                context.Response.StatusCode = (int)code;
                context.Response.Close();
            }
        }, cts.Token);
        return new Receiver(listener, prefix.TrimEnd('/'), cts);
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private sealed record CapturedWebhook(string Id, string Timestamp, string Signature, string Body, string Type, int StatusCode);

    private sealed class Receiver : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts;

        public Receiver(HttpListener listener, string url, CancellationTokenSource cts)
        {
            _listener = listener;
            Url = url;
            _cts = cts;
        }

        public string Url { get; }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Close();
            _cts.Dispose();
        }
    }
}
