using Deleguard.Domain.Authorization;

namespace Deleguard.Application.Abstractions;

public sealed record GatewayRouteView(
    string Id,
    string Alias,
    string Provider,
    string Model,
    string Tier,
    string Region,
    string FallbackGroup,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    int SortOrder,
    bool Enabled);

public sealed record GatewaySettingsView(
    bool Enabled,
    string RoutingMode,
    decimal MonthlyBudgetUsd,
    decimal RemainingBudgetUsd,
    decimal UsedBudgetUsd,
    string DefaultTokenBudgetClass,
    int ShortMaxOutputTokens,
    int StandardMaxOutputTokens,
    int DeepMaxOutputTokens,
    int HeuristicCheapMaxTokens,
    int CascadeSkipToStrongTokens,
    int CascadeMaxAttempts,
    decimal CascadeMaxUsd,
    int ExactCacheTtlSeconds,
    bool PrefixCacheEnabled,
    bool ShadowRouterEnabled,
    IReadOnlyList<GatewayRouteView> Routes)
{
    public bool SemanticCacheEnabled => false;
}

public sealed record GatewaySettingsUpdate(
    bool Enabled,
    string RoutingMode,
    decimal MonthlyBudgetUsd,
    string DefaultTokenBudgetClass,
    int? ShortMaxOutputTokens = null,
    int? StandardMaxOutputTokens = null,
    int? DeepMaxOutputTokens = null,
    int? HeuristicCheapMaxTokens = null,
    int? CascadeSkipToStrongTokens = null,
    int? CascadeMaxAttempts = null,
    decimal? CascadeMaxUsd = null,
    int? ExactCacheTtlSeconds = null,
    bool? PrefixCacheEnabled = null,
    bool? ShadowRouterEnabled = null,
    bool? SemanticCacheEnabled = null);

public sealed record GatewaySettingsPatch(
    bool Enabled,
    string RoutingMode,
    decimal MonthlyBudgetUsd,
    string DefaultTokenBudgetClass,
    int ShortMaxOutputTokens,
    int StandardMaxOutputTokens,
    int DeepMaxOutputTokens,
    int HeuristicCheapMaxTokens,
    int CascadeSkipToStrongTokens,
    int CascadeMaxAttempts,
    decimal CascadeMaxUsd,
    int ExactCacheTtlSeconds,
    bool PrefixCacheEnabled,
    bool ShadowRouterEnabled)
{
    public static GatewaySettingsPatch Overlay(GatewaySettingsView current, GatewaySettingsUpdate update) =>
        new(
            update.Enabled,
            update.RoutingMode,
            update.MonthlyBudgetUsd,
            update.DefaultTokenBudgetClass,
            update.ShortMaxOutputTokens ?? current.ShortMaxOutputTokens,
            update.StandardMaxOutputTokens ?? current.StandardMaxOutputTokens,
            update.DeepMaxOutputTokens ?? current.DeepMaxOutputTokens,
            update.HeuristicCheapMaxTokens ?? current.HeuristicCheapMaxTokens,
            update.CascadeSkipToStrongTokens ?? current.CascadeSkipToStrongTokens,
            update.CascadeMaxAttempts ?? current.CascadeMaxAttempts,
            update.CascadeMaxUsd ?? current.CascadeMaxUsd,
            update.ExactCacheTtlSeconds ?? current.ExactCacheTtlSeconds,
            update.PrefixCacheEnabled ?? current.PrefixCacheEnabled,
            update.ShadowRouterEnabled ?? current.ShadowRouterEnabled);
}

public sealed record GatewayRouteWrite(
    string? Id,
    string Alias,
    string Provider,
    string Model,
    string Tier,
    string Region,
    string FallbackGroup,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    int SortOrder,
    bool Enabled);

public sealed record GatewaySpendView(
    string Id,
    string DecisionId,
    decimal ReservedUsd,
    decimal? SettledUsd,
    string Status);

public interface IGatewayDirectory
{
    Task<GatewaySettingsView> GetSettingsAsync(string organizationId, CancellationToken cancellationToken);
    Task<GatewaySettingsView> UpdateSettingsAsync(
        string organizationId,
        GatewaySettingsPatch patch,
        CancellationToken cancellationToken);
    Task<GatewaySettingsView> UpsertRouteAsync(
        string organizationId,
        GatewayRouteWrite route,
        CancellationToken cancellationToken);
    Task<GatewaySettingsView> DeleteRouteAsync(
        string organizationId,
        string routeId,
        CancellationToken cancellationToken);
}

public interface ISpendLedger
{
    Task<decimal> RemainingBudgetUsdAsync(string organizationId, CancellationToken cancellationToken);
    Task<GatewaySpendView> ReserveAsync(
        string organizationId,
        string agentId,
        string decisionId,
        decimal estimatedCostUsd,
        CancellationToken cancellationToken);
    Task<GatewaySpendView> SettleAsync(
        string organizationId,
        string agentId,
        string decisionId,
        decimal actualCostUsd,
        CancellationToken cancellationToken);
    Task ReleaseAsync(
        string organizationId,
        string agentId,
        string decisionId,
        CancellationToken cancellationToken);
}
