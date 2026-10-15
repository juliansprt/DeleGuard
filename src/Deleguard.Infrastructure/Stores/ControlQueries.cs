using System.Globalization;
using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Stores;

public sealed class ControlQueries(DeleguardDbContext db) : IControlQueries
{
    private IQueryable<AuthorizationDecision> Decisions(string org, string app) => db.AuthorizationDecisions.AsNoTracking()
        .Where(d => d.OrganizationId == org && d.ApplicationId == app);
    private IQueryable<string?> PlaygroundIds(string org,string app)=>db.AuditEvents.Where(e=>e.OrganizationId==org&&e.ApplicationId==app&&e.EventType=="authorization.decided"
        &&EF.Functions.JsonContains(e.PayloadJson,"{\"source\":\"playground\"}")).Select(e=>e.DecisionId);

    public Task<bool> AgentHasEvidenceAsync(string organizationId, string applicationId, string agentId, CancellationToken ct)
    {
        var tests=PlaygroundIds(organizationId,applicationId);
        var ids = Decisions(organizationId, applicationId).Where(d => d.AgentId == agentId&&!tests.Contains(d.Id)).Select(d => d.Id);
        return db.ExecutionEvidence.AnyAsync(e => e.OrganizationId == organizationId && ids.Contains(e.DecisionId), ct);
    }
    public Task<bool> AgentHasAuthorizationAsync(string organizationId,string applicationId,string agentId,CancellationToken ct)
    {
        var tests=PlaygroundIds(organizationId,applicationId);
        return Decisions(organizationId,applicationId).AnyAsync(d=>d.AgentId==agentId&&!tests.Contains(d.Id),ct);
    }

    public async Task<ControlSummary> SummaryAsync(string organizationId, string applicationId, DateTimeOffset now, CancellationToken ct)
    {
        var start = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var end = start.AddDays(1);
        var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var decisions = Decisions(organizationId, applicationId);
        var ids = decisions.Select(d => d.Id);
        var today = decisions.Where(d => d.CreatedAt >= start && d.CreatedAt < end);
        // Original decisions are immutable in the audit event; approval resolution can change the decision row.
        var events = db.AuditEvents.AsNoTracking().Where(e => e.OrganizationId == organizationId &&
            e.ApplicationId == applicationId && e.EventType == "authorization.decided" && e.CreatedAt >= start && e.CreatedAt < end);
        var checks = await events.CountAsync(ct);
        var denials = await events.CountAsync(e => EF.Functions.JsonContains(e.PayloadJson, "{\"decision\":\"DENY\"}"), ct);
        var evidence = db.ExecutionEvidence.AsNoTracking().Where(e => e.OrganizationId == organizationId && ids.Contains(e.DecisionId));
        var pending = await db.ApprovalRequests.CountAsync(a => a.OrganizationId == organizationId && ids.Contains(a.DecisionId)
            && a.Status == ApprovalStatus.Pending && a.ExpiresAt > now, ct);
        var missing = await decisions.CountAsync(d => d.Decision == DecisionEffect.Allow &&
            !db.ExecutionEvidence.Any(e => e.OrganizationId == organizationId && e.DecisionId == d.Id), ct);
        var failed = await evidence.Where(e => e.CreatedAt >= start && e.CreatedAt < end && e.Status == ExecutionStatus.Failed)
            .Select(e => e.DecisionId).Distinct().CountAsync(ct);
        var spend = await db.GatewaySpendReservations.Where(s => s.OrganizationId == organizationId && ids.Contains(s.DecisionId)
            && s.CreatedAt >= month && s.CreatedAt < month.AddMonths(1) && s.Status == GatewaySpendStatuses.Settled)
            .SumAsync(s => s.SettledUsd ?? 0m, ct);
        var blocks = await today.CountAsync(d => d.Action.StartsWith("llm.") && d.Decision == DecisionEffect.Deny &&
            d.Reason != null && d.Reason.ToLower().Contains("budget"), ct);
        var playgroundIds=PlaygroundIds(organizationId,applicationId);
        var integration=decisions.Where(d=>!playgroundIds.Contains(d.Id));
        return new(checks, denials, pending, missing, failed, await integration.AnyAsync(ct),
            await evidence.AnyAsync(e=>integration.Select(d=>d.Id).Contains(e.DecisionId),ct), spend, blocks);
    }

    public async Task<decimal> AgentSpendAsync(string organizationId, string applicationId, string agentId, DateTimeOffset now, CancellationToken ct)
    {
        var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var ids = Decisions(organizationId, applicationId).Where(d => d.AgentId == agentId).Select(d => d.Id);
        return await db.GatewaySpendReservations.Where(s => s.OrganizationId == organizationId && ids.Contains(s.DecisionId)
            && s.CreatedAt >= month && s.CreatedAt < month.AddMonths(1) && s.Status == GatewaySpendStatuses.Settled)
            .SumAsync(s => s.SettledUsd ?? 0m, ct);
    }

    public async Task<IReadOnlyList<CoverageRow>> CoverageAsync(string organizationId, string applicationId, string? agentId, CancellationToken ct)
    {
        var actions = await db.ActionDefinitions.AsNoTracking().Where(a => a.OrganizationId == organizationId && a.ApplicationId == applicationId).OrderBy(a => a.Name).ToListAsync(ct);
        var agents = await db.Agents.AsNoTracking().Where(a => a.OrganizationId == organizationId && a.ApplicationId == applicationId && (agentId == null || a.Id == agentId)).ToListAsync(ct);
        var grants = await db.AgentActionGrants.AsNoTracking().Where(g => g.OrganizationId == organizationId && g.ApplicationId == applicationId).ToListAsync(ct);
        var policyGrants = await db.AgentPolicyGrants.AsNoTracking().Where(g => g.OrganizationId == organizationId && g.ApplicationId == applicationId).ToListAsync(ct);
        var published = await (from p in db.Policies.AsNoTracking() join v in db.PolicyVersions.AsNoTracking() on p.Id equals v.PolicyId
            where p.OrganizationId == organizationId && p.ApplicationId == applicationId && v.OrganizationId == organizationId && v.Published
            select new { p.Id, p.Slug, v.Version, v.Action, v.ResourceType }).ToListAsync(ct);
        var result = new List<CoverageRow>();
        foreach (var action in actions)
        {
            var assigned = agents.Where(a => grants.Any(g => g.AgentId == a.Id && g.ActionDefinitionId == action.Id)).ToList();
            var candidates = agentId is null ? assigned : agents;
            foreach (var agent in candidates)
                result.Add(new(agent.Id, agent.DisplayName, agent.Enabled, action.Id, action.Name, action.ResourceType,
                    grants.Any(g => g.AgentId == agent.Id && g.ActionDefinitionId == action.Id),
                    published.Where(p => p.Action == action.Name && p.ResourceType == action.ResourceType && policyGrants.Any(g => g.AgentId == agent.Id && g.PolicyId == p.Id))
                        .Select(p => new CoveragePolicy(p.Id, p.Slug, p.Version)).ToList()));
            if (agentId is null && assigned.Count == 0)
                result.Add(new(null, "Unassigned", false, action.Id, action.Name, action.ResourceType, false, []));
        }
        return result;
    }

    public async Task<ActivityPage> ActivityAsync(string organizationId, string applicationId, string? agentId, string? filter, int page, CancellationToken ct)
    {
        var query = Decisions(organizationId, applicationId);
        if (!string.IsNullOrWhiteSpace(agentId)) query = query.Where(d => d.AgentId == agentId);
        var day = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var playgroundIds=PlaygroundIds(organizationId,applicationId);
        query = filter switch
        {
            "allow"=>query.Where(d=>d.Decision==DecisionEffect.Allow),
            "deny"=>query.Where(d=>d.Decision==DecisionEffect.Deny),
            "review"=>query.Where(d=>d.Decision==DecisionEffect.RequireApproval),
            "ide"=>query.Where(d=>EF.Functions.JsonContains(d.ContextJson,"{\"source\":\"ide-hook\"}")),
            "playground"=>query.Where(d=>playgroundIds.Contains(d.Id)),
            "missing" => query.Where(d => d.Decision == DecisionEffect.Allow && !db.ExecutionEvidence.Any(e => e.OrganizationId == organizationId && e.DecisionId == d.Id)),
            "failed" => query.Where(d => db.ExecutionEvidence.Any(e => e.OrganizationId == organizationId && e.DecisionId == d.Id && e.Status == ExecutionStatus.Failed && e.CreatedAt >= day && e.CreatedAt < day.AddDays(1))),
            "gateway" => query.Where(d => d.Action.StartsWith("llm.")),
            "budget" => query.Where(d => d.Action.StartsWith("llm.") && d.CreatedAt >= day && d.CreatedAt < day.AddDays(1) && d.Decision == DecisionEffect.Deny && d.Reason != null && d.Reason.ToLower().Contains("budget")),
            _ => query
        };
        page = Math.Clamp(page, 1, 100000);
        var rows = await query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).Skip((page - 1) * 30).Take(31).ToListAsync(ct);
        var ids = rows.Select(d => d.Id).ToList();
        var tests=await playgroundIds.Where(id=>ids.Contains(id!)).ToListAsync(ct);
        var evidence = await db.ExecutionEvidence.AsNoTracking().Where(e => e.OrganizationId == organizationId && ids.Contains(e.DecisionId)).OrderByDescending(e => e.CreatedAt).ToListAsync(ct);
        return new(rows.Take(30).Select(d => Project(d, evidence.FirstOrDefault(e => e.DecisionId == d.Id),tests.Contains(d.Id))).ToList(), page, rows.Count > 30);
    }

    public async Task<DecisionCase?> CaseAsync(string organizationId, string applicationId, string decisionId, CancellationToken ct)
    {
        var d = await Decisions(organizationId, applicationId).FirstOrDefaultAsync(d => d.Id == decisionId, ct);
        if (d is null) return null;
        var evidence = await db.ExecutionEvidence.AsNoTracking().Where(e => e.OrganizationId == organizationId && e.DecisionId == d.Id).OrderBy(e => e.CreatedAt).ToListAsync(ct);
        var approvals = await db.ApprovalRequests.AsNoTracking().Where(a => a.OrganizationId == organizationId && a.DecisionId == d.Id).OrderBy(a => a.CreatedAt)
            .Select(a => new ApprovalRecord(a.Id, a.Status.ToString(), a.ExpiresAt, a.ResolvedBy, a.ResolvedAt)).ToListAsync(ct);
        var permits = await db.Permits.AsNoTracking().Where(p => p.OrganizationId == organizationId && p.DecisionId == d.Id).OrderBy(p => p.IssuedAt)
            .Select(p => new PermitRecord(p.Id, p.IssuedAt, p.ExpiresAt, p.RevokedAt)).ToListAsync(ct);
        var policyLink = d.PolicyId is null ? null : await db.Policies.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.ApplicationId == applicationId && (p.Id == d.PolicyId || p.Slug == d.PolicyId))
            .Select(p => p.Id).FirstOrDefaultAsync(ct);
        var original = await db.AuditEvents.AsNoTracking().Where(e => e.OrganizationId == organizationId && e.ApplicationId == applicationId
            && e.DecisionId == d.Id && e.EventType == "authorization.decided").OrderBy(e => e.CreatedAt).Select(e => e.PayloadJson).FirstOrDefaultAsync(ct);
        return new(Project(d, evidence.LastOrDefault(),Read(original,"source")=="playground"), Read(original, "decision"), applicationId, d.ActorId, d.PolicyId, policyLink, d.PolicyVersion, d.MatchedRule, d.ResourceSnapshotHash,
            approvals, permits, evidence.Select(e => new EvidenceRecord(e.Id, e.Status.ToString(), e.ProviderReference, e.ResultHash, e.CreatedAt)).ToList());
    }

    private static DecisionActivity Project(AuthorizationDecision d, ExecutionEvidence? evidence,bool consoleTest=false)
    {
        // Only an explicit metadata allowlist is projected. Never expose raw payloads, prompts or permit tokens.
        var meta = evidence?.MetadataJson;
        var cost = decimal.TryParse(Read(meta, "cost_usd"), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : (decimal?)null;
        return new(d.Id, d.AgentId, d.AgentExternalId, d.Action, d.ResourceId, DecisionNames.ToApi(d.Decision), d.Reason, d.CreatedAt,
            evidence is not null, Read(meta, "model_requested") ?? Read(d.ResourceAttributesJson, "model_requested"), Read(meta, "model_used"),
            Read(meta, "route_reason"), cost, evidence?.Status.ToString(), consoleTest?"playground":Read(d.ContextJson,"source")=="ide-hook"?"ide":"application");
    }

    private static string? Read(string? json, string key)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null; }
        catch (JsonException) { return null; }
    }
}
