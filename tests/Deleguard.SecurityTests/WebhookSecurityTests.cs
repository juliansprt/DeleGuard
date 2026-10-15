using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace Deleguard.SecurityTests;

public sealed class WebhookSecurityTests : IAsyncLifetime
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
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                    ["Permits:SigningKey"] = "deleguard-dev-permit-hmac-key-change-in-prod-32",
                    ["Webhooks:ProtectionKey"] = "deleguard-dev-webhook-protect-key-32"
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
    public async Task Agent_cannot_configure_webhooks()
    {
        var response = await _agent!.PostAsJsonAsync("/v1/webhooks", new
        {
            description = "nope",
            url = "https://example.com/hooks"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Metadata_and_loopback_urls_are_rejected()
    {
        var metadata = await _console!.PostAsJsonAsync("/v1/webhooks", new
        {
            description = "ssrf",
            url = "https://169.254.169.254/latest/meta-data"
        });
        Assert.Equal(HttpStatusCode.BadRequest, metadata.StatusCode);

        var loopback = await _console.PostAsJsonAsync("/v1/webhooks", new
        {
            description = "ssrf",
            url = "http://127.0.0.1/hooks"
        });
        Assert.Equal(HttpStatusCode.BadRequest, loopback.StatusCode);
    }

    [Fact]
    public async Task Console_can_create_https_endpoint()
    {
        var response = await _console!.PostAsJsonAsync("/v1/webhooks", new
        {
            description = "ok",
            url = "https://example.com/deleguard/hooks",
            event_types = new[] { "authorization.decided" }
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.StartsWith("whsec_", body.GetProperty("secret").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("endpoint_id").GetString()));
    }
}
