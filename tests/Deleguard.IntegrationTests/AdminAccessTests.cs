using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deleguard.Application.Identity;
using Deleguard.Application.Policies;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Deleguard.IntegrationTests;

public sealed class AdminAccessPostgresFixture : IAsyncLifetime
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

public sealed class AdminAccessTests : IClassFixture<AdminAccessPostgresFixture>, IAsyncLifetime
{
    private readonly string _connectionString;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _console;

    public AdminAccessTests(AdminAccessPostgresFixture fixture)
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
                    ["DemoAuth:ApiKeyPepper"] = "deleguard-dev-pepper",
                    ["DemoAuth:ConsoleToken"] = DemoSeeder.DemoConsoleToken,
                    ["DemoAuth:DemoOrganizationId"] = DemoSeeder.OrganizationId,
                    ["Permits:SigningKey"] = "deleguard-dev-permit-hmac-key-change-in-prod-32"
                });
            });
        });
        _console = _factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        _console.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _console?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Console_lists_seeded_admin_user()
    {
        var response = await _console!.GetAsync("/v1/users");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.EnumerateArray(), item => item.GetProperty("email").GetString() == DemoSeeder.DemoAdminEmail);
    }

    [Fact]
    public async Task Admin_mcp_user_list_is_allow_after_enable_and_token()
    {
        var enabled = await _console!.PostAsJsonAsync("/v1/mcp/settings", new
        {
            enabled = true,
            tools = AdminMcpCatalog.ToolNames
        });
        enabled.EnsureSuccessStatusCode();

        var issued = await _console.PostAsJsonAsync("/v1/mcp/tokens", new { user_id = DemoSeeder.DemoAdminUserId });
        issued.EnsureSuccessStatusCode();
        var secret = await issued.Content.ReadFromJsonAsync<JsonElement>();
        var token = secret.GetProperty("token").GetString();
        Assert.StartsWith("dgm_", token);

        using var mcp = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var init = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = "test", version = "1" } }
            })
        };
        var initialized = await mcp.SendAsync(init);
        initialized.EnsureSuccessStatusCode();

        using var call = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new { name = "user_list", arguments = new { } }
            })
        };
        var listed = await mcp.SendAsync(call);
        listed.EnsureSuccessStatusCode();
        var payload = await listed.Content.ReadFromJsonAsync<JsonElement>();
        var result = payload.GetProperty("result");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("ALLOW", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"decision\":\"DENY\"", text, StringComparison.Ordinal);
        var structured = result.GetProperty("structuredContent");
        Assert.Equal("ALLOW", structured.GetProperty("decision").GetString());
        Assert.Equal("allow", structured.GetProperty("result_type").GetString());
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.True(structured.TryGetProperty("data", out var data));
        Assert.True(data.TryGetProperty("items", out _));
        Assert.True(data.TryGetProperty("next_cursor", out _));
    }

    [Fact]
    public async Task Admin_mcp_tools_list_publishes_closed_schemas()
    {
        using var mcp = await EnableMcpClientAsync();
        using var list = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 4, method = "tools/list", @params = new { } })
        };
        var listed = await mcp.SendAsync(list);
        listed.EnsureSuccessStatusCode();
        var payload = await listed.Content.ReadFromJsonAsync<JsonElement>();
        var tools = payload.GetProperty("result").GetProperty("tools");
        Assert.True(tools.GetArrayLength() > 0);
        foreach (var tool in tools.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("title").GetString()));
            var schema = tool.GetProperty("inputSchema");
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean(), tool.GetProperty("name").GetString());
            Assert.True(tool.TryGetProperty("outputSchema", out _));
            Assert.False(tool.GetProperty("annotations").GetProperty("openWorldHint").GetBoolean());
        }

        var names = tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToHashSet();
        Assert.Contains("webhook_list", names);
        Assert.Contains("webhook_create", names);
        Assert.Contains("webhook_rotate_secret", names);
    }

    [Fact]
    public async Task Admin_mcp_prompts_and_resource_templates_are_listed()
    {
        using var mcp = await EnableMcpClientAsync();
        using var prompts = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 5, method = "prompts/list", @params = new { } })
        };
        var promptResponse = await mcp.SendAsync(prompts);
        promptResponse.EnsureSuccessStatusCode();
        var promptBody = await promptResponse.Content.ReadFromJsonAsync<JsonElement>();
        var names = promptBody.GetProperty("result").GetProperty("prompts").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToHashSet();
        Assert.Contains("review-pending-approvals", names);
        Assert.Contains("safe-publish-checklist", names);
        Assert.Contains("configure-environment", names);
        Assert.Contains("configure-ai-gateway", names);

        using var templates = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 6, method = "resources/templates/list", @params = new { } })
        };
        var templateResponse = await mcp.SendAsync(templates);
        templateResponse.EnsureSuccessStatusCode();
        var templateBody = await templateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var uris = templateBody.GetProperty("result").GetProperty("resourceTemplates").EnumerateArray()
            .Select(item => item.GetProperty("uriTemplate").GetString())
            .ToHashSet();
        Assert.Contains("deleguard://policy/{id}", uris);
        Assert.Contains("deleguard://decision/{id}", uris);
        Assert.Contains("deleguard://approval/{id}", uris);
        Assert.Contains("deleguard://docs/{slug}", uris);
    }

    [Fact]
    public async Task Admin_mcp_docs_get_returns_environment_setup()
    {
        using var mcp = await EnableMcpClientAsync();
        using var call = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 9,
                method = "tools/call",
                @params = new { name = "docs_get", arguments = new { slug = "configure-with-mcp" } }
            })
        };
        var response = await mcp.SendAsync(call);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var structured = payload.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("allow", structured.GetProperty("result_type").GetString());
        var data = structured.GetProperty("data");
        Assert.Equal("configure-with-mcp", data.GetProperty("slug").GetString());
        Assert.Contains("environment_create", data.GetProperty("markdown").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("http://localhost:4321", data.GetProperty("site_url").GetString());
    }

    [Fact]
    public async Task Get_mcp_discovery_does_not_require_a_token()
    {
        using var anonymous = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        var response = await anonymous.GetAsync("/mcp");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("streamable-http", body.GetProperty("transport").GetString());
        Assert.Equal("2025-03-26", body.GetProperty("protocol").GetString());
    }

    [Fact]
    public async Task Seeded_demo_mcp_token_initializes()
    {
        using var mcp = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoMcpToken);
        using var init = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = "test", version = "1" } }
            })
        };
        var initialized = await mcp.SendAsync(init);
        initialized.EnsureSuccessStatusCode();
        var body = await initialized.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("deleguard-admin", body.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
    }

    private async Task<HttpClient> EnableMcpClientAsync()
    {
        var enabled = await _console!.PostAsJsonAsync("/v1/mcp/settings", new
        {
            enabled = true,
            tools = AdminMcpCatalog.ToolNames
        });
        enabled.EnsureSuccessStatusCode();
        var issued = await _console.PostAsJsonAsync("/v1/mcp/tokens", new { user_id = DemoSeeder.DemoAdminUserId });
        issued.EnsureSuccessStatusCode();
        var token = (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var mcp = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return mcp;
    }

    [Fact]
    public async Task Require_approval_does_not_rotate_key()
    {
        var created = await _console!.PostAsJsonAsync("/v1/users", new
        {
            email = $"operator-{Guid.NewGuid():N}@demo.local",
            display_name = "Operator",
            roles = new[] { "operator" },
            password = "OperatorPass!23"
        });
        created.EnsureSuccessStatusCode();
        var user = await created.Content.ReadFromJsonAsync<JsonElement>();

        var enabled = await _console.PostAsJsonAsync("/v1/mcp/settings", new
        {
            enabled = true,
            tools = AdminMcpCatalog.ToolNames
        });
        enabled.EnsureSuccessStatusCode();
        var issued = await _console.PostAsJsonAsync("/v1/mcp/tokens", new { user_id = user.GetProperty("user_id").GetString() });
        issued.EnsureSuccessStatusCode();
        var token = (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();

        var before = await _console.GetFromJsonAsync<JsonElement>("/v1/agents");
        var invoice = before.EnumerateArray().First(item => item.GetProperty("external_id").GetString() == "invoice-agent");
        var prefix = invoice.GetProperty("key_prefix").GetString();

        using var mcp = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var call = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new
                {
                    name = "agent_rotate_key",
                    arguments = new { agent_id = invoice.GetProperty("agent_id").GetString() }
                }
            })
        };
        var rotated = await mcp.SendAsync(call);
        rotated.EnsureSuccessStatusCode();
        var payload = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        var result = payload.GetProperty("result");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("REQUIRE_APPROVAL", text, StringComparison.Ordinal);
        Assert.False(result.GetProperty("isError").GetBoolean());
        var structured = result.GetProperty("structuredContent");
        Assert.Equal("require_approval", structured.GetProperty("result_type").GetString());
        Assert.Contains("/approvals/", structured.GetProperty("next_action").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("data").ValueKind);

        var after = await _console.GetFromJsonAsync<JsonElement>("/v1/agents");
        var invoiceAfter = after.EnumerateArray().First(item => item.GetProperty("external_id").GetString() == "invoice-agent");
        Assert.Equal(prefix, invoiceAfter.GetProperty("key_prefix").GetString());
    }

    [Fact]
    public async Task Disabling_an_agent_fails_closed_on_authorize()
    {
        var created = await _console!.PostAsJsonAsync("/v1/agents", new
        {
            external_id = "disable-probe-" + Guid.NewGuid().ToString("N")[..8],
            display_name = "Disable Probe",
            environment = "production"
        });
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Store this API key; it is shown only once.", body.GetProperty("message").GetString());
        var agentId = body.GetProperty("agent_id").GetString();
        var apiKey = body.GetProperty("api_key").GetString();

        var disable = await _console.PostAsync($"/v1/agents/{agentId}/disable", null);
        disable.EnsureSuccessStatusCode();
        var disableBody = await disable.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ALLOW", disableBody.GetProperty("decision").GetString());
        Assert.False(disableBody.GetProperty("enabled").GetBoolean());

        var listed = await _console.GetFromJsonAsync<JsonElement>("/v1/agents");
        var probe = listed.EnumerateArray().First(item => item.GetProperty("agent_id").GetString() == agentId);
        Assert.False(probe.GetProperty("enabled").GetBoolean());

        using var agent = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                agent = new { id = body.GetProperty("external_id").GetString(), environment = "production" },
                action = "invoice.cancel",
                resource = new { type = "invoice", id = "FAC-DISABLE", attributes = new { status = "draft", amount = 1 } }
            })
        };
        request.Headers.Add("Idempotency-Key", "disable-probe-" + Guid.NewGuid().ToString("N"));
        var response = await agent.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Administration_mcp_agent_cannot_be_disabled()
    {
        var response = await _console!.PostAsync($"/v1/agents/{DemoSeeder.AdminMcpAgentId}/disable", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Console_lists_seeded_production_environment()
    {
        var response = await _console!.GetAsync("/v1/environments");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var production = body.EnumerateArray().Single(item => item.GetProperty("name").GetString() == "production");
        Assert.Equal(DemoSeeder.EnvironmentId, production.GetProperty("environment_id").GetString());
        Assert.True(production.GetProperty("agent_count").GetInt32() >= 1);
    }

    [Fact]
    public async Task Environment_crud_creates_renames_and_deletes_empty_env()
    {
        var slug = "staging-" + Guid.NewGuid().ToString("N")[..8];
        var created = await _console!.PostAsJsonAsync("/v1/environments", new { name = slug });
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("environment_id").GetString();
        Assert.Equal(slug, body.GetProperty("name").GetString());
        Assert.Equal(0, body.GetProperty("agent_count").GetInt32());

        var renamedSlug = "qa-" + Guid.NewGuid().ToString("N")[..8];
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/v1/environments/{id}")
        {
            Content = JsonContent.Create(new { name = renamedSlug })
        };
        var renamed = await _console.SendAsync(patch);
        renamed.EnsureSuccessStatusCode();
        var renamedBody = await renamed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(renamedSlug, renamedBody.GetProperty("name").GetString());

        var deleted = await _console.DeleteAsync($"/v1/environments/{id}");
        deleted.EnsureSuccessStatusCode();
        var deletedBody = await deleted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ALLOW", deletedBody.GetProperty("decision").GetString());
        Assert.True(deletedBody.GetProperty("deleted").GetBoolean());

        var listed = await _console.GetFromJsonAsync<JsonElement>("/v1/environments");
        Assert.DoesNotContain(listed.EnumerateArray(), item => item.GetProperty("environment_id").GetString() == id);
    }

    [Fact]
    public async Task Deleting_environment_with_agents_fails_closed()
    {
        var listed = await _console!.GetFromJsonAsync<JsonElement>("/v1/environments");
        var production = listed.EnumerateArray().First(item => item.GetProperty("name").GetString() == "production");
        var response = await _console.DeleteAsync($"/v1/environments/{production.GetProperty("environment_id").GetString()}");
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Operator_environment_delete_requires_approval_and_does_not_mutate()
    {
        var slug = "ephemeral-" + Guid.NewGuid().ToString("N")[..8];
        var created = await _console!.PostAsJsonAsync("/v1/environments", new { name = slug });
        created.EnsureSuccessStatusCode();
        var env = await created.Content.ReadFromJsonAsync<JsonElement>();
        var environmentId = env.GetProperty("environment_id").GetString();

        var userCreated = await _console.PostAsJsonAsync("/v1/users", new
        {
            email = $"operator-env-{Guid.NewGuid():N}@demo.local",
            display_name = "Operator Env",
            roles = new[] { "operator" },
            password = "OperatorPass!23"
        });
        userCreated.EnsureSuccessStatusCode();
        var user = await userCreated.Content.ReadFromJsonAsync<JsonElement>();

        var enabled = await _console.PostAsJsonAsync("/v1/mcp/settings", new
        {
            enabled = true,
            tools = AdminMcpCatalog.ToolNames
        });
        enabled.EnsureSuccessStatusCode();
        var issued = await _console.PostAsJsonAsync("/v1/mcp/tokens", new { user_id = user.GetProperty("user_id").GetString() });
        issued.EnsureSuccessStatusCode();
        var token = (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();

        using var mcp = _factory!.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var call = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 8,
                method = "tools/call",
                @params = new
                {
                    name = "environment_delete",
                    arguments = new { environment_id = environmentId }
                }
            })
        };
        var deleted = await mcp.SendAsync(call);
        deleted.EnsureSuccessStatusCode();
        var payload = await deleted.Content.ReadFromJsonAsync<JsonElement>();
        var structured = payload.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("require_approval", structured.GetProperty("result_type").GetString());
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("data").ValueKind);

        var stillThere = await _console.GetAsync($"/v1/environments/{environmentId}");
        stillThere.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Console_lists_and_creates_protected_action()
    {
        var listed = await _console!.GetFromJsonAsync<JsonElement>("/v1/actions");
        Assert.Contains(listed.EnumerateArray(), item => item.GetProperty("name").GetString() == "invoice.cancel");
        Assert.DoesNotContain(listed.EnumerateArray(), item => item.GetProperty("name").GetString() == "admin.action.create");

        var name = "email.send." + Guid.NewGuid().ToString("N")[..8];
        var created = await _console.PostAsJsonAsync("/v1/actions", new
        {
            name,
            resource_type = "email",
            risk = "HIGH",
            agent_ids = new[] { DemoSeeder.AgentId }
        });
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(name, body.GetProperty("name").GetString());
        Assert.Equal("email", body.GetProperty("resource_type").GetString());
        Assert.Equal("HIGH", body.GetProperty("risk").GetString());

        var invalid = await _console.PostAsJsonAsync("/v1/actions", new
        {
            name = "Invoice.Cancel",
            resource_type = "invoice",
            risk = "HIGH"
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Admin_mcp_policy_create_adds_an_unpublished_product_policy()
    {
        using var mcp = await EnableMcpClientAsync();
        using var listedTools = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 10, method = "tools/list", @params = new { } })
        };
        var toolsResponse = await mcp.SendAsync(listedTools);
        toolsResponse.EnsureSuccessStatusCode();
        var toolNames = (await toolsResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToHashSet();
        Assert.Contains("policy_create", toolNames);

        var slug = "pendulo-mcp-" + Guid.NewGuid().ToString("N")[..8];
        var yaml = $"""
            id: {slug}
            version: 1
            priority: 40
            match:
              action: invoice.cancel
              resource_type: invoice
            rules:
              - name: deny-all
                when:
                  field: agent.id
                  op: eq
                  value: nobody
                effect: DENY
                reason: Draft product policy created via Administration MCP
            fallback: DENY
            """;

        using var create = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 11,
                method = "tools/call",
                @params = new { name = "policy_create", arguments = new { yaml, agent_ids = new[] { DemoSeeder.AgentId } } }
            })
        };
        var created = await mcp.SendAsync(create);
        created.EnsureSuccessStatusCode();
        var createdPayload = await created.Content.ReadFromJsonAsync<JsonElement>();
        var structured = createdPayload.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("allow", structured.GetProperty("result_type").GetString());
        Assert.False(createdPayload.GetProperty("result").GetProperty("isError").GetBoolean());
        var data = structured.GetProperty("data");
        Assert.False(data.GetProperty("published").GetBoolean());
        Assert.Equal(slug, data.GetProperty("slug").GetString());
        var policyId = data.GetProperty("policy_id").GetString();
        Assert.StartsWith("pol_", policyId);
        Assert.StartsWith("pver_", data.GetProperty("version_id").GetString());
        Assert.Equal($"deleguard://policy/{policyId}", data.GetProperty("yaml_resource").GetString());

        using var list = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 12,
                method = "tools/call",
                @params = new { name = "policy_list", arguments = new { kind = "product" } }
            })
        };
        var listed = await mcp.SendAsync(list);
        listed.EnsureSuccessStatusCode();
        var listedPayload = await listed.Content.ReadFromJsonAsync<JsonElement>();
        var items = listedPayload.GetProperty("result").GetProperty("structuredContent").GetProperty("data").GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item =>
            item.GetProperty("policy_id").GetString() == policyId &&
            item.GetProperty("slug").GetString() == slug &&
            item.GetProperty("published").GetBoolean() == false);

        var consoleListed = await _console!.GetFromJsonAsync<JsonElement>("/v1/policies");
        Assert.Contains(consoleListed.EnumerateArray(), item => item.GetProperty("policy_id").GetString() == policyId);

        // Publication re-runs persisted expectations, regardless of the entrypoint used.
        var blocked = await _console.PostAsJsonAsync($"/v1/policies/{policyId}/publish", new { version_id = data.GetProperty("version_id").GetString() });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, blocked.StatusCode);
        using (var scope = _factory!.Services.CreateScope())
        {
            var policies = scope.ServiceProvider.GetRequiredService<PolicyAdminHandler>();
            await policies.SaveCaseAsync("Unmatched agent is denied", new PolicyTestCommand(
                DemoSeeder.OrganizationId, policyId!, yaml, "DENY", "invoice-agent", "production",
                "invoice.cancel", "invoice", "test-invoice", null, null), default);
        }
        using (var scope = _factory!.Services.CreateScope())
        {
            var policies = scope.ServiceProvider.GetRequiredService<PolicyAdminHandler>();
            var savedCases = await policies.ListCasesAsync(DemoSeeder.OrganizationId, policyId!, default);
            Assert.Equal("DENY", Assert.Single(savedCases).ExpectedEffect);
        }
        // The first consumed command failed. A human verifies that it did not publish before starting a new operation.
        var incidents = await _console.GetFromJsonAsync<JsonElement>("/v1/incidents?administration=true");
        foreach (var incident in incidents.EnumerateArray())
            (await _console.PostAsJsonAsync("/v1/incidents/" + incident.GetProperty("execution_id").GetString() + "?administration=true",
                new { resolution = "Confirmed the validation failure left the policy unpublished; saved cases are now ready." })).EnsureSuccessStatusCode();
        var published = await _console.PostAsJsonAsync($"/v1/policies/{policyId}/publish", new { version_id = data.GetProperty("version_id").GetString() });
        published.EnsureSuccessStatusCode();
        using (var scope = _factory!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PolicyAdminHandler>()
                .DeleteAsync(DemoSeeder.OrganizationId, policyId!, default);
        }
    }
}
