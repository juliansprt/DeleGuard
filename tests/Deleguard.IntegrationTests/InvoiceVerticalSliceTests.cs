using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Deleguard.IntegrationTests;

public sealed class InvoiceVerticalSliceTests : IAsyncLifetime
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
                    ["DemoAuth:ApiKeyPepper"] = "deleguard-dev-pepper",
                    ["DemoAuth:ConsoleToken"] = "dgc_demo_console_local_dev_only",
                    ["DemoAuth:DemoOrganizationId"] = DemoSeeder.OrganizationId,
                    ["Permits:SigningKey"] = "deleguard-dev-permit-hmac-key-change-in-prod-32"
                });
            });
        });
        _agent = _factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        _agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoAgentKey);
        _console = _factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        _console.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "dgc_demo_console_local_dev_only");
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
    public async Task Direct_authorization_approvals_and_evidence_work_without_gateway_configuration()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var before = await _console!.GetStringAsync("/");
        var setupState = System.Text.RegularExpressions.Regex.Match(before, "Configuration (ready|in progress)").Value;
        Assert.NotEmpty(setupState);
        Assert.Equal("Configuration ready", setupState);
        await db.GatewayModelRoutes.ExecuteDeleteAsync();
        await db.GatewaySettings.ExecuteDeleteAsync();

        await Draft_invoice_allows_and_accepts_evidence();
        await Tax_reported_denies_with_http_200();
        await High_value_requires_approval_then_permit();
        await Idempotency_returns_same_decision();

        var setup = await _console!.GetStringAsync("/agents/connect?AgentId=" + DemoSeeder.AgentId);
        Assert.Contains("POST /v1/authorize", setup);
        var overview = await _console.GetStringAsync("/");
        Assert.Contains(setupState, overview);
        Assert.False(await db.GatewaySettings.AnyAsync());
        Assert.False(await db.GatewayModelRoutes.AnyAsync());
    }

    [Fact]
    public async Task Draft_invoice_allows_and_accepts_evidence()
    {
        var decision = await AuthorizeAsync(_agent!, "FAC-DRAFT", "draft", 1_000_000, false);
        Assert.Equal("ALLOW", decision.GetProperty("decision").GetString());
        Assert.False(string.IsNullOrWhiteSpace(decision.GetProperty("permit").GetString()));

        var execution = await ExecutionContract.ConsumeAsync(_agent!, decision);
        using var evidence = new HttpRequestMessage(HttpMethod.Post, "/v1/executions")
        {
            Content = JsonContent.Create(new
            {
                decision_id = decision.GetProperty("decision_id").GetString(),
                execution_id = execution,
                resource_snapshot_hash = decision.GetProperty("resource_snapshot_hash").GetString(),
                status = "SUCCESS",
                provider_reference = "erp-1"
            })
        };
        var response = await _agent!.SendAsync(evidence);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task High_value_requires_approval_then_permit()
    {
        var pending = await AuthorizeAsync(_agent!, "FAC-8392", "issued", 35_000_000, false);
        Assert.Equal("REQUIRE_APPROVAL", pending.GetProperty("decision").GetString());
        var approvalId = pending.GetProperty("approval_id").GetString();
        var approved = await _console!.PostAsJsonAsync($"/v1/approvals/{approvalId}/approve", new { note = "ok" });
        approved.EnsureSuccessStatusCode();
        var body = await approved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ALLOW", body.GetProperty("decision").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("permit").GetString()));
    }

    [Fact]
    public async Task Tax_reported_denies_with_http_200()
    {
        var denied = await AuthorizeAsync(_agent!, "FAC-TAX", "issued", 500, true);
        Assert.Equal("DENY", denied.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Idempotency_returns_same_decision()
    {
        var key = Guid.NewGuid().ToString("N");
        var first = await AuthorizeAsync(_agent!, "FAC-IDEM", "draft", 100, false, key);
        var second = await AuthorizeAsync(_agent!, "FAC-IDEM", "draft", 100, false, key);
        Assert.Equal(first.GetProperty("decision_id").GetString(), second.GetProperty("decision_id").GetString());
    }

    [Fact]
    public async Task Evidence_without_permit_is_conflict()
    {
        var denied = await AuthorizeAsync(_agent!, "FAC-NO", "issued", 500, true);
        using var evidence = new HttpRequestMessage(HttpMethod.Post, "/v1/executions")
        {
            Content = JsonContent.Create(new
            {
                decision_id = denied.GetProperty("decision_id").GetString(),
                status = "SUCCESS"
            })
        };
        var response = await _agent!.SendAsync(evidence);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Console_can_list_agents_and_test_policy()
    {
        var agents = await _console!.GetAsync("/v1/agents");
        agents.EnsureSuccessStatusCode();
        var list = await agents.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(list.GetArrayLength() >= 1);

        using var test = new HttpRequestMessage(HttpMethod.Post, $"/v1/policies/{DemoSeeder.PolicyId}/test")
        {
            Content = JsonContent.Create(new
            {
                expected_effect = "DENY",
                request = new
                {
                    action = "invoice.cancel",
                    resource_type = "invoice",
                    resource_id = "FAC-TAX",
                    attributes = new { status = "issued", amount = 800000, reportedToTaxAuthority = true }
                }
            })
        };
        var response = await _console.SendAsync(test);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("passed").GetBoolean());
        Assert.Equal("DENY", body.GetProperty("actual_effect").GetString());
    }

    private static async Task<JsonElement> AuthorizeAsync(
        HttpClient client,
        string id,
        string status,
        long amount,
        bool reported,
        string? idempotency = null)
    {
        idempotency ??= Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                actor = new { id = "usr_481", type = "human" },
                operation = new { id = idempotency, hash = new string('a', 64) },
                action = "invoice.cancel",
                resource = new
                {
                    type = "invoice",
                    id,
                    attributes = new
                    {
                        status,
                        amount,
                        currency = "COP",
                        reportedToTaxAuthority = reported
                    }
                },
                context = new { reason = "test" }
            })
        };
        request.Headers.Add("Idempotency-Key", idempotency ?? Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
