using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deleguard.AiGateway;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Deleguard.UnitTests;

public sealed class GatewayPipelineTests
{
    [Fact]
    public async Task Embeddings_never_receive_chat_only_generation_parameters()
    {
        var stub = new StubControlPlane { Decision = Allow() };
        var body = JsonNode.Parse("""{"model":"chat.cheap","input":"example text"}""")!;
        var result = await Execute(await Create(stub).EmbedAsync(body, default, operationId: "embedding-test"));
        Assert.Equal(200, result.Status);
        Assert.Equal(1, stub.ProviderCalls);
        var forwarded = JsonNode.Parse(stub.LastProviderBody!)!;
        Assert.Null(forwarded["max_tokens"]);
        Assert.Null(forwarded["max_completion_tokens"]);
        Assert.Null(forwarded["prompt_cache_key"]);
        Assert.Null(forwarded["tools"]);
        Assert.Equal("example text", forwarded["input"]!.ToString());
    }

    [Fact]
    public async Task Authorize_payload_omits_messages_and_prompt()
    {
        var stub = new StubControlPlane { Decision = Allow() };
        var pipeline = Create(stub);
        var result = await pipeline.ChatAsync(ChatBody("secret prompt text"), CancellationToken.None, operationId: Guid.NewGuid().ToString("N"));
        var executed = await Execute(result);
        Assert.Equal(StatusCodes.Status200OK, executed.Status);
        Assert.NotNull(stub.LastAuthorizeBody);
        Assert.DoesNotContain("secret prompt text", stub.LastAuthorizeBody, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(stub.LastAuthorizeBody!);
        var attributes = document.RootElement.GetProperty("resource").GetProperty("attributes");
        Assert.False(attributes.TryGetProperty("messages", out _));
        Assert.False(attributes.TryGetProperty("prompt", out _));
        Assert.True(attributes.TryGetProperty("message_hash", out _));
        Assert.True(attributes.TryGetProperty("route_reason", out var reason));
        Assert.Equal("static", reason.GetString());
        Assert.False(attributes.GetProperty("prompt_has_tools").GetBoolean());
        Assert.Equal(1, stub.ProviderCalls);
        Assert.Contains("\"max_tokens\":256", stub.LastProviderBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deny_does_not_call_the_provider()
    {
        var stub = new StubControlPlane { Decision = new PdpDecision { Decision = "DENY", DecisionId = "dec_d", Reason = "blocked" } };
        var pipeline = Create(stub);
        var executed = await Execute(await pipeline.ChatAsync(ChatBody("hi"), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status403Forbidden, executed.Status);
        Assert.Equal(0, stub.ProviderCalls);
        Assert.Contains("DENY", executed.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdp_outage_is_503_and_does_not_forward()
    {
        var stub = new StubControlPlane { ConfigStatus = HttpStatusCode.ServiceUnavailable };
        var pipeline = Create(stub);
        var executed = await Execute(await pipeline.ChatAsync(ChatBody("hi"), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, executed.Status);
        Assert.Equal(0, stub.ProviderCalls);
    }

    [Fact]
    public async Task Cache_hit_still_authorizes_and_does_not_replay_without_allow()
    {
        var stub = new StubControlPlane { Decision = Allow() };
        var pipeline = Create(stub);
        var body = ChatBody("same");
        var first = await Execute(await pipeline.ChatAsync(body.DeepClone(), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status200OK, first.Status);
        Assert.Equal(1, stub.ProviderCalls);
        Assert.Equal(1, stub.AuthorizeCalls);

        var second = await Execute(await pipeline.ChatAsync(body.DeepClone(), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status200OK, second.Status);
        Assert.Equal(1, stub.ProviderCalls);
        Assert.Equal(2, stub.AuthorizeCalls);
        Assert.Equal("hit", second.Headers[GatewayHttp.CacheHeader]);

        stub.Decision = new PdpDecision { Decision = "DENY", DecisionId = "dec_no", Reason = "nope" };
        var denied = await Execute(await pipeline.ChatAsync(body.DeepClone(), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status403Forbidden, denied.Status);
        Assert.Equal(1, stub.ProviderCalls);
        Assert.DoesNotContain("hello", denied.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failover_stays_inside_the_allowlist()
    {
        var stub = new StubControlPlane { Decision = Allow(), FailFirstProvider = true };
        var pipeline = Create(stub);
        var executed = await Execute(await pipeline.ChatAsync(ChatBody("hi"), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status200OK, executed.Status);
        Assert.Equal(2, stub.ProviderCalls);
        Assert.Contains("gpt-4.1", stub.ProviderModels);
        Assert.DoesNotContain("gpt-eu", stub.ProviderModels);
    }

    [Fact]
    public async Task Failover_retries_429_inside_the_allowlist()
    {
        var stub = new StubControlPlane { Decision = Allow(), FailFirstProvider = true, FailFirstProviderStatus = HttpStatusCode.TooManyRequests };
        var pipeline = Create(stub);
        var executed = await Execute(await pipeline.ChatAsync(ChatBody("hi"), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status200OK, executed.Status);
        Assert.Equal(2, stub.ProviderCalls);
        Assert.Equal("ALLOW", executed.Headers[GatewayHttp.DecisionHeader]);
        Assert.Equal("gpt-4.1", executed.Headers[GatewayHttp.ModelHeader]);
        Assert.Equal("miss", executed.Headers[GatewayHttp.CacheHeader]);
        Assert.Equal("static", executed.Headers[GatewayHttp.RouteReasonHeader]);
    }

    [Fact]
    public async Task Provider_400_does_not_failover()
    {
        var stub = new StubControlPlane { Decision = Allow(), FailFirstProvider = true, FailFirstProviderStatus = HttpStatusCode.BadRequest };
        var pipeline = Create(stub);
        var executed = await Execute(await pipeline.ChatAsync(ChatBody("hi"), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status400BadRequest, executed.Status);
        Assert.Equal(1, stub.ProviderCalls);
    }

    [Fact]
    public async Task Missing_inbound_and_configured_key_is_401()
    {
        var stub = new StubControlPlane { Decision = Allow() };
        var pipeline = Create(stub, apiKey: "");
        var executed = await Execute(await pipeline.ChatAsync(ChatBody("hi"), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status401Unauthorized, executed.Status);
        Assert.Equal(0, stub.AuthorizeCalls);
        Assert.Equal(0, stub.ProviderCalls);
        Assert.Equal("UNAUTHENTICATED", executed.Headers[GatewayHttp.DecisionHeader]);
    }

    [Fact]
    public async Task Inbound_bearer_is_sent_to_the_pdp_and_identifies_the_agent()
    {
        var stub = new StubControlPlane { Decision = Allow() };
        var pipeline = Create(stub, apiKey: "dgk_fallback", agentId: "");
        var executed = await Execute(await pipeline.ChatAsync(ChatBody("hi"), CancellationToken.None, "dgk_inbound", Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status200OK, executed.Status);
        Assert.Equal("Bearer dgk_inbound", stub.LastPdpAuthorization);
        Assert.NotNull(stub.LastAuthorizeBody);
        using var document = JsonDocument.Parse(stub.LastAuthorizeBody!);
        Assert.Equal("invoice-agent", document.RootElement.GetProperty("agent").GetProperty("id").GetString());
        Assert.Equal("ALLOW", executed.Headers[GatewayHttp.DecisionHeader]);
        Assert.Equal("gpt-4.1-mini", executed.Headers[GatewayHttp.ModelHeader]);
    }

    [Fact]
    public async Task Heuristic_routes_tools_to_strong_without_sending_prompts()
    {
        var stub = new StubControlPlane { Decision = Allow(), RoutingMode = "heuristic" };
        var pipeline = Create(stub);
        var body = JsonNode.Parse(
            """{"model":"chat.cheap","messages":[{"role":"user","content":"secret"}],"tools":[{"type":"function","function":{"name":"lookup"}}]}""")!;
        var executed = await Execute(await pipeline.ChatAsync(body, CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status200OK, executed.Status);
        Assert.Equal("gpt-4.1", executed.Headers[GatewayHttp.ModelHeader]);
        Assert.Equal("tools", executed.Headers[GatewayHttp.RouteReasonHeader]);
        Assert.DoesNotContain("secret", stub.LastAuthorizeBody, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(stub.LastAuthorizeBody!);
        var attributes = document.RootElement.GetProperty("resource").GetProperty("attributes");
        Assert.True(attributes.GetProperty("prompt_has_tools").GetBoolean());
        Assert.Equal("tools", attributes.GetProperty("route_reason").GetString());
        Assert.Contains("\"max_tokens\":2048", stub.LastProviderBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Health_lists_provider_names_without_secrets()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:openai:ApiKey"] = "sk-secret",
                ["Providers:azure:ApiKey"] = "azure-secret"
            })
            .Build();
        var json = JsonSerializer.Serialize(GatewayHealth.Snapshot(configuration, new GatewayOptions { Url = "http://pdp" }), DeleguardControlPlaneClient.Json);
        Assert.Contains("openai", json, StringComparison.Ordinal);
        Assert.Contains("azure", json, StringComparison.Ordinal);
        Assert.Contains("pdp_configured", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("azure-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabled_gateway_fails_closed()
    {
        var stub = new StubControlPlane { Decision = Allow(), Enabled = false };
        var pipeline = Create(stub);
        var executed = await Execute(await pipeline.ChatAsync(ChatBody("hi"), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, executed.Status);
        Assert.Equal(0, stub.ProviderCalls);
        Assert.Equal(0, stub.AuthorizeCalls);
    }

    [Fact]
    public async Task Unauthorized_tools_are_stripped_before_the_provider()
    {
        var stub = new StubControlPlane { Decision = Allow() };
        var pipeline = Create(stub);
        var body = ChatBody("hi");
        body["tools"] = JsonNode.Parse("""[{"type":"function","function":{"name":"wipe_database"}},{"type":"function","function":{"name":"cancel_invoice"}}]""");
        var executed = await Execute(await pipeline.ChatAsync(body, CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status200OK, executed.Status);
        Assert.NotNull(stub.LastProviderBody);
        using var document = JsonDocument.Parse(stub.LastProviderBody!);
        var tools = document.RootElement.GetProperty("tools");
        Assert.Equal(1, tools.GetArrayLength());
        Assert.Equal("cancel_invoice", tools[0].GetProperty("function").GetProperty("name").GetString());
        Assert.True(document.RootElement.TryGetProperty("prompt_cache_key", out _));
        Assert.True(document.RootElement.TryGetProperty("max_tokens", out var max));
        Assert.True(max.GetInt32() <= 2048);
    }

    [Fact]
    public async Task Ledger_outage_is_503_without_provider_call()
    {
        var stub = new StubControlPlane { Decision = Allow(), ReserveStatus = HttpStatusCode.ServiceUnavailable };
        var pipeline = Create(stub);
        var executed = await Execute(await pipeline.ChatAsync(ChatBody("hi"), CancellationToken.None, operationId: Guid.NewGuid().ToString("N")));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, executed.Status);
        Assert.Equal(0, stub.ProviderCalls);
    }

    [Fact]
    public async Task Retry_after_lost_response_does_not_dispatch_again()
    {
        var stub = new StubControlPlane(); var p = Create(stub);
        Assert.Equal(200, (await Execute(await p.ChatAsync(ChatBody("same"), default, operationId: "retry-1"))).Status);
        Assert.Equal(409, (await Execute(await p.ChatAsync(ChatBody("same"), default, operationId: "retry-1"))).Status);
        Assert.Equal(1, stub.ProviderCalls);
    }

    [Fact]
    public async Task Missing_identity_and_multiple_choices_fail_before_provider()
    {
        var stub = new StubControlPlane(); var p = Create(stub);
        Assert.Equal(400, (await Execute(await p.ChatAsync(ChatBody("same"), default))).Status);
        var body = ChatBody("same"); body["n"] = 3;
        Assert.Equal(400, (await Execute(await p.ChatAsync(body, default, operationId: "multiple"))).Status);
        Assert.Equal(0, stub.ProviderCalls);
    }

    [Fact]
    public async Task Cache_is_separated_by_authenticated_credential()
    {
        var stub = new StubControlPlane(); var p = Create(stub);
        Assert.Equal(200, (await Execute(await p.ChatAsync(ChatBody("same"), default, "dgk_one", "one"))).Status);
        Assert.Equal(200, (await Execute(await p.ChatAsync(ChatBody("same"), default, "dgk_two", "two"))).Status);
        Assert.Equal(2, stub.ProviderCalls);
    }

    [Fact]
    public async Task Approval_resumes_same_operation_and_completion_limit_is_capped()
    {
        var stub = new StubControlPlane { Decision = new() { Decision = "REQUIRE_APPROVAL", DecisionId = "pending" } };
        var p = Create(stub); var body = ChatBody("same"); body["max_completion_tokens"] = 90000;
        Assert.Equal(409, (await Execute(await p.ChatAsync(body.DeepClone(), default, operationId: "approval"))).Status);
        stub.Decision = Allow();
        Assert.Equal(200, (await Execute(await p.ChatAsync(body.DeepClone(), default, operationId: "approval"))).Status);
        Assert.Equal(1, stub.AuthorizeCalls); Assert.Equal(1, stub.ProviderCalls);
        var sent = JsonNode.Parse(stub.LastProviderBody!)!;
        Assert.Null(sent["max_tokens"]);
        Assert.InRange(sent["max_completion_tokens"]!.GetValue<int>(), 1, 2048);
    }

    private static GatewayPipeline Create(StubControlPlane stub, string apiKey = "dgk_test", string agentId = "invoice-agent")
    {
        var options = Options.Create(new GatewayOptions
        {
            Url = "http://pdp/",
            ApiKey = apiKey,
            AgentId = agentId
        });
        var pdpClient = new HttpClient(stub, false) { BaseAddress = new Uri("http://pdp/") };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:openai:BaseUrl"] = "https://api.openai.com/v1",
                ["Providers:openai:ApiKey"] = "sk-test"
            })
            .Build();
        return new GatewayPipeline(
            new DeleguardControlPlaneClient(pdpClient, options),
            stub,
            configuration,
            new ExactResponseCache(),
            NullLogger<GatewayPipeline>.Instance,
            options);
    }

    private static JsonNode ChatBody(string user) =>
        JsonNode.Parse($$"""{"model":"chat.cheap","messages":[{"role":"user","content":"{{user}}"}]}""")!;

    private static PdpDecision Allow() => new()
    {
        Decision = "ALLOW",
        DecisionId = "dec_ok",
        Reason = "ok",
        Permit = "permit",
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
    };

    private static async Task<(int Status, string Body, IHeaderDictionary Headers)> Execute(IResult result)
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        http.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        await result.ExecuteAsync(http);
        http.Response.Body.Position = 0;
        using var reader = new StreamReader(http.Response.Body);
        return (http.Response.StatusCode, await reader.ReadToEndAsync(), http.Response.Headers);
    }

    private sealed class StubControlPlane : HttpMessageHandler, IHttpClientFactory
    {
        public PdpDecision Decision { get; set; } = Allow();
        public HttpStatusCode ConfigStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode ReserveStatus { get; set; } = HttpStatusCode.OK;
        public bool Enabled { get; set; } = true;
        public string RoutingMode { get; set; } = "static";
        public bool FailFirstProvider { get; set; }
        public HttpStatusCode FailFirstProviderStatus { get; set; } = HttpStatusCode.InternalServerError;
        public int AuthorizeCalls { get; private set; }
        public int ProviderCalls { get; private set; }
        public string? LastAuthorizeBody { get; private set; }
        public string? LastPdpAuthorization { get; private set; }
        public string? LastProviderBody { get; private set; }
        public List<string> ProviderModels { get; } = [];
        private readonly Dictionary<string, JsonObject> _decisions = [];
        private readonly HashSet<string> _consumed = [];
        private readonly Dictionary<string, string> _operations = [];

        private JsonObject Live(string id)
        {
            var value = _decisions[id].DeepClone().AsObject();
            value["decision"] = Decision.Decision;
            value["permit"] = Decision.Permit;
            if (_consumed.Contains("/v1/permits/perm_" + id + "/consume"))
                value["execution"] = new JsonObject { ["execution_id"] = "exec_" + id };
            return value;
        }

        public HttpClient CreateClient(string name) => new(this, false) { BaseAddress = new Uri("https://api.openai.com/v1/") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.Contains("chat/completions", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("embeddings", StringComparison.OrdinalIgnoreCase))
            {
                LastPdpAuthorization = request.Headers.Authorization?.ToString();
            }
            if (path.Contains("/v1/gateway/config", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("/gateway/config", StringComparison.OrdinalIgnoreCase))
            {
                if (ConfigStatus != HttpStatusCode.OK)
                {
                    return new HttpResponseMessage(ConfigStatus);
                }

                var config = GatewayTestCatalog.Config();
                var payload = new
                {
                    enabled = Enabled,
                    agent_id = config.AgentId,
                    environment = config.Environment,
                    routing_mode = RoutingMode,
                    monthly_budget_usd = 50,
                    remaining_budget_usd = config.RemainingBudgetUsd,
                    default_token_budget_class = config.DefaultTokenBudgetClass,
                    token_budget_classes = config.TokenBudgetClasses,
                    routes = config.Routes,
                    authorized_tools = new[]
                    {
                        new { tool = "invoice-demo__cancel_invoice", action = "invoice.cancel", resource_type = "invoice", risk = "HIGH", side_effect = "irreversible" }
                    }
                };
                return Json(payload);
            }

            if (path.StartsWith("/v1/operations/"))
            {
                var op = path.Split('/').Last();
                return _operations.TryGetValue(op, out var found) ? Json(Live(found)) : new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (path.Contains("/v1/authorize", StringComparison.OrdinalIgnoreCase))
            {
                AuthorizeCalls++;
                LastAuthorizeBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                using var body = JsonDocument.Parse(LastAuthorizeBody!);
                var id = "dec_" + AuthorizeCalls;
                var snapshot = new string('a', 64);
                var result = new { Decision.Decision, Decision.Reason, decision_id = id, Decision.Permit,
                    Decision.ApprovalId, Decision.ExpiresAt, permit_id = "perm_" + id, operation = body.RootElement.GetProperty("operation").Clone(),
                    resource_snapshot_hash = snapshot };
                _decisions[id] = JsonSerializer.SerializeToNode(result, DeleguardControlPlaneClient.Json)!.AsObject();
                _operations[body.RootElement.GetProperty("operation").GetProperty("id").GetString()!] = id;
                return Json(result);
            }
            if (path.StartsWith("/v1/decisions/")) return Json(Live(path.Split('/').Last()));
            if (path.Contains("/v1/permits/") && path.EndsWith("/consume"))
                return Json(new { acquired = _consumed.Add(path), execution_id = "exec_" + path.Split('/')[3] });

            if (path.Contains("/gateway/spend/reserve", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(ReserveStatus)
                {
                    Content = new StringContent("{\"status\":\"reserved\"}", new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" })
                };
            }

            if (path.Contains("/gateway/spend/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/v1/executions", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            if (path.Contains("chat/completions", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("embeddings", StringComparison.OrdinalIgnoreCase))
            {
                ProviderCalls++;
                LastProviderBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(LastProviderBody))
                {
                    using var document = JsonDocument.Parse(LastProviderBody);
                    if (document.RootElement.TryGetProperty("model", out var model))
                    {
                        ProviderModels.Add(model.GetString() ?? string.Empty);
                    }
                }

                if (FailFirstProvider && ProviderCalls == 1)
                {
                    return new HttpResponseMessage(FailFirstProviderStatus);
                }

                return Json(new
                {
                    id = "chatcmpl-1",
                    choices = new[]
                    {
                        new { finish_reason = "stop", message = new { role = "assistant", content = "hello" } }
                    },
                    usage = new { prompt_tokens = 8, completion_tokens = 2 }
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object payload)
        {
            var json = JsonSerializer.Serialize(payload, DeleguardControlPlaneClient.Json);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" })
            };
        }
    }
}
