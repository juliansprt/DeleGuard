namespace Deleguard.Domain.Authorization;

public static class GatewayRoutingModes
{
    public const string Static = "static";
    public const string Heuristic = "heuristic";
    public const string Cascade = "cascade";

    public static readonly IReadOnlyList<string> All = [Static, Heuristic, Cascade];

    public static bool IsKnown(string? value) =>
        All.Contains(value, StringComparer.Ordinal);
}

public static class GatewayModelTiers
{
    public const string Cheap = "cheap";
    public const string Strong = "strong";
    public const string Embed = "embed";

    public static readonly IReadOnlyList<string> All = [Cheap, Strong, Embed];

    public static bool IsKnown(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}

public static class GatewaySpendStatuses
{
    public const string Reserved = "reserved";
    public const string Settled = "settled";
    public const string Released = "released";
}

public static class GatewayTokenBudgets
{
    public const string Short = "short";
    public const string Standard = "standard";
    public const string Deep = "deep";
    public const int DefaultShort = 256;
    public const int DefaultStandard = 2048;
    public const int DefaultDeep = 8192;
    public const int MinOutputTokens = 1;
    public const int MaxOutputTokensBound = 128_000;

    public static readonly IReadOnlyDictionary<string, int> MaxOutputTokens = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [Short] = DefaultShort,
        [Standard] = DefaultStandard,
        [Deep] = DefaultDeep
    };

    public static bool IsKnown(string? value) =>
        value is not null && MaxOutputTokens.ContainsKey(value);

    public static int MaxTokens(string? value) =>
        value is not null && MaxOutputTokens.TryGetValue(value, out var tokens) ? tokens : MaxOutputTokens[Standard];
}

public static class GatewayRoutingKnobs
{
    public const int HeuristicCheapMaxTokens = 400;
    public const int CascadeSkipToStrongTokens = 2000;
    public const int CascadeMaxAttempts = 2;
    public const int MaxCascadeAttempts = 2;
    public const decimal CascadeMaxUsd = 1m;
    public const int ExactCacheTtlSeconds = 300;
    public const int MaxExactCacheTtlSeconds = 86_400;
}

public static class GatewaySideEffects
{
    public static string FromRisk(Deleguard.Domain.Common.RiskLevel risk) => risk switch
    {
        Deleguard.Domain.Common.RiskLevel.Low => "read_only",
        Deleguard.Domain.Common.RiskLevel.Medium => "idempotent",
        _ => "irreversible"
    };
}

public sealed class OrganizationGatewaySettings
{
    public required string OrganizationId { get; init; }
    public bool Enabled { get; set; }
    public required string RoutingMode { get; set; }
    public decimal MonthlyBudgetUsd { get; set; }
    public required string DefaultTokenBudgetClass { get; set; }
    public int ShortMaxOutputTokens { get; set; } = GatewayTokenBudgets.DefaultShort;
    public int StandardMaxOutputTokens { get; set; } = GatewayTokenBudgets.DefaultStandard;
    public int DeepMaxOutputTokens { get; set; } = GatewayTokenBudgets.DefaultDeep;
    public int HeuristicCheapMaxTokens { get; set; } = GatewayRoutingKnobs.HeuristicCheapMaxTokens;
    public int CascadeSkipToStrongTokens { get; set; } = GatewayRoutingKnobs.CascadeSkipToStrongTokens;
    public int CascadeMaxAttempts { get; set; } = GatewayRoutingKnobs.CascadeMaxAttempts;
    public decimal CascadeMaxUsd { get; set; } = GatewayRoutingKnobs.CascadeMaxUsd;
    public int ExactCacheTtlSeconds { get; set; } = GatewayRoutingKnobs.ExactCacheTtlSeconds;
    public bool PrefixCacheEnabled { get; set; } = true;
    public bool ShadowRouterEnabled { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }

    public static OrganizationGatewaySettings Create(string organizationId, DateTimeOffset now) => new()
    {
        OrganizationId = organizationId,
        Enabled = false,
        RoutingMode = GatewayRoutingModes.Static,
        MonthlyBudgetUsd = 50m,
        DefaultTokenBudgetClass = GatewayTokenBudgets.Standard,
        ShortMaxOutputTokens = GatewayTokenBudgets.DefaultShort,
        StandardMaxOutputTokens = GatewayTokenBudgets.DefaultStandard,
        DeepMaxOutputTokens = GatewayTokenBudgets.DefaultDeep,
        HeuristicCheapMaxTokens = GatewayRoutingKnobs.HeuristicCheapMaxTokens,
        CascadeSkipToStrongTokens = GatewayRoutingKnobs.CascadeSkipToStrongTokens,
        CascadeMaxAttempts = GatewayRoutingKnobs.CascadeMaxAttempts,
        CascadeMaxUsd = GatewayRoutingKnobs.CascadeMaxUsd,
        ExactCacheTtlSeconds = GatewayRoutingKnobs.ExactCacheTtlSeconds,
        PrefixCacheEnabled = true,
        ShadowRouterEnabled = true,
        UpdatedAt = now
    };
}

public sealed class GatewayModelRoute
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string Alias { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public required string Tier { get; set; }
    public required string Region { get; set; }
    public required string FallbackGroup { get; set; }
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class GatewaySpendReservation
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string DecisionId { get; init; }
    public required string AgentId { get; init; }
    public decimal ReservedUsd { get; init; }
    public decimal? SettledUsd { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? SettledAt { get; set; }
}
