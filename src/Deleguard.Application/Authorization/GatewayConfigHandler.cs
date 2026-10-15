using Deleguard.Application.Abstractions;
using Deleguard.Domain.Authorization;

namespace Deleguard.Application.Authorization;

public sealed record GatewayConfigRouteDto(
    string Alias,
    string Provider,
    string Model,
    string Tier,
    string Region,
    string FallbackGroup,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    int SortOrder);

public sealed record GatewayAuthorizedToolDto(
    string Tool,
    string Action,
    string ResourceType,
    string Risk,
    string SideEffect);

public sealed record GatewayTokenBudgetDto(string Name, int MaxOutputTokens);

public sealed record GatewayConfigDto(
    bool Enabled,
    string AgentId,
    string Environment,
    string RoutingMode,
    decimal MonthlyBudgetUsd,
    decimal RemainingBudgetUsd,
    string DefaultTokenBudgetClass,
    IReadOnlyList<GatewayTokenBudgetDto> TokenBudgetClasses,
    int HeuristicCheapMaxTokens,
    int CascadeSkipToStrongTokens,
    int CascadeMaxAttempts,
    decimal CascadeMaxUsd,
    int ExactCacheTtlSeconds,
    bool PrefixCacheEnabled,
    bool ShadowRouterEnabled,
    bool SemanticCacheEnabled,
    IReadOnlyList<GatewayConfigRouteDto> Routes,
    IReadOnlyList<GatewayAuthorizedToolDto> AuthorizedTools);

public sealed class GatewayConfigHandler
{
    private readonly IGatewayDirectory _gateway;
    private readonly IGuardMappingStore _mappings;
    private readonly ISpendLedger _spend;

    public GatewayConfigHandler(IGatewayDirectory gateway, IGuardMappingStore mappings, ISpendLedger spend)
    {
        _gateway = gateway;
        _mappings = mappings;
        _spend = spend;
    }

    public async Task<GatewayConfigDto> HandleAsync(AgentPrincipal principal, CancellationToken cancellationToken)
    {
        var settings = await _gateway.GetSettingsAsync(principal.OrganizationId, cancellationToken);
        var remaining = await _spend.RemainingBudgetUsdAsync(principal.OrganizationId, cancellationToken);
        var mappings = await _mappings.ListAsync(principal.OrganizationId, cancellationToken);
        var tools = mappings
            .Where(row => !ActionAdminHandler.IsAdministration(row.Action))
            .Select(row => new GatewayAuthorizedToolDto(
                GuardToolNames.Compose(row.McpServer, row.McpTool),
                row.Action,
                row.ResourceType,
                row.Risk.ToString().ToUpperInvariant(),
                GatewaySideEffects.FromRisk(row.Risk)))
            .ToList();

        return new GatewayConfigDto(
            settings.Enabled,
            principal.AgentExternalId,
            principal.EnvironmentName,
            settings.RoutingMode,
            settings.MonthlyBudgetUsd,
            remaining,
            settings.DefaultTokenBudgetClass,
            [
                new GatewayTokenBudgetDto(GatewayTokenBudgets.Short, settings.ShortMaxOutputTokens),
                new GatewayTokenBudgetDto(GatewayTokenBudgets.Standard, settings.StandardMaxOutputTokens),
                new GatewayTokenBudgetDto(GatewayTokenBudgets.Deep, settings.DeepMaxOutputTokens)
            ],
            settings.HeuristicCheapMaxTokens,
            settings.CascadeSkipToStrongTokens,
            settings.CascadeMaxAttempts,
            settings.CascadeMaxUsd,
            settings.ExactCacheTtlSeconds,
            settings.PrefixCacheEnabled,
            settings.ShadowRouterEnabled,
            settings.SemanticCacheEnabled,
            settings.Routes
                .Where(route => route.Enabled)
                .Select(route => new GatewayConfigRouteDto(
                    route.Alias,
                    route.Provider,
                    route.Model,
                    route.Tier,
                    route.Region,
                    route.FallbackGroup,
                    route.InputPricePerMillion,
                    route.OutputPricePerMillion,
                    route.SortOrder))
                .ToList(),
            tools);
    }
}
