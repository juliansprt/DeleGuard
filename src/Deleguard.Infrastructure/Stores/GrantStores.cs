using Deleguard.Application.Abstractions;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Policies;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Stores;

public sealed class ActionGrantStore : IActionGrantStore
{
    private readonly DeleguardDbContext _db;

    public ActionGrantStore(DeleguardDbContext db) => _db = db;

    public Task<bool> HasAsync(string organizationId, string agentId, string actionId, CancellationToken cancellationToken) =>
        _db.AgentActionGrants.AsNoTracking().AnyAsync(
            x => x.OrganizationId == organizationId && x.AgentId == agentId && x.ActionDefinitionId == actionId,
            cancellationToken);

    public async Task<IReadOnlyList<string>> ListAgentIdsForActionAsync(
        string organizationId,
        string actionId,
        CancellationToken cancellationToken) =>
        await _db.AgentActionGrants.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ActionDefinitionId == actionId)
            .Select(x => x.AgentId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ListAgentIdsByActionIdsAsync(
        string organizationId,
        IReadOnlyList<string> actionIds,
        CancellationToken cancellationToken)
    {
        if (actionIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        var rows = await _db.AgentActionGrants.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && actionIds.Contains(x.ActionDefinitionId))
            .Select(x => new { x.ActionDefinitionId, x.AgentId })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(x => x.ActionDefinitionId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.AgentId).ToList());
    }

    public async Task ReplaceAsync(
        string organizationId,
        string applicationId,
        string actionId,
        IReadOnlyList<string> agentIds,
        CancellationToken cancellationToken)
    {
        var existing = await _db.AgentActionGrants
            .Where(x => x.OrganizationId == organizationId && x.ActionDefinitionId == actionId)
            .ToListAsync(cancellationToken);
        _db.AgentActionGrants.RemoveRange(existing);
        var now = DateTimeOffset.UtcNow;
        foreach (var agentId in agentIds.Distinct(StringComparer.Ordinal))
        {
            await _db.AgentActionGrants.AddAsync(new AgentActionGrant
            {
                OrganizationId = organizationId,
                ApplicationId = applicationId,
                AgentId = agentId,
                ActionDefinitionId = actionId,
                CreatedAt = now
            }, cancellationToken);
        }
    }

    public async Task DeleteByActionIdAsync(string organizationId, string actionId, CancellationToken cancellationToken)
    {
        var existing = await _db.AgentActionGrants
            .Where(x => x.OrganizationId == organizationId && x.ActionDefinitionId == actionId)
            .ToListAsync(cancellationToken);
        _db.AgentActionGrants.RemoveRange(existing);
    }
}

public sealed class PolicyGrantStore : IPolicyGrantStore
{
    private readonly DeleguardDbContext _db;

    public PolicyGrantStore(DeleguardDbContext db) => _db = db;

    public Task<bool> HasAsync(string organizationId, string agentId, string policyId, CancellationToken cancellationToken) =>
        _db.AgentPolicyGrants.AsNoTracking().AnyAsync(
            x => x.OrganizationId == organizationId && x.AgentId == agentId && x.PolicyId == policyId,
            cancellationToken);

    public async Task<IReadOnlyList<string>> ListAgentIdsForPolicyAsync(
        string organizationId,
        string policyId,
        CancellationToken cancellationToken) =>
        await _db.AgentPolicyGrants.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.PolicyId == policyId)
            .Select(x => x.AgentId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ListAgentIdsByPolicyIdsAsync(
        string organizationId,
        IReadOnlyList<string> policyIds,
        CancellationToken cancellationToken)
    {
        if (policyIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        var rows = await _db.AgentPolicyGrants.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && policyIds.Contains(x.PolicyId))
            .Select(x => new { x.PolicyId, x.AgentId })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(x => x.PolicyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.AgentId).ToList());
    }

    public async Task ReplaceAsync(
        string organizationId,
        string applicationId,
        string policyId,
        IReadOnlyList<string> agentIds,
        CancellationToken cancellationToken)
    {
        var existing = await _db.AgentPolicyGrants
            .Where(x => x.OrganizationId == organizationId && x.PolicyId == policyId)
            .ToListAsync(cancellationToken);
        _db.AgentPolicyGrants.RemoveRange(existing);
        var now = DateTimeOffset.UtcNow;
        foreach (var agentId in agentIds.Distinct(StringComparer.Ordinal))
        {
            await _db.AgentPolicyGrants.AddAsync(new AgentPolicyGrant
            {
                OrganizationId = organizationId,
                ApplicationId = applicationId,
                AgentId = agentId,
                PolicyId = policyId,
                CreatedAt = now
            }, cancellationToken);
        }
    }

    public async Task DeleteByPolicyIdAsync(string organizationId, string policyId, CancellationToken cancellationToken)
    {
        var existing = await _db.AgentPolicyGrants
            .Where(x => x.OrganizationId == organizationId && x.PolicyId == policyId)
            .ToListAsync(cancellationToken);
        _db.AgentPolicyGrants.RemoveRange(existing);
    }
}
