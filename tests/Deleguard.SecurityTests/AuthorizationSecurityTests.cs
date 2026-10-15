using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Policies;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Deleguard.SecurityTests;

public sealed class AuthorizationPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var local = Environment.GetEnvironmentVariable("DELEGUARD_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(local))
        {
            ConnectionString = local;
            return;
        }

        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("deleguard")
            .WithUsername("deleguard")
            .WithPassword("deleguard")
            .Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

public sealed class AuthorizationSecurityTests : IClassFixture<AuthorizationPostgresFixture>, IAsyncLifetime
{
    private readonly string _connectionString;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public AuthorizationSecurityTests(AuthorizationPostgresFixture fixture)
    {
        _connectionString = fixture.ConnectionString;
    }

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _connectionString,
                    ["Permits:SigningKey"] = "deleguard-dev-permit-hmac-key-change-in-prod-32"
                });
            });
        });
        _client = _factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoAgentKey);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Missing_credential_is_401()
    {
        using var anonymous = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Content = JsonContent.Create(new { action = "invoice.cancel", resource = new { type = "invoice", id = "x" } });
        var response = await anonymous.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Confusable_action_is_unknown_and_denied()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                action = "invoice.cancel.admin",
                resource = new { type = "invoice", id = "FAC-1", attributes = new { status = "draft", amount = 1 } }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("DENY", body.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Agent_id_spoof_is_rejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "other-agent", environment = "production" },
                action = "invoice.cancel",
                resource = new { type = "invoice", id = "FAC-1" }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Idempotency_conflict_when_payload_changes()
    {
        var key = Guid.NewGuid().ToString("N");
        await SendAsync("FAC-A", key);
        using var second = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                action = "invoice.cancel",
                resource = new { type = "invoice", id = "FAC-B", attributes = new { status = "draft", amount = 1 } }
            })
        };
        second.Headers.Add("Idempotency-Key", key);
        var response = await _client!.SendAsync(second);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Changed_snapshot_on_evidence_requires_reauthorization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                action = "invoice.cancel",
                resource = new { type = "invoice", id = "FAC-TOCTOU", attributes = new { status = "draft", amount = 1 } }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var authorized = await _client!.SendAsync(request);
        authorized.EnsureSuccessStatusCode();
        var body = await authorized.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var evidence = new HttpRequestMessage(HttpMethod.Post, "/v1/executions")
        {
            Content = JsonContent.Create(new
            {
                decision_id = body.GetProperty("decision_id").GetString(),
                status = "SUCCESS",
                resource_snapshot_hash = "tampered"
            })
        };
        var response = await _client.SendAsync(evidence);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Product_agent_admin_action_is_deny()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                action = "admin.user.list",
                resource = new { type = "user", id = "usr_x" }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("DENY", body.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Invoice_vertical_slice_still_allows_granted_demo_agent()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                action = "invoice.cancel",
                resource = new
                {
                    type = "invoice",
                    id = "FAC-GRANT",
                    attributes = new { status = "draft", amount = 150000, reportedToTaxAuthority = false }
                }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("ALLOW", body.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Ungranted_action_in_the_same_application_is_deny()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var actionName = "invoice.ungranted." + Guid.NewGuid().ToString("N")[..8];
        db.ActionDefinitions.Add(new ActionDefinition
        {
            Id = "act_ungranted_" + Guid.NewGuid().ToString("N")[..8],
            OrganizationId = DemoSeeder.OrganizationId,
            ApplicationId = DemoSeeder.ApplicationId,
            Name = actionName,
            ResourceType = "invoice",
            Risk = RiskLevel.Low,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                action = actionName,
                resource = new { type = "invoice", id = "FAC-NONE" }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("DENY", body.GetProperty("decision").GetString());
        Assert.Equal("Agent is not allowed to request this action.", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Homonymous_action_in_another_application_is_unknown()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var now = DateTimeOffset.UtcNow;
        var otherAppId = "app_other_" + Guid.NewGuid().ToString("N")[..8];
        db.Applications.Add(new OrganizationApplication
        {
            Id = otherAppId,
            OrganizationId = DemoSeeder.OrganizationId,
            Slug = "other-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Other",
            Kind = ApplicationKinds.Product,
            CreatedAt = now
        });
        var actionName = "invoice.export." + Guid.NewGuid().ToString("N")[..8];
        db.ActionDefinitions.Add(new ActionDefinition
        {
            Id = "act_other_" + Guid.NewGuid().ToString("N")[..8],
            OrganizationId = DemoSeeder.OrganizationId,
            ApplicationId = otherAppId,
            Name = actionName,
            ResourceType = "invoice",
            Risk = RiskLevel.Low,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                action = actionName,
                resource = new { type = "invoice", id = "FAC-OTHER" }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("DENY", body.GetProperty("decision").GetString());
        Assert.Equal("Unknown action.", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Published_policy_without_agent_grant_is_deny()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var actionName = "invoice.nogrant." + suffix;
        var actionId = "act_nogrant_" + suffix;
        var policyId = "pol_nogrant_" + suffix;
        var versionId = "pver_nogrant_" + suffix;
        db.ActionDefinitions.Add(new ActionDefinition
        {
            Id = actionId,
            OrganizationId = DemoSeeder.OrganizationId,
            ApplicationId = DemoSeeder.ApplicationId,
            Name = actionName,
            ResourceType = "invoice",
            Risk = RiskLevel.Low,
            CreatedAt = now
        });
        db.AgentActionGrants.Add(new AgentActionGrant
        {
            OrganizationId = DemoSeeder.OrganizationId,
            ApplicationId = DemoSeeder.ApplicationId,
            AgentId = DemoSeeder.AgentId,
            ActionDefinitionId = actionId,
            CreatedAt = now
        });
        db.Policies.Add(new PolicyDefinition
        {
            Id = policyId,
            OrganizationId = DemoSeeder.OrganizationId,
            ApplicationId = DemoSeeder.ApplicationId,
            Slug = "invoice-nogrant-" + suffix,
            PublishedVersionId = versionId,
            CreatedAt = now
        });
        db.PolicyVersions.Add(new PolicyVersion
        {
            Id = versionId,
            OrganizationId = DemoSeeder.OrganizationId,
            PolicyId = policyId,
            Version = 1,
            Yaml = $"""
                id: invoice-nogrant-{suffix}
                version: 1
                priority: 10
                match:
                  action: {actionName}
                  resource_type: invoice
                rules:
                  - name: allow-all
                    when:
                      field: agent.id
                      op: eq
                      value: invoice-agent
                    effect: ALLOW
                fallback: DENY
                """,
            Action = actionName,
            ResourceType = "invoice",
            Priority = 10,
            Published = true,
            CreatedAt = now,
            PublishedAt = now
        });
        await db.SaveChangesAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                action = actionName,
                resource = new { type = "invoice", id = "FAC-NOGRANT" }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("DENY", body.GetProperty("decision").GetString());
    }

    private Task<HttpResponseMessage> SendAsync(string id, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                action = "invoice.cancel",
                resource = new { type = "invoice", id, attributes = new { status = "draft", amount = 1 } }
            })
        };
        request.Headers.Add("Idempotency-Key", key);
        return _client!.SendAsync(request);
    }

    [Fact]
    public async Task Agent_cannot_call_admin_environments()
    {
        var response = await _client!.GetAsync("/v1/environments");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Console_token_cannot_call_authorize()
    {
        using var console = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        console.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                action = "invoice.cancel",
                resource = new { type = "invoice", id = "x" }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await console.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("invalid_agent_credential", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Agent_cannot_call_admin_agents()
    {
        var response = await _client!.GetAsync("/v1/agents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Agent_cannot_call_admin_actions()
    {
        var response = await _client!.GetAsync("/v1/actions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Console_can_list_and_test_policy_without_writing_a_decision()
    {
        using var console = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        console.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);

        var agents = await console.GetAsync("/v1/agents");
        agents.EnsureSuccessStatusCode();

        using var test = new HttpRequestMessage(HttpMethod.Post, $"/v1/policies/{DemoSeeder.PolicyId}/test")
        {
            Content = JsonContent.Create(new
            {
                expected_effect = "ALLOW",
                request = new
                {
                    agent_id = "invoice-agent",
                    environment = "production",
                    action = "invoice.cancel",
                    resource_type = "invoice",
                    resource_id = "FAC-TEST",
                    attributes = new { status = "draft", amount = 150000, reportedToTaxAuthority = false }
                }
            })
        };
        var response = await console.SendAsync(test);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(body.GetProperty("passed").GetBoolean());
        Assert.Equal("ALLOW", body.GetProperty("actual_effect").GetString());
    }

    [Fact]
    public async Task Mcp_token_cannot_call_authorize()
    {
        using var console = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        console.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
        await console.PostAsJsonAsync("/v1/mcp/settings", new { enabled = true, tools = new[] { "user_list" } });
        var issued = await console.PostAsJsonAsync("/v1/mcp/tokens", new { user_id = DemoSeeder.DemoAdminUserId });
        issued.EnsureSuccessStatusCode();
        var token = (await issued.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())!.GetProperty("token").GetString();

        using var mcp = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                action = "invoice.cancel",
                resource = new { type = "invoice", id = "x" }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await mcp.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Agent_key_cannot_call_mcp()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { } })
        };
        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_token_cannot_read_guard_config()
    {
        using var console = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        console.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
        await console.PostAsJsonAsync("/v1/mcp/settings", new { enabled = true, tools = new[] { "user_list" } });
        var issued = await console.PostAsJsonAsync("/v1/mcp/tokens", new { user_id = DemoSeeder.DemoAdminUserId });
        issued.EnsureSuccessStatusCode();
        var token = (await issued.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())!.GetProperty("token").GetString();

        using var mcp = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await mcp.GetAsync("/v1/guard/config");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invoice_agent_admin_action_is_denied()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = "invoice-agent", environment = "production" },
                actor = new { id = DemoSeeder.DemoAdminUserId, type = "human" },
                action = "admin.user.list",
                resource = new { type = "user", id = "directory" }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("DENY", body.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Console_token_cannot_call_gateway_config()
    {
        using var console = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        console.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
        var response = await console.GetAsync("/v1/gateway/config");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Agent_cannot_call_gateway_settings()
    {
        var response = await _client!.GetAsync("/v1/gateway/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Agent_can_read_gateway_config()
    {
        var response = await _client!.GetAsync("/v1/gateway/config");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(body.GetProperty("enabled").GetBoolean());
        Assert.True(body.GetProperty("routes").GetArrayLength() > 0);
    }
}
