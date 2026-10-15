using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Stores;

public sealed class GatewayDirectory : IGatewayDirectory
{
    private readonly DeleguardDbContext _db;
    private readonly IClock _clock;

    public GatewayDirectory(DeleguardDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<GatewaySettingsView> GetSettingsAsync(string organizationId, CancellationToken cancellationToken)
    {
        var settings = await EnsureSettingsAsync(organizationId, cancellationToken);
        var (remaining, used) = await SpendAsync(organizationId, settings.MonthlyBudgetUsd, cancellationToken);
        var routes = await ListRoutesAsync(organizationId, cancellationToken);
        return ToView(settings, remaining, used, routes);
    }

    public async Task<GatewaySettingsView> UpdateSettingsAsync(
        string organizationId,
        GatewaySettingsPatch patch,
        CancellationToken cancellationToken)
    {
        GatewayCatalogRules.ValidateSettings(patch);
        var settings = await EnsureSettingsAsync(organizationId, cancellationToken);
        settings.Enabled = patch.Enabled;
        settings.RoutingMode = patch.RoutingMode;
        settings.MonthlyBudgetUsd = decimal.Round(patch.MonthlyBudgetUsd, 6);
        settings.DefaultTokenBudgetClass = patch.DefaultTokenBudgetClass;
        settings.ShortMaxOutputTokens = patch.ShortMaxOutputTokens;
        settings.StandardMaxOutputTokens = patch.StandardMaxOutputTokens;
        settings.DeepMaxOutputTokens = patch.DeepMaxOutputTokens;
        settings.HeuristicCheapMaxTokens = patch.HeuristicCheapMaxTokens;
        settings.CascadeSkipToStrongTokens = patch.CascadeSkipToStrongTokens;
        settings.CascadeMaxAttempts = patch.CascadeMaxAttempts;
        settings.CascadeMaxUsd = decimal.Round(patch.CascadeMaxUsd, 6);
        settings.ExactCacheTtlSeconds = patch.ExactCacheTtlSeconds;
        settings.PrefixCacheEnabled = patch.PrefixCacheEnabled;
        settings.ShadowRouterEnabled = patch.ShadowRouterEnabled;
        settings.UpdatedAt = _clock.UtcNow;
        var (remaining, used) = await SpendAsync(organizationId, settings.MonthlyBudgetUsd, cancellationToken);
        var routes = await ListRoutesAsync(organizationId, cancellationToken);
        return ToView(settings, remaining, used, routes);
    }

    public async Task<GatewaySettingsView> UpsertRouteAsync(
        string organizationId,
        GatewayRouteWrite route,
        CancellationToken cancellationToken)
    {
        var write = GatewayCatalogRules.NormalizeRoute(route);
        await EnsureSettingsAsync(organizationId, cancellationToken);
        GatewayModelRoute? row = null;
        if (!string.IsNullOrWhiteSpace(write.Id))
        {
            row = await _db.GatewayModelRoutes.FirstOrDefaultAsync(
                      x => x.OrganizationId == organizationId && x.Id == write.Id,
                      cancellationToken)
                  ?? throw new NotFoundException("Route not found.");
        }
        else
        {
            row = await _db.GatewayModelRoutes.FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Alias == write.Alias,
                cancellationToken);
        }

        if (row is null)
        {
            row = new GatewayModelRoute
            {
                Id = IdGenerator.New("rte"),
                OrganizationId = organizationId,
                Alias = write.Alias,
                Provider = write.Provider,
                Model = write.Model,
                Tier = write.Tier,
                Region = write.Region,
                FallbackGroup = write.FallbackGroup
            };
            await _db.GatewayModelRoutes.AddAsync(row, cancellationToken);
        }

        var clash = await _db.GatewayModelRoutes.AnyAsync(
            x => x.OrganizationId == organizationId && x.Alias == write.Alias && x.Id != row.Id,
            cancellationToken);
        if (clash)
        {
            throw new ConflictException("A route with that alias already exists.");
        }

        row.Alias = write.Alias;
        row.Provider = write.Provider;
        row.Model = write.Model;
        row.Tier = write.Tier;
        row.Region = write.Region;
        row.FallbackGroup = write.FallbackGroup;
        row.InputPricePerMillion = write.InputPricePerMillion;
        row.OutputPricePerMillion = write.OutputPricePerMillion;
        row.SortOrder = write.SortOrder;
        row.Enabled = write.Enabled;
        return await GetSettingsAsync(organizationId, cancellationToken);
    }

    public async Task<GatewaySettingsView> DeleteRouteAsync(
        string organizationId,
        string routeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(routeId))
        {
            throw new RequestValidationException("route_id is required.");
        }

        var row = await _db.GatewayModelRoutes.FirstOrDefaultAsync(
                      x => x.OrganizationId == organizationId && x.Id == routeId,
                      cancellationToken)
                  ?? throw new NotFoundException("Route not found.");
        _db.GatewayModelRoutes.Remove(row);
        return await GetSettingsAsync(organizationId, cancellationToken);
    }

    private async Task<OrganizationGatewaySettings> EnsureSettingsAsync(string organizationId, CancellationToken cancellationToken)
    {
        var settings = await _db.GatewaySettings.FirstOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = OrganizationGatewaySettings.Create(organizationId, _clock.UtcNow);
        await _db.GatewaySettings.AddAsync(settings, cancellationToken);
        await EnsureDefaultRoutesAsync(organizationId, cancellationToken);
        return settings;
    }

    private async Task EnsureDefaultRoutesAsync(string organizationId, CancellationToken cancellationToken)
    {
        if (await _db.GatewayModelRoutes.AnyAsync(x => x.OrganizationId == organizationId, cancellationToken) ||
            _db.GatewayModelRoutes.Local.Any(x => x.OrganizationId == organizationId))
        {
            return;
        }

        foreach (var route in DefaultGatewayCatalog.Routes(organizationId))
        {
            await _db.GatewayModelRoutes.AddAsync(route, cancellationToken);
        }
    }

    private Task<List<GatewayModelRoute>> ListRoutesAsync(string organizationId, CancellationToken cancellationToken) =>
        _db.GatewayModelRoutes.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.FallbackGroup)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Alias)
            .ToListAsync(cancellationToken);

    private async Task<(decimal Remaining, decimal Used)> SpendAsync(string organizationId, decimal monthlyBudget, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(_clock.UtcNow.Year, _clock.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var rows = await _db.GatewaySpendReservations.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && (x.CreatedAt >= start || x.SettledAt >= start || x.Status == GatewaySpendStatuses.Reserved || x.Status == "estimated"))
            .Select(x => new { x.Status, x.ReservedUsd, x.SettledUsd })
            .ToListAsync(cancellationToken);
        var used = rows.Sum(x => x.Status switch
        {
            GatewaySpendStatuses.Settled => x.SettledUsd ?? 0,
            GatewaySpendStatuses.Reserved or "estimated" => x.ReservedUsd,
            _ => 0
        });
        var remaining = monthlyBudget - used;
        if (remaining < 0)
        {
            remaining = 0;
        }

        return (decimal.Round(remaining, 6), decimal.Round(used, 6));
    }

    private static GatewaySettingsView ToView(
        OrganizationGatewaySettings settings,
        decimal remaining,
        decimal used,
        IReadOnlyList<GatewayModelRoute> routes) =>
        new(
            settings.Enabled,
            settings.RoutingMode,
            settings.MonthlyBudgetUsd,
            remaining,
            used,
            settings.DefaultTokenBudgetClass,
            settings.ShortMaxOutputTokens,
            settings.StandardMaxOutputTokens,
            settings.DeepMaxOutputTokens,
            settings.HeuristicCheapMaxTokens,
            settings.CascadeSkipToStrongTokens,
            settings.CascadeMaxAttempts,
            settings.CascadeMaxUsd,
            settings.ExactCacheTtlSeconds,
            settings.PrefixCacheEnabled,
            settings.ShadowRouterEnabled,
            routes.Select(route => new GatewayRouteView(
                route.Id,
                route.Alias,
                route.Provider,
                route.Model,
                route.Tier,
                route.Region,
                route.FallbackGroup,
                route.InputPricePerMillion,
                route.OutputPricePerMillion,
                route.SortOrder,
                route.Enabled)).ToList());
}

public sealed class SpendLedger : ISpendLedger
{
    private readonly DeleguardDbContext _db;
    private readonly IClock _clock;
    private readonly IGatewayDirectory _gateway;
    private readonly IStateCoordinator _state;

    public SpendLedger(DeleguardDbContext db, IClock clock, IGatewayDirectory gateway, IStateCoordinator state)
    {
        _db = db;
        _clock = clock;
        _gateway = gateway; _state = state;
    }

    public async Task<decimal> RemainingBudgetUsdAsync(string organizationId, CancellationToken cancellationToken)
    {
        var settings = await _gateway.GetSettingsAsync(organizationId, cancellationToken);
        return settings.RemainingBudgetUsd;
    }

    public Task<GatewaySpendView> ReserveAsync(string organizationId, string agentId, string decisionId, decimal estimatedCostUsd, CancellationToken ct) =>
        _state.RunAsync(organizationId, () => ReserveCoreAsync(organizationId, agentId, decisionId, estimatedCostUsd, ct), ct);

    private async Task<GatewaySpendView> ReserveCoreAsync(
        string organizationId,
        string agentId,
        string decisionId,
        decimal estimatedCostUsd,
        CancellationToken cancellationToken)
    {
        if (estimatedCostUsd < 0) throw new RequestValidationException("Estimated cost cannot be negative.");
        var existing = await _db.GatewaySpendReservations.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DecisionId == decisionId,
            cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.AgentId, agentId, StringComparison.Ordinal))
            {
                throw new NotFoundException("Decision not found.");
            }

            if (existing.Status != GatewaySpendStatuses.Reserved) throw new ConflictException("Reservation already closed. Inspect the original operation; do not execute again.");
            if (existing.ReservedUsd != decimal.Round(estimatedCostUsd, 6)) throw new ConflictException("Reservation identity reused with another amount.");
            return ToView(existing);
        }

        var remaining = await RemainingBudgetUsdAsync(organizationId, cancellationToken);
        if (remaining <= 0 || estimatedCostUsd > remaining)
        {
            throw new ConflictException("Monthly model budget exhausted.");
        }

        var row = new GatewaySpendReservation
        {
            Id = IdGenerator.New("spn"),
            OrganizationId = organizationId,
            DecisionId = decisionId,
            AgentId = agentId,
            ReservedUsd = decimal.Round(estimatedCostUsd, 6),
            Status = GatewaySpendStatuses.Reserved,
            CreatedAt = _clock.UtcNow
        };
        await _db.GatewaySpendReservations.AddAsync(row, cancellationToken);
        return ToView(row);
    }

    public Task<GatewaySpendView> SettleAsync(string organizationId, string agentId, string decisionId, decimal actualCostUsd, CancellationToken ct) =>
        _state.RunAsync(organizationId, () => SettleCoreAsync(organizationId, agentId, decisionId, actualCostUsd, ct), ct);

    private async Task<GatewaySpendView> SettleCoreAsync(
        string organizationId,
        string agentId,
        string decisionId,
        decimal actualCostUsd,
        CancellationToken cancellationToken)
    {
        var row = await RequireAsync(organizationId, agentId, decisionId, cancellationToken);
        if (actualCostUsd < 0) throw new RequestValidationException("Actual cost cannot be negative.");
        if (row.Status == GatewaySpendStatuses.Released || row.Status == GatewaySpendStatuses.Settled && row.SettledUsd != decimal.Round(actualCostUsd, 6))
            throw new ConflictException("Conflicting settlement.");
        if (row.Status == GatewaySpendStatuses.Settled) return ToView(row);
        row.SettledUsd = decimal.Round(actualCostUsd, 6);
        row.Status = GatewaySpendStatuses.Settled;
        row.SettledAt = _clock.UtcNow;
        return ToView(row);
    }

    public Task ReleaseAsync(string organizationId, string agentId, string decisionId, CancellationToken ct) =>
        _state.RunAsync(organizationId, async () => { await ReleaseCoreAsync(organizationId, agentId, decisionId, ct); return true; }, ct);

    private async Task ReleaseCoreAsync(
        string organizationId,
        string agentId,
        string decisionId,
        CancellationToken cancellationToken)
    {
        var row = await _db.GatewaySpendReservations.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DecisionId == decisionId && x.AgentId == agentId,
            cancellationToken);
        if (row is null || row.Status != GatewaySpendStatuses.Reserved)
        {
            return;
        }

        if (await _db.OperationExecutions.AnyAsync(e => e.OrganizationId == organizationId && e.AgentId == agentId && e.DecisionId == decisionId, cancellationToken))
        {
            row.Status = "estimated"; // The provider may have received the request. Never reinterpret it as zero usage.
            return;
        }
        row.Status = GatewaySpendStatuses.Released;
        row.SettledAt = _clock.UtcNow;
        row.SettledUsd = 0;
    }

    private async Task<GatewaySpendReservation> RequireAsync(
        string organizationId,
        string agentId,
        string decisionId,
        CancellationToken cancellationToken)
    {
        var row = await _db.GatewaySpendReservations.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DecisionId == decisionId,
            cancellationToken) ?? throw new NotFoundException("Spend reservation not found.");
        if (!string.Equals(row.AgentId, agentId, StringComparison.Ordinal))
        {
            throw new NotFoundException("Spend reservation not found.");
        }

        return row;
    }

    private static GatewaySpendView ToView(GatewaySpendReservation row) =>
        new(row.Id, row.DecisionId, row.ReservedUsd, row.SettledUsd, row.Status);
}
