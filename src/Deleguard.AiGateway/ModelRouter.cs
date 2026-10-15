namespace Deleguard.AiGateway;

public sealed record RouteDecision(GatewayRoute Route, string Reason);

public static class ModelRouter
{
    public static GatewayRoute? Find(GatewayConfigSnapshot config, string requested)
    {
        return config.Routes.FirstOrDefault(route =>
                   string.Equals(route.Alias, requested, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(route.Model, requested, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<GatewayRoute> Allowlist(GatewayConfigSnapshot config, GatewayRoute seed) =>
        config.Routes
            .Where(route =>
                string.Equals(route.FallbackGroup, seed.FallbackGroup, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(route.Region, seed.Region, StringComparison.OrdinalIgnoreCase))
            .GroupBy(route => route.Model, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(route => route.SortOrder).First())
            .OrderBy(route => route.SortOrder)
            .ThenBy(route => route.Alias, StringComparer.Ordinal)
            .ToList();

    public static string ShadowWouldRoute(
        PromptSignals signals,
        int heuristicCheapMaxTokens = 400) =>
        DecideTarget("heuristic", signals, heuristicCheapMaxTokens, 2000).Tier;

    public static (string Tier, string Reason) DecideTarget(
        string routingMode,
        PromptSignals signals,
        int heuristicCheapMaxTokens = 400,
        int cascadeSkipToStrongTokens = 2000)
    {
        if (string.Equals(routingMode, "static", StringComparison.OrdinalIgnoreCase))
        {
            return ("static", GatewayRouteReasons.Static);
        }

        if (signals.HasImage)
        {
            return ("strong", GatewayRouteReasons.Image);
        }

        if (signals.WantsJsonSchema)
        {
            return ("strong", GatewayRouteReasons.JsonSchema);
        }

        if (signals.HasTools)
        {
            return ("strong", GatewayRouteReasons.Tools);
        }

        if (string.Equals(routingMode, "heuristic", StringComparison.OrdinalIgnoreCase))
        {
            if (signals.EstimatedInputTokens <= heuristicCheapMaxTokens)
            {
                return ("cheap", GatewayRouteReasons.LengthCheap);
            }

            return ("strong", GatewayRouteReasons.LengthStrong);
        }

        if (string.Equals(routingMode, "cascade", StringComparison.OrdinalIgnoreCase))
        {
            if (signals.EstimatedInputTokens > cascadeSkipToStrongTokens)
            {
                return ("strong", GatewayRouteReasons.CascadeSkipLength);
            }

            return ("cheap", GatewayRouteReasons.LengthCheap);
        }

        return ("static", GatewayRouteReasons.Static);
    }

    public static RouteDecision Choose(
        string routingMode,
        GatewayRoute requested,
        IReadOnlyList<GatewayRoute> allowlist,
        PromptSignals signals,
        int heuristicCheapMaxTokens = 400,
        int cascadeSkipToStrongTokens = 2000)
    {
        var (tier, reason) = DecideTarget(routingMode, signals, heuristicCheapMaxTokens, cascadeSkipToStrongTokens);
        if (allowlist.Count == 0)
        {
            return new RouteDecision(requested, reason);
        }

        if (string.Equals(tier, "static", StringComparison.OrdinalIgnoreCase))
        {
            var match = allowlist.FirstOrDefault(route =>
                            string.Equals(route.Model, requested.Model, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(route.Alias, requested.Alias, StringComparison.OrdinalIgnoreCase))
                        ?? requested;
            return new RouteDecision(match, GatewayRouteReasons.Static);
        }

        if (string.Equals(tier, "cheap", StringComparison.OrdinalIgnoreCase))
        {
            var cheap = Cheapest(allowlist, "cheap") ?? Cheapest(allowlist, null) ?? requested;
            return new RouteDecision(cheap, reason);
        }

        var strong = FirstTier(allowlist, "strong") ?? requested;
        return new RouteDecision(strong, reason);
    }

    public static GatewayRoute? NextFailover(IReadOnlyList<GatewayRoute> allowlist, IReadOnlySet<string> tried) =>
        allowlist.FirstOrDefault(route => !tried.Contains(route.Model));

    public static bool ShouldFailover(System.Net.HttpStatusCode status)
    {
        var code = (int)status;
        return code == 429 || code >= 500;
    }

    public static decimal EstimateCostUsd(GatewayRoute route, int inputTokens, int outputTokens) =>
        decimal.Round(
            (inputTokens / 1_000_000m) * route.InputPricePerMillion +
            (outputTokens / 1_000_000m) * route.OutputPricePerMillion,
            6);

    public static GatewayRoute? Cheapest(IReadOnlyList<GatewayRoute> allowlist, string? tier)
    {
        var pool = string.IsNullOrWhiteSpace(tier)
            ? allowlist
            : allowlist.Where(route => string.Equals(route.Tier, tier, StringComparison.OrdinalIgnoreCase)).ToList();
        return pool.OrderBy(route => route.InputPricePerMillion + route.OutputPricePerMillion)
            .ThenBy(route => route.SortOrder)
            .FirstOrDefault();
    }

    public static GatewayRoute? FirstTier(IReadOnlyList<GatewayRoute> allowlist, string tier) =>
        allowlist.FirstOrDefault(route => string.Equals(route.Tier, tier, StringComparison.OrdinalIgnoreCase));
}
