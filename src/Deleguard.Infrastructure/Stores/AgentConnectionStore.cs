using System.Data;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Stores;

public sealed class AgentConnectionStore(DeleguardDbContext db, IClock clock) : IAgentConnectionStore
{
    public async Task ConnectAsync(string organizationId, string applicationId, string agentId, string actionId, string policyId, CancellationToken ct)
    {
        try { await ConnectCoreAsync(organizationId, applicationId, agentId, actionId, policyId, ct); }
        catch (Npgsql.PostgresException ex) when (ex.SqlState is "40001" or "40P01")
        { throw new ConflictException("Configuration changed during assignment. Refresh and try again."); }
    }

    private async Task ConnectCoreAsync(string organizationId, string applicationId, string agentId, string actionId, string policyId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var valid = await (from application in db.Applications
            join action in db.ActionDefinitions on application.Id equals action.ApplicationId
            join policy in db.Policies on application.Id equals policy.ApplicationId
            join version in db.PolicyVersions on policy.PublishedVersionId equals version.Id
            join agent in db.Agents on application.Id equals agent.ApplicationId
            where application.OrganizationId == organizationId && application.Id == applicationId && application.Kind == ApplicationKinds.Product
                && action.OrganizationId == organizationId && action.Id == actionId && !action.Name.StartsWith("admin.")
                && policy.OrganizationId == organizationId && policy.Id == policyId && version.OrganizationId == organizationId
                && version.Published && version.PolicyId == policy.Id && version.Action == action.Name && version.ResourceType == action.ResourceType
                && agent.OrganizationId == organizationId && agent.Id == agentId
            select agent.Id).AnyAsync(ct);
        if (!valid) throw new ConflictException("The agent, action or published policy changed. Refresh setup and choose a matching policy.");
        var now = clock.UtcNow;
        // Additive upserts never replace another agent's grants or rewrite resource type/risk.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO agent_action_grants ("OrganizationId", "ApplicationId", "AgentId", "ActionDefinitionId", "CreatedAt")
            VALUES ({organizationId}, {applicationId}, {agentId}, {actionId}, {now}) ON CONFLICT DO NOTHING
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO agent_policy_grants ("OrganizationId", "ApplicationId", "AgentId", "PolicyId", "CreatedAt")
            VALUES ({organizationId}, {applicationId}, {agentId}, {policyId}, {now}) ON CONFLICT DO NOTHING
            """, ct);
        await transaction.CommitAsync(ct);
    }
}
