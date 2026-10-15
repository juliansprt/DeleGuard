namespace Deleguard.AiGateway;

public sealed class GatewayOptions
{
    public const string SectionName = "Deleguard";

    public string Url { get; set; } = "http://127.0.0.1:5080";
    public string ApiKey { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string? ActorId { get; set; }
}

public sealed class ProviderOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class GatewayConfigSnapshot
{
    public bool Enabled { get; init; }
    public string AgentId { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string RoutingMode { get; init; } = "static";
    public decimal MonthlyBudgetUsd { get; init; }
    public decimal RemainingBudgetUsd { get; init; }
    public string DefaultTokenBudgetClass { get; init; } = "standard";
    public IReadOnlyList<GatewayTokenBudget> TokenBudgetClasses { get; init; } = [];
    public int HeuristicCheapMaxTokens { get; init; } = 400;
    public int CascadeSkipToStrongTokens { get; init; } = 2000;
    public int CascadeMaxAttempts { get; init; } = 2;
    public decimal CascadeMaxUsd { get; init; } = 1m;
    public int ExactCacheTtlSeconds { get; init; } = 300;
    public bool PrefixCacheEnabled { get; init; } = true;
    public bool ShadowRouterEnabled { get; init; } = true;
    public bool SemanticCacheEnabled { get; init; }
    public IReadOnlyList<GatewayRoute> Routes { get; init; } = [];
    public IReadOnlyList<GatewayAuthorizedTool> AuthorizedTools { get; init; } = [];

    public int EffectiveHeuristicCheapMaxTokens => HeuristicCheapMaxTokens > 0 ? HeuristicCheapMaxTokens : 400;
    public int EffectiveCascadeSkipToStrongTokens => CascadeSkipToStrongTokens > 0 ? CascadeSkipToStrongTokens : 2000;
    public int EffectiveCascadeMaxAttempts => CascadeMaxAttempts is >= 1 and <= 2 ? CascadeMaxAttempts : 2;
    public int EffectiveExactCacheTtlSeconds => ExactCacheTtlSeconds < 0 ? 300 : ExactCacheTtlSeconds;
}

public sealed class GatewayTokenBudget
{
    public string Name { get; init; } = string.Empty;
    public int MaxOutputTokens { get; init; }
}

public sealed class GatewayRoute
{
    public string Alias { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Tier { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string FallbackGroup { get; init; } = string.Empty;
    public decimal InputPricePerMillion { get; init; }
    public decimal OutputPricePerMillion { get; init; }
    public int SortOrder { get; init; }
}

public sealed class GatewayAuthorizedTool
{
    public string Tool { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string Risk { get; init; } = string.Empty;
    public string SideEffect { get; init; } = string.Empty;
}

public sealed record GatewayOperation(string Id, string Hash);
public sealed record GatewayExecution(string ExecutionId);
public sealed class PdpDecision
{
    public string? PermitId { get; init; }
    public GatewayOperation? Operation { get; init; }
    public string? ResourceSnapshotHash { get; init; }
    public GatewayExecution? Execution { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string DecisionId { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public string? Permit { get; init; }
    public string? ApprovalId { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class GatewayPdpUnavailableException : Exception
{
    public GatewayPdpUnavailableException(string message) : base(message)
    {
    }
}
