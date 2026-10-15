using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Authorization;

namespace Deleguard.Application.Identity;

public sealed class GatewayAdminHandler
{
    private readonly IGatewayDirectory _gateway;
    private readonly IUnitOfWork _uow;

    public GatewayAdminHandler(IGatewayDirectory gateway, IUnitOfWork uow)
    {
        _gateway = gateway;
        _uow = uow;
    }

    public Task<GatewaySettingsView> GetSettingsAsync(string organizationId, CancellationToken cancellationToken) =>
        _gateway.GetSettingsAsync(organizationId, cancellationToken);

    public async Task<GatewaySettingsView> UpdateSettingsAsync(
        string organizationId,
        GatewaySettingsUpdate update,
        CancellationToken cancellationToken)
    {
        GatewayCatalogRules.RejectSemanticCache(update.SemanticCacheEnabled);
        var current = await _gateway.GetSettingsAsync(organizationId, cancellationToken);
        var patch = GatewaySettingsPatch.Overlay(current, update);
        GatewayCatalogRules.ValidateSettings(patch);
        var updated = await _gateway.UpdateSettingsAsync(
            organizationId,
            patch,
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public async Task<GatewaySettingsView> UpsertRouteAsync(
        string organizationId,
        GatewayRouteWrite route,
        CancellationToken cancellationToken)
    {
        var updated = await _gateway.UpsertRouteAsync(
            organizationId,
            GatewayCatalogRules.NormalizeRoute(route),
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public async Task<GatewaySettingsView> DeleteRouteAsync(
        string organizationId,
        string routeId,
        CancellationToken cancellationToken)
    {
        var updated = await _gateway.DeleteRouteAsync(organizationId, routeId, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public async Task<GatewaySettingsView> ApplyOpenAiChatPairAsync(
        string organizationId,
        CancellationToken cancellationToken)
    {
        foreach (var route in GatewayRecipes.OpenAiChatPair())
        {
            await _gateway.UpsertRouteAsync(
                organizationId,
                GatewayCatalogRules.NormalizeRoute(route),
                cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);
        return await _gateway.GetSettingsAsync(organizationId, cancellationToken);
    }
}
