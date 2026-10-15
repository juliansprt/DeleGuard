using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Authorization;

namespace Deleguard.UnitTests;

public sealed class GatewayCatalogRulesTests
{
    [Fact]
    public void Token_classes_must_be_strictly_increasing()
    {
        var patch = ValidPatch() with { ShortMaxOutputTokens = 2048, StandardMaxOutputTokens = 2048 };
        var ex = Assert.Throws<RequestValidationException>(() => GatewayCatalogRules.ValidateSettings(patch));
        Assert.Contains("short < standard < deep", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cascade_attempts_are_bounded_to_two()
    {
        var patch = ValidPatch() with { CascadeMaxAttempts = 3 };
        Assert.Throws<RequestValidationException>(() => GatewayCatalogRules.ValidateSettings(patch));
    }

    [Fact]
    public void Semantic_cache_cannot_be_enabled()
    {
        var ex = Assert.Throws<RequestValidationException>(() => GatewayCatalogRules.RejectSemanticCache(true));
        Assert.Contains("Semantic cache", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlay_keeps_omitted_knobs()
    {
        var current = new GatewaySettingsView(
            true,
            GatewayRoutingModes.Heuristic,
            80,
            40,
            40,
            GatewayTokenBudgets.Deep,
            128,
            1024,
            4096,
            250,
            1500,
            2,
            0.5m,
            120,
            true,
            false,
            []);
        var overlay = GatewaySettingsPatch.Overlay(
            current,
            new GatewaySettingsUpdate(false, GatewayRoutingModes.Static, 10, GatewayTokenBudgets.Short));
        Assert.False(overlay.Enabled);
        Assert.Equal(GatewayRoutingModes.Static, overlay.RoutingMode);
        Assert.Equal(128, overlay.ShortMaxOutputTokens);
        Assert.Equal(250, overlay.HeuristicCheapMaxTokens);
        Assert.Equal(120, overlay.ExactCacheTtlSeconds);
        Assert.False(overlay.ShadowRouterEnabled);
    }

    [Fact]
    public void Route_alias_is_normalized()
    {
        var write = GatewayCatalogRules.NormalizeRoute(new GatewayRouteWrite(
            null,
            " Chat.Lab ",
            "OpenAI",
            "gpt-4.1-mini",
            "CHEAP",
            "Global",
            "Chat",
            0.4m,
            1.6m,
            0,
            true));
        Assert.Equal("chat.lab", write.Alias);
        Assert.Equal("openai", write.Provider);
        Assert.Equal("cheap", write.Tier);
        Assert.Equal("global", write.Region);
        Assert.Equal("chat", write.FallbackGroup);
    }

    [Fact]
    public void OpenAi_chat_pair_recipe_uses_one_fallback_group()
    {
        var routes = GatewayRecipes.OpenAiChatPair();
        Assert.Equal(new[] { "chat.cheap", "chat.strong", "chat.default" }, routes.Select(route => route.Alias).ToArray());
        Assert.All(routes, route =>
        {
            Assert.Equal("openai", route.Provider);
            Assert.Equal("chat", route.FallbackGroup);
            Assert.Equal("global", route.Region);
        });
        Assert.False(GatewayRecipes.HasOpenAiChatPair([]));
        Assert.True(GatewayRecipes.HasOpenAiChatPair(
        [
            new("1", "chat.cheap", "openai", "gpt-4.1-mini", "cheap", "global", "chat", 0.4m, 1.6m, 0, true),
            new("2", "chat.strong", "openai", "gpt-4.1", "strong", "global", "chat", 2m, 8m, 1, true),
            new("3", "chat.default", "openai", "gpt-4.1-mini", "cheap", "global", "chat", 0.4m, 1.6m, 0, true)
        ]));
    }

    private static GatewaySettingsPatch ValidPatch() => new(
        true,
        GatewayRoutingModes.Heuristic,
        50,
        GatewayTokenBudgets.Standard,
        256,
        2048,
        8192,
        400,
        2000,
        2,
        1m,
        300,
        true,
        true);
}
