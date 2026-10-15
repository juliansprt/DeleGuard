using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using Deleguard.AiGateway;

namespace Deleguard.UnitTests;

public sealed class ModelRouterTests
{
    [Fact]
    public void Heuristic_picks_cheap_for_short_prompts_inside_allowlist()
    {
        var config = GatewayTestCatalog.Config();
        var seed = ModelRouter.Find(config, "chat.default")!;
        var allowlist = ModelRouter.Allowlist(config, seed);
        var chosen = ModelRouter.Choose("heuristic", seed, allowlist, PromptSignals.LengthOnly(40)).Route;
        Assert.Equal("cheap", chosen.Tier);
        Assert.Contains(allowlist, route => string.Equals(route.Model, chosen.Model, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Heuristic_picks_strong_for_long_prompts_inside_allowlist()
    {
        var config = GatewayTestCatalog.Config();
        var seed = ModelRouter.Find(config, "chat.cheap")!;
        var allowlist = ModelRouter.Allowlist(config, seed);
        var chosen = ModelRouter.Choose("heuristic", seed, allowlist, PromptSignals.LengthOnly(4_000)).Route;
        Assert.Equal("strong", chosen.Tier);
    }

    [Fact]
    public void Allowlist_excludes_other_regions()
    {
        var seed = ModelRouter.Find(GatewayTestCatalog.Config(), "chat.cheap")!;
        var mixed = new GatewayConfigSnapshot
        {
            Routes =
            [
                ..GatewayTestCatalog.Config().Routes,
                new GatewayRoute
                {
                    Alias = "chat.eu",
                    Provider = "openai",
                    Model = "gpt-eu",
                    Tier = "cheap",
                    Region = "eu",
                    FallbackGroup = "chat",
                    SortOrder = 0
                }
            ]
        };
        var allowlist = ModelRouter.Allowlist(mixed, seed);
        Assert.DoesNotContain(allowlist, route => string.Equals(route.Model, "gpt-eu", StringComparison.OrdinalIgnoreCase));
        Assert.All(allowlist, route => Assert.Equal("global", route.Region));
    }

    [Fact]
    public void Failover_never_leaves_the_preauthorized_graph()
    {
        var config = GatewayTestCatalog.Config();
        var seed = ModelRouter.Find(config, "chat.cheap")!;
        var allowlist = ModelRouter.Allowlist(config, seed);
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { seed.Model };
        var next = ModelRouter.NextFailover(allowlist, tried);
        Assert.NotNull(next);
        Assert.Contains(allowlist, route => string.Equals(route.Model, next!.Model, StringComparison.OrdinalIgnoreCase));
        tried.Add(next!.Model);
        Assert.Null(ModelRouter.NextFailover(allowlist, tried));
    }

    [Fact]
    public void Cascade_skips_to_strong_when_prompt_is_already_large()
    {
        var config = GatewayTestCatalog.Config();
        var seed = ModelRouter.Find(config, "chat.cheap")!;
        var allowlist = ModelRouter.Allowlist(config, seed);
        var chosen = ModelRouter.Choose("cascade", seed, allowlist, PromptSignals.LengthOnly(8_000)).Route;
        Assert.Equal("strong", chosen.Tier);
    }

    [Fact]
    public void Heuristic_picks_strong_for_short_prompts_with_tools()
    {
        var config = GatewayTestCatalog.Config();
        var seed = ModelRouter.Find(config, "chat.cheap")!;
        var allowlist = ModelRouter.Allowlist(config, seed);
        var body = JsonNode.Parse(
            """{"model":"chat.cheap","messages":[{"role":"user","content":"hi"}],"tools":[{"type":"function","function":{"name":"lookup"}}]}""")!;
        var signals = PromptSignals.FromRequest(body, 12);
        var chosen = ModelRouter.Choose("heuristic", seed, allowlist, signals);
        Assert.True(signals.HasTools);
        Assert.Equal("strong", chosen.Route.Tier);
        Assert.Equal(GatewayRouteReasons.Tools, chosen.Reason);
        Assert.Equal("strong", ModelRouter.ShadowWouldRoute(signals));
    }

    [Fact]
    public void Static_ignores_tools_and_keeps_the_requested_alias()
    {
        var config = GatewayTestCatalog.Config();
        var seed = ModelRouter.Find(config, "chat.cheap")!;
        var allowlist = ModelRouter.Allowlist(config, seed);
        var signals = PromptSignals.FromRequest(
            JsonNode.Parse("""{"tools":[{"type":"function","function":{"name":"x"}}],"messages":[{"role":"user","content":"hi"}]}""")!,
            12);
        var chosen = ModelRouter.Choose("static", seed, allowlist, signals);
        Assert.Equal("cheap", chosen.Route.Tier);
        Assert.Equal(GatewayRouteReasons.Static, chosen.Reason);
    }

    [Fact]
    public void Heuristic_picks_strong_for_json_schema_and_images()
    {
        var config = GatewayTestCatalog.Config();
        var seed = ModelRouter.Find(config, "chat.cheap")!;
        var allowlist = ModelRouter.Allowlist(config, seed);
        var schema = PromptSignals.FromRequest(
            JsonNode.Parse("""{"response_format":{"type":"json_schema"},"messages":[{"role":"user","content":"hi"}]}""")!,
            8);
        var image = PromptSignals.FromRequest(
            JsonNode.Parse(
                """{"messages":[{"role":"user","content":[{"type":"text","text":"see"},{"type":"image_url","image_url":{"url":"https://ex"}}]}]}""")!,
            8);
        Assert.Equal(GatewayRouteReasons.JsonSchema, ModelRouter.Choose("heuristic", seed, allowlist, schema).Reason);
        Assert.Equal(GatewayRouteReasons.Image, ModelRouter.Choose("heuristic", seed, allowlist, image).Reason);
        Assert.Equal("strong", ModelRouter.Choose("cascade", seed, allowlist, schema).Route.Tier);
    }

    [Fact]
    public void Shadow_router_is_independent_of_the_pdp()
    {
        Assert.Equal("cheap", ModelRouter.ShadowWouldRoute(PromptSignals.LengthOnly(10)));
        Assert.Equal("strong", ModelRouter.ShadowWouldRoute(PromptSignals.LengthOnly(4_000)));
    }

    [Fact]
    public void Heuristic_threshold_is_configurable()
    {
        var config = GatewayTestCatalog.Config();
        var seed = ModelRouter.Find(config, "chat.default")!;
        var allowlist = ModelRouter.Allowlist(config, seed);
        var cheap = ModelRouter.Choose("heuristic", seed, allowlist, PromptSignals.LengthOnly(500), heuristicCheapMaxTokens: 800).Route;
        var strong = ModelRouter.Choose("heuristic", seed, allowlist, PromptSignals.LengthOnly(500), heuristicCheapMaxTokens: 400).Route;
        Assert.Equal("cheap", cheap.Tier);
        Assert.Equal("strong", strong.Tier);
    }

    [Fact]
    public void Cascade_skip_threshold_is_configurable()
    {
        var config = GatewayTestCatalog.Config();
        var seed = ModelRouter.Find(config, "chat.cheap")!;
        var allowlist = ModelRouter.Allowlist(config, seed);
        var stillCheap = ModelRouter.Choose("cascade", seed, allowlist, PromptSignals.LengthOnly(1_500), cascadeSkipToStrongTokens: 3_000).Route;
        var skip = ModelRouter.Choose("cascade", seed, allowlist, PromptSignals.LengthOnly(1_500), cascadeSkipToStrongTokens: 1_000).Route;
        Assert.Equal("cheap", stillCheap.Tier);
        Assert.Equal("strong", skip.Tier);
    }
}

public sealed class GatewayMetadataTests
{
    [Fact]
    public void Authorize_attributes_never_include_prompts()
    {
        var attributes = DeleguardControlPlaneClient.MetadataOnlyAttributes(
            true,
            "chat.cheap",
            "chat.cheap",
            "cheap",
            "global",
            12,
            0.002m,
            40m,
            false,
            "standard",
            "static",
            "abc");
        Assert.Empty(GatewayPipeline.AuthorizeProbe(attributes));
        Assert.False(attributes.ContainsKey("messages"));
        Assert.False(attributes.ContainsKey("prompt"));
        Assert.True(attributes.ContainsKey("message_hash"));
        Assert.True(attributes.ContainsKey("estimated_cost_usd"));
        Assert.True(attributes.ContainsKey("over_budget"));
        Assert.True(attributes.ContainsKey("prompt_has_tools"));
        Assert.True(attributes.ContainsKey("prompt_wants_json"));
        Assert.True(attributes.ContainsKey("prompt_has_image"));
        Assert.True(attributes.ContainsKey("route_reason"));
        Assert.Equal(JsonValueKind.False, attributes["prompt_has_tools"].ValueKind);
        Assert.Equal("static", attributes["route_reason"].GetString());
    }

    [Fact]
    public void Exact_cache_requires_a_prior_store()
    {
        var cache = new ExactResponseCache();
        Assert.False(cache.TryGet("missing", 300, out _));
        cache.Set("k", "{\"ok\":true}");
        Assert.True(cache.TryGet("k", 300, out var json));
        Assert.Equal("{\"ok\":true}", json);
        Assert.False(cache.TryGet("k", 0, out _));
    }

    [Fact]
    public void Token_budget_classes_are_explicit()
    {
        Assert.Equal(256, Deleguard.Domain.Authorization.GatewayTokenBudgets.MaxTokens("short"));
        Assert.Equal(2048, Deleguard.Domain.Authorization.GatewayTokenBudgets.MaxTokens("standard"));
        Assert.Equal(8192, Deleguard.Domain.Authorization.GatewayTokenBudgets.MaxTokens("deep"));
    }

    [Fact]
    public void Prefix_hash_uses_system_messages_only()
    {
        var messages = JsonNode.Parse(
            """[{"role":"system","content":"stable"},{"role":"user","content":"dynamic"}]""");
        var prefix = TokenEstimator.PrefixHash(messages);
        var again = TokenEstimator.PrefixHash(JsonNode.Parse(
            """[{"role":"system","content":"stable"},{"role":"user","content":"other"}]"""));
        Assert.Equal(prefix, again);
        Assert.NotEqual(prefix, TokenEstimator.MessageHash(messages));
    }

    [Fact]
    public void Tool_filter_drops_unmapped_tools()
    {
        var body = JsonNode.Parse(
            """{"model":"chat.cheap","tools":[{"type":"function","function":{"name":"wipe_database"}},{"type":"function","function":{"name":"invoice-demo__cancel_invoice"}}]}""")!;
        GatewayToolFilter.Apply(body, new GatewayConfigSnapshot
        {
            AuthorizedTools =
            [
                new GatewayAuthorizedTool
                {
                    Tool = "invoice-demo__cancel_invoice",
                    Action = "invoice.cancel",
                    ResourceType = "invoice",
                    Risk = "HIGH",
                    SideEffect = "irreversible"
                }
            ]
        });
        var tools = body["tools"]!.AsArray();
        Assert.Single(tools);
        Assert.Equal("invoice-demo__cancel_invoice", tools[0]!["function"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Cascade_scorer_escalates_invalid_json_when_json_was_requested()
    {
        var request = JsonNode.Parse("""{"response_format":{"type":"json_object"}}""")!;
        var bad = """{"choices":[{"finish_reason":"stop","message":{"content":"not json"}}]}""";
        var good = """{"choices":[{"finish_reason":"stop","message":{"content":"{\"ok\":true}"}}]}""";
        Assert.True(CascadeScorer.NeedsStrong(request, bad));
        Assert.False(CascadeScorer.NeedsStrong(request, good));
        Assert.True(CascadeScorer.ShouldEscalate("cascade", request, bad, 0, 2));
        Assert.False(CascadeScorer.ShouldEscalate("static", request, bad, 0, 2));
    }

    [Fact]
    public void Cascade_scorer_escalates_refusal_and_content_filter()
    {
        var request = JsonNode.Parse("""{"messages":[{"role":"user","content":"hi"}]}""")!;
        var refusal = """{"choices":[{"finish_reason":"stop","message":{"content":null,"refusal":"no"}}]}""";
        var filtered = """{"choices":[{"finish_reason":"content_filter","message":{"content":""}}]}""";
        Assert.Equal("refusal", CascadeScorer.NeedsStrongReason(request, refusal));
        Assert.Equal("content_filter", CascadeScorer.NeedsStrongReason(request, filtered));
        Assert.True(CascadeScorer.ShouldEscalate("cascade", request, refusal, 0, 2));
        Assert.False(CascadeScorer.ShouldEscalate("static", request, refusal, 0, 2));
    }

    [Fact]
    public void Cascade_scorer_accepts_tool_calls_and_escalates_tools_without_content()
    {
        var request = JsonNode.Parse("""{"tools":[{"type":"function","function":{"name":"lookup"}}]}""")!;
        var calls = """{"choices":[{"finish_reason":"tool_calls","message":{"content":null,"tool_calls":[{"id":"1"}]}}]}""";
        var empty = """{"choices":[{"finish_reason":"stop","message":{"content":""}}]}""";
        var text = """{"choices":[{"finish_reason":"stop","message":{"content":"done"}}]}""";
        Assert.False(CascadeScorer.NeedsStrong(request, calls));
        Assert.Equal("tools", CascadeScorer.NeedsStrongReason(request, empty));
        Assert.False(CascadeScorer.NeedsStrong(request, text));
    }

    [Fact]
    public void Dispatch_max_never_exceeds_capped_max()
    {
        var classes = GatewayTestCatalog.Config().TokenBudgetClasses;
        var shortPrompt = PromptSignals.LengthOnly(10);
        var longPrompt = PromptSignals.LengthOnly(4_000);
        var tools = PromptSignals.FromRequest(
            JsonNode.Parse("""{"tools":[{"type":"function","function":{"name":"x"}}]}""")!,
            10);
        Assert.Equal(256, PromptSignals.DispatchMax(shortPrompt, GatewayRouteReasons.LengthCheap, classes, 400, 2048));
        Assert.Equal(2048, PromptSignals.DispatchMax(longPrompt, GatewayRouteReasons.LengthStrong, classes, 400, 8192));
        Assert.Equal(2048, PromptSignals.DispatchMax(tools, GatewayRouteReasons.Tools, classes, 400, 2048));
        Assert.Equal(100, PromptSignals.DispatchMax(shortPrompt, GatewayRouteReasons.LengthCheap, classes, 400, 100));
        Assert.Equal(8192, PromptSignals.DispatchMax(shortPrompt, GatewayRouteReasons.CascadeSkipLength, classes, 400, 8192));
    }

    [Fact]
    public void Failover_is_429_503_and_5xx_inside_the_allowlist()
    {
        Assert.True(ModelRouter.ShouldFailover(HttpStatusCode.TooManyRequests));
        Assert.True(ModelRouter.ShouldFailover(HttpStatusCode.ServiceUnavailable));
        Assert.True(ModelRouter.ShouldFailover(HttpStatusCode.InternalServerError));
        Assert.False(ModelRouter.ShouldFailover(HttpStatusCode.BadRequest));
        Assert.False(ModelRouter.ShouldFailover(HttpStatusCode.Unauthorized));
    }
}

internal static class GatewayTestCatalog
{
    public static GatewayConfigSnapshot Config() => new()
    {
        Enabled = true,
        AgentId = "invoice-agent",
        Environment = "production",
        RoutingMode = "static",
        RemainingBudgetUsd = 50,
        DefaultTokenBudgetClass = "standard",
        TokenBudgetClasses =
        [
            new GatewayTokenBudget { Name = "short", MaxOutputTokens = 256 },
            new GatewayTokenBudget { Name = "standard", MaxOutputTokens = 2048 },
            new GatewayTokenBudget { Name = "deep", MaxOutputTokens = 8192 }
        ],
        Routes =
        [
            new GatewayRoute { Alias = "chat.cheap", Provider = "openai", Model = "gpt-4.1-mini", Tier = "cheap", Region = "global", FallbackGroup = "chat", SortOrder = 0, InputPricePerMillion = 0.4m, OutputPricePerMillion = 1.6m },
            new GatewayRoute { Alias = "chat.strong", Provider = "openai", Model = "gpt-4.1", Tier = "strong", Region = "global", FallbackGroup = "chat", SortOrder = 1, InputPricePerMillion = 2m, OutputPricePerMillion = 8m },
            new GatewayRoute { Alias = "chat.default", Provider = "openai", Model = "gpt-4.1-mini", Tier = "cheap", Region = "global", FallbackGroup = "chat", SortOrder = 0, InputPricePerMillion = 0.4m, OutputPricePerMillion = 1.6m }
        ]
    };
}
