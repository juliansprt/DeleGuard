using Deleguard.Application.Abstractions;
using Deleguard.Domain.Authorization;

namespace Deleguard.Application.Authorization;

public static class GatewayRecipes
{
    public const string OpenAiChatPairId = "recipe.openai.chat";

    public static IReadOnlyList<GatewayRouteWrite> OpenAiChatPair() =>
    [
        Route("chat.cheap", "openai", "gpt-4.1-mini", GatewayModelTiers.Cheap, "chat", 0, 0.40m, 1.60m),
        Route("chat.strong", "openai", "gpt-4.1", GatewayModelTiers.Strong, "chat", 1, 2.00m, 8.00m),
        Route("chat.default", "openai", "gpt-4.1-mini", GatewayModelTiers.Cheap, "chat", 0, 0.40m, 1.60m)
    ];

    public static bool HasOpenAiChatPair(IEnumerable<GatewayRouteView> routes)
    {
        var aliases = routes.Select(route => route.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return aliases.Contains("chat.cheap") && aliases.Contains("chat.strong") && aliases.Contains("chat.default");
    }

    private static GatewayRouteWrite Route(
        string alias,
        string provider,
        string model,
        string tier,
        string group,
        int order,
        decimal input,
        decimal output) =>
        new(null, alias, provider, model, tier, "global", group, input, output, order, true);
}
