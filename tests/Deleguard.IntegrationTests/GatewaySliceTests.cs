using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace Deleguard.IntegrationTests;

public sealed class GatewaySliceTests : IAsyncLifetime
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
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
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
    public async Task Cheap_catalog_model_allows()
    {
        var decision = await AuthorizeAsync("chat.cheap", true, "cheap", 0.002m, false);
        Assert.Equal("ALLOW", decision.GetProperty("decision").GetString());
        Assert.False(string.IsNullOrWhiteSpace(decision.GetProperty("permit").GetString()));
    }

    [Fact]
    public async Task Expensive_completion_requires_approval()
    {
        var decision = await AuthorizeAsync("chat.strong", true, "strong", 2.5m, false);
        Assert.Equal("REQUIRE_APPROVAL", decision.GetProperty("decision").GetString());
        Assert.False(string.IsNullOrWhiteSpace(decision.GetProperty("approval_id").GetString()));
    }

    [Fact]
    public async Task Unknown_model_denies_with_http_200()
    {
        var decision = await AuthorizeAsync("gpt-secret", false, "cheap", 0.01m, false);
        Assert.Equal("DENY", decision.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Over_budget_is_deny()
    {
        var decision = await AuthorizeAsync("chat.cheap", true, "cheap", 0.01m, true);
        Assert.Equal("DENY", decision.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Spend_reserve_requires_allow_with_permit()
    {
        var denied = await AuthorizeAsync("gpt-secret", false, "cheap", 0.01m, false);
        var response = await _agent!.PostAsJsonAsync("/v1/gateway/spend/reserve", new
        {
            decision_id = denied.GetProperty("decision_id").GetString(),
            estimated_cost_usd = 0.01m
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Spend_reserve_then_settle_after_allow()
    {
        var allowed = await AuthorizeAsync("chat.cheap", true, "cheap", 0.002m, false);
        var reserved = await _agent!.PostAsJsonAsync("/v1/gateway/spend/reserve", new
        {
            decision_id = allowed.GetProperty("decision_id").GetString(),
            estimated_cost_usd = 0.002m
        });
        reserved.EnsureSuccessStatusCode();
        var settled = await _agent.PostAsJsonAsync("/v1/gateway/spend/settle", new
        {
            decision_id = allowed.GetProperty("decision_id").GetString(),
            actual_cost_usd = 0.001m
        });
        settled.EnsureSuccessStatusCode();
        var config = await _agent.GetFromJsonAsync<JsonElement>("/v1/gateway/config");
        Assert.True(config.GetProperty("remaining_budget_usd").GetDecimal() < 50m);
    }

    [Fact]
    public async Task Console_reads_gateway_settings_and_agent_cannot()
    {
        var settings = await _console!.GetFromJsonAsync<JsonElement>("/v1/gateway/settings");
        Assert.True(settings.TryGetProperty("used_budget_usd", out _));
        Assert.True(settings.TryGetProperty("remaining_budget_usd", out _));
        var forbidden = await _agent!.GetAsync("/v1/gateway/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
        var config = await _console.GetAsync("/v1/gateway/config");
        Assert.Equal(HttpStatusCode.Unauthorized, config.StatusCode);
        var mutate = await _agent.PostAsJsonAsync("/v1/gateway/routes", new
        {
            alias = "chat.stolen",
            provider = "openai",
            model = "gpt-stolen",
            tier = "cheap",
            region = "global",
            fallback_group = "chat"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, mutate.StatusCode);
    }

    [Fact]
    public async Task Console_can_configure_token_budgets_and_catalog()
    {
        var saved = await _console!.PostAsJsonAsync("/v1/gateway/settings", new
        {
            enabled = true,
            routing_mode = "heuristic",
            monthly_budget_usd = 75,
            default_token_budget_class = "short",
            short_max_output_tokens = 128,
            standard_max_output_tokens = 1024,
            deep_max_output_tokens = 4096,
            heuristic_cheap_max_tokens = 250,
            cascade_skip_to_strong_tokens = 1500,
            cascade_max_attempts = 2,
            cascade_max_usd = 0.75,
            exact_cache_ttl_seconds = 60,
            prefix_cache_enabled = true,
            shadow_router_enabled = false
        });
        saved.EnsureSuccessStatusCode();

        var upserted = await _console.PostAsJsonAsync("/v1/gateway/routes", new
        {
            alias = "chat.lab",
            provider = "openai",
            model = "gpt-lab",
            tier = "cheap",
            region = "global",
            fallback_group = "chat",
            input_price_per_million = 0.1,
            output_price_per_million = 0.4,
            sort_order = 9,
            enabled = true
        });
        upserted.EnsureSuccessStatusCode();

        var config = await _agent!.GetFromJsonAsync<JsonElement>("/v1/gateway/config");
        var shortClass = config.GetProperty("token_budget_classes").EnumerateArray()
            .First(item => item.GetProperty("name").GetString() == "short");
        Assert.Equal("heuristic", config.GetProperty("routing_mode").GetString());
        Assert.Equal(128, shortClass.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(250, config.GetProperty("heuristic_cheap_max_tokens").GetInt32());
        Assert.False(config.GetProperty("shadow_router_enabled").GetBoolean());
        Assert.False(config.GetProperty("semantic_cache_enabled").GetBoolean());
        var aliases = config.GetProperty("routes").EnumerateArray()
            .Select(route => route.GetProperty("alias").GetString())
            .ToHashSet();
        Assert.Contains("chat.lab", aliases);

        var settings = await _console.GetFromJsonAsync<JsonElement>("/v1/gateway/settings");
        var lab = settings.GetProperty("routes").EnumerateArray()
            .First(route => route.GetProperty("alias").GetString() == "chat.lab");
        var deleted = await _console.DeleteAsync($"/v1/gateway/routes/{lab.GetProperty("id").GetString()}");
        deleted.EnsureSuccessStatusCode();
        var after = await _agent.GetFromJsonAsync<JsonElement>("/v1/gateway/config");
        Assert.DoesNotContain(
            after.GetProperty("routes").EnumerateArray().Select(route => route.GetProperty("alias").GetString()),
            alias => alias == "chat.lab");
    }

    [Fact]
    public async Task Evidence_metadata_is_accepted_without_prompt_fields()
    {
        var allowed = await AuthorizeAsync("chat.cheap", true, "cheap", 0.002m, false);
        (await _agent!.PostAsJsonAsync("/v1/gateway/spend/reserve", new { decision_id = allowed.GetProperty("decision_id").GetString(), estimated_cost_usd = 0.002m })).EnsureSuccessStatusCode();
        var execution = await ExecutionContract.ConsumeAsync(_agent!, allowed);
        using var evidence = new HttpRequestMessage(HttpMethod.Post, "/v1/executions")
        {
            Content = JsonContent.Create(new
            {
                decision_id = allowed.GetProperty("decision_id").GetString(),
                execution_id = execution,
                resource_snapshot_hash = allowed.GetProperty("resource_snapshot_hash").GetString(),
                status = "SUCCESS",
                provider_reference = "openai/gpt-4.1-mini",
                metadata = new Dictionary<string, string>
                {
                    ["source"] = "ai-gateway",
                    ["model_requested"] = "chat.cheap",
                    ["model_used"] = "gpt-4.1-mini",
                    ["cache_hit"] = "false",
                    ["message_hash"] = "abc"
                }
            })
        };
        var response = await _agent!.SendAsync(evidence);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Released_budget_cannot_be_reused_for_execution()
    {
        var d = await AuthorizeAsync("chat.cheap", true, "cheap", 0.002m, false);
        var id = d.GetProperty("decision_id").GetString();
        (await _agent!.PostAsJsonAsync("/v1/gateway/spend/reserve", new { decision_id = id, estimated_cost_usd = 0.002m })).EnsureSuccessStatusCode();
        (await _agent.PostAsJsonAsync("/v1/gateway/spend/release", new { decision_id = id })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await _agent.PostAsJsonAsync("/v1/gateway/spend/reserve", new { decision_id = id, estimated_cost_usd = 0.002m })).StatusCode);
        var operation = d.GetProperty("operation");
        var consumed = await _agent.PostAsJsonAsync("/v1/permits/" + d.GetProperty("permit_id").GetString() + "/consume", new {
            operation_id = operation.GetProperty("id").GetString(), operation_hash = operation.GetProperty("hash").GetString(),
            resource_snapshot_hash = d.GetProperty("resource_snapshot_hash").GetString() });
        Assert.Equal(HttpStatusCode.Conflict, consumed.StatusCode);
    }

    private async Task<JsonElement> AuthorizeAsync(
        string resourceId,
        bool inCatalog,
        string tier,
        decimal cost,
        bool overBudget)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                operation = new { id = Guid.NewGuid().ToString("N"), hash = new string('a', 64) },
                action = "llm.complete",
                resource = new
                {
                    type = "model",
                    id = resourceId,
                    attributes = new
                    {
                        provider = "openai",
                        provider_model = tier == "cheap" ? "gpt-4.1-mini" : "gpt-4.1",
                        input_price_per_million = tier == "cheap" ? 0.40m : 2m,
                        output_price_per_million = tier == "cheap" ? 1.60m : 8m,
                        in_catalog = inCatalog,
                        model_tier = tier,
                        estimated_cost_usd = cost,
                        over_budget = overBudget,
                        region = "global"
                    }
                }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _agent!.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
