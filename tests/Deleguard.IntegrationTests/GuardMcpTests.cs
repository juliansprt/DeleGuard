using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deleguard.Infrastructure.Persistence;
using Deleguard.McpGuard;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Deleguard.IntegrationTests;

public sealed class GuardMcpTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _agent;
    private HttpClient? _console;
    private readonly string _journalRoot=Path.Combine(Path.GetTempPath(),"dg-it-"+Guid.NewGuid().ToString("N"));
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var local = Environment.GetEnvironmentVariable("DELEGUARD_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(local))
        {
            _connectionString = local;
        }
        else
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("deleguard")
                .WithUsername("deleguard")
                .WithPassword("deleguard")
                .Build();
            await _postgres.StartAsync();
            _connectionString = _postgres.GetConnectionString();
        }
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _connectionString,
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
        _console.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
    }

    public async Task DisposeAsync()
    {
        if(Directory.Exists(_journalRoot))Directory.Delete(_journalRoot,true);
        _agent?.Dispose();
        _console?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    public async Task Agent_receives_seeded_invoice_binding()
    {
        var response = await _agent!.GetAsync("/v1/guard/config");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invoice-agent", body.GetProperty("agent_id").GetString());
        Assert.True(body.GetProperty("default_deny_unmapped").GetBoolean());
        var mappings = body.GetProperty("mappings");
        Assert.Equal(JsonValueKind.Array, mappings.ValueKind);
        Assert.Contains(mappings.EnumerateArray(), item =>
            item.GetProperty("action").GetString() == "invoice.cancel" &&
            item.GetProperty("tool").GetString() == "invoice-demo__cancel_invoice");
        Assert.DoesNotContain(mappings.EnumerateArray(), item =>
            item.GetProperty("action").GetString()?.StartsWith("admin.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Console_cannot_read_guard_config()
    {
        var response = await _console!.GetAsync("/v1/guard/config");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Draft_invoice_tool_is_allowed_and_forwarded()
    {
        var downstream = new RecordingDownstream();
        var enforcer = CreateEnforcer(downstream);
        var result = await enforcer.CallAsync(
            "invoice-demo__cancel_invoice",
            JsonSerializer.SerializeToElement(new
            {
                _deleguard_operation_id = Guid.NewGuid().ToString("N"),
            invoice_id = "FAC-GUARD-DRAFT-" + Guid.NewGuid().ToString("N")[..8],
                amount = 1_000_000,
                status = "draft",
                reportedToTaxAuthority = false
            }),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(result.Forwarded);
        Assert.Equal(1, downstream.Calls);
    }

    [Fact]
    public async Task High_value_requires_approval_without_forward_then_resumes()
    {
        var downstream = new RecordingDownstream();
        var enforcer = CreateEnforcer(downstream);
        var args = JsonSerializer.SerializeToElement(new
        {
            _deleguard_operation_id = Guid.NewGuid().ToString("N"),
            invoice_id = "FAC-GUARD-HI-" + Guid.NewGuid().ToString("N")[..8],
            amount = 35_000_000,
            status = "issued",
            reportedToTaxAuthority = false
        });

        var pending = await enforcer.CallAsync("invoice-demo__cancel_invoice", args, CancellationToken.None);
        Assert.True(pending.IsError);
        Assert.False(pending.Forwarded);
        Assert.Equal(0, downstream.Calls);
        var structured = pending.Structured;
        Assert.Equal("REQUIRE_APPROVAL", structured.GetProperty("decision").GetString());
        var approvalId = structured.GetProperty("approval_id").GetString();
        var decisionId = structured.GetProperty("decision_id").GetString();

        var approved = await _console!.PostAsJsonAsync($"/v1/approvals/{approvalId}/approve", new { note = "guard" });
        approved.EnsureSuccessStatusCode();

        var resumed = await enforcer.CallAsync(
            GuardEnforcer.GetDecisionTool,
            JsonSerializer.SerializeToElement(new { decision_id = decisionId }),
            CancellationToken.None);
        Assert.True(resumed.Forwarded);
        Assert.Equal(1, downstream.Calls);
    }

    private GuardEnforcer CreateEnforcer(RecordingDownstream downstream)
    {
        var options = Options.Create(new GuardOptions
        {
            Url = _agent!.BaseAddress!.ToString(),
            ApiKey = DemoSeeder.DemoAgentKey,
            AgentId = "invoice-agent"
        });
        var pdp = new DeleguardPdpClient(_agent, options);
        return new GuardEnforcer(pdp, pdp, new DownstreamMcpHub([downstream]),new GuardJournal(_journalRoot));
    }

    private sealed class RecordingDownstream : IDownstreamMcp
    {
        public string Name => "invoice-demo";
        public string ConnectionHash => "integration-test-destination";
        public int Calls { get; private set; }

        public Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownstreamTool>>([
                new DownstreamTool { Name = "cancel_invoice", InputSchema = JsonSerializer.SerializeToElement(new { type = "object" }) }
            ]);

        public Task<DownstreamCallResult> CallAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new DownstreamCallResult { IsError = false, Text = "ok" });
        }
    }
}
