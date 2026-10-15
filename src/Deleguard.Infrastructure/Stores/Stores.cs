using System.Text.Json;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Audit;
using Deleguard.Domain.Approvals;
using Deleguard.Domain.Audit;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;
using Deleguard.Domain.Playground;
using Deleguard.Domain.Policies;
using Deleguard.Infrastructure.Persistence;
using Deleguard.Policy;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Stores;

public sealed class ActionRegistry : IActionRegistry
{
    private readonly DeleguardDbContext _db;

    public ActionRegistry(DeleguardDbContext db) => _db = db;

    public Task<ActionDefinition?> GetByNameAsync(
        string organizationId,
        string applicationId,
        string name,
        CancellationToken cancellationToken) =>
        _db.ActionDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Name == name,
                cancellationToken);

    public Task<ActionDefinition?> GetAsync(string organizationId, string actionId, CancellationToken cancellationToken) =>
        _db.ActionDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == actionId, cancellationToken);

    public async Task<IReadOnlyList<ActionDefinition>> ListAsync(
        string organizationId,
        string? applicationId,
        CancellationToken cancellationToken)
    {
        var query = _db.ActionDefinitions.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            query = query.Where(x => x.ApplicationId == applicationId);
        }

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ActionDefinition action, CancellationToken cancellationToken) =>
        await _db.ActionDefinitions.AddAsync(action, cancellationToken);

    public async Task UpdateAsync(
        string organizationId,
        string actionId,
        string resourceType,
        RiskLevel risk,
        CancellationToken cancellationToken)
    {
        var tracked = await _db.ActionDefinitions.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.Id == actionId,
            cancellationToken) ?? throw new NotFoundException("Action not found.");
        tracked.ResourceType = resourceType;
        tracked.Risk = risk;
    }

    public async Task DeleteAsync(string organizationId, string actionId, CancellationToken cancellationToken)
    {
        var tracked = await _db.ActionDefinitions.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.Id == actionId,
            cancellationToken) ?? throw new NotFoundException("Action not found.");
        _db.ActionDefinitions.Remove(tracked);
    }
}

public sealed class PolicyRepository : IPolicyRepository
{
    private readonly DeleguardDbContext _db;

    public PolicyRepository(DeleguardDbContext db) => _db = db;

    public async Task<T> ExecuteLockedAsync<T>(string organizationId, string policyId, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        // Every policy writer takes this same parent-row lock before reading versions, grants or
        // fixtures. Join an outer transaction when present; never attempt a nested transaction.
        await using var transaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var locked = await _db.Policies.FromSqlInterpolated($"""
                SELECT * FROM policies WHERE "OrganizationId" = {organizationId} AND "Id" = {policyId} FOR UPDATE
                """).AsNoTracking().ToListAsync(cancellationToken);
            if (locked.Count != 1) throw new NotFoundException("Policy not found.");
            // The wizard may have read this policy earlier in the request. Tracking must not return
            // that stale instance after another writer committed while we were acquiring the lock.
            foreach (var entry in _db.ChangeTracker.Entries().Where(e => BelongsToPolicy(e.Entity, organizationId, policyId)).ToArray())
            {
                if (entry.State is not (EntityState.Unchanged or EntityState.Detached))
                    throw new InvalidOperationException("Save pending policy changes before acquiring its mutation lock.");
                entry.State = EntityState.Detached;
            }
            var result = await action();
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                // SaveChanges can mark rolled-back entities Unchanged, while failures before it
                // leave Modified/Added/Deleted entries. Detach both: a later audit/evidence save
                // must never flush the failed policy mutation after the lock has been released.
                foreach (var entry in _db.ChangeTracker.Entries().Where(e => BelongsToPolicy(e.Entity, organizationId, policyId)).ToArray())
                    entry.State = EntityState.Detached;
            }
            throw;
        }
    }

    private static bool BelongsToPolicy(object entity, string organizationId, string policyId) => entity switch
    {
        PolicyDefinition p => p.OrganizationId == organizationId && p.Id == policyId,
        PolicyVersion v => v.OrganizationId == organizationId && v.PolicyId == policyId,
        AgentPolicyGrant g => g.OrganizationId == organizationId && g.PolicyId == policyId,
        PolicySimulationCase c => c.OrganizationId == organizationId && c.PolicyId == policyId,
        _ => false
    };

    public async Task<IReadOnlyList<CompiledPolicy>> GetPublishedAsync(
        string organizationId,
        string applicationId,
        string action,
        string resourceType,
        string agentId,
        CancellationToken cancellationToken)
    {
        var versions = await (
                from version in _db.PolicyVersions.AsNoTracking()
                join policy in _db.Policies.AsNoTracking() on version.PolicyId equals policy.Id
                join grant in _db.AgentPolicyGrants.AsNoTracking() on policy.Id equals grant.PolicyId
                where version.OrganizationId == organizationId
                      && version.Published
                      && version.Action == action
                      && version.ResourceType == resourceType
                      && policy.ApplicationId == applicationId
                      && grant.OrganizationId == organizationId
                      && grant.AgentId == agentId
                select version)
            .ToListAsync(cancellationToken);
        return versions.Select(v => PolicyYamlParser.Parse(v.PolicyId, v.Yaml)).ToList();
    }

    public async Task<IReadOnlyList<PolicySummary>> ListAsync(
        string organizationId,
        string? applicationId,
        CancellationToken cancellationToken)
    {
        var query = _db.Policies.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            query = query.Where(x => x.ApplicationId == applicationId);
        }

        var policies = await query.OrderBy(x => x.Slug).ToListAsync(cancellationToken);
        var ids = policies.Select(x => x.Id).ToList();
        var versions = await _db.PolicyVersions.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.PolicyId))
            .ToListAsync(cancellationToken);
        return policies.Select(policy =>
        {
            var owned = versions.Where(v => v.PolicyId == policy.Id).ToList();
            var published = owned.FirstOrDefault(v => v.Id == policy.PublishedVersionId) ?? owned.FirstOrDefault(v => v.Published);
            var latest = owned.OrderByDescending(v => v.Version).FirstOrDefault();
            var current = published ?? latest;
            return new PolicySummary(
                policy.Id,
                policy.Slug,
                policy.ApplicationId,
                policy.PublishedVersionId,
                owned.Count,
                published?.Version,
                current?.Action,
                current?.ResourceType);
        }).ToList();
    }

    public Task<PolicyDefinition?> GetAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
        _db.Policies.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == policyId, cancellationToken);

    public async Task<IReadOnlyList<PolicyVersion>> ListVersionsAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
        await _db.PolicyVersions.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.PolicyId == policyId)
            .OrderByDescending(x => x.Version)
            .ToListAsync(cancellationToken);

    public Task<PolicyVersion?> GetVersionAsync(string organizationId, string policyId, string versionId, CancellationToken cancellationToken) =>
        _db.PolicyVersions.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.PolicyId == policyId && x.Id == versionId,
            cancellationToken);

    public Task<PolicyVersion?> GetLatestVersionAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
        _db.PolicyVersions.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.PolicyId == policyId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddDefinitionAsync(PolicyDefinition policy, CancellationToken cancellationToken) =>
        await _db.Policies.AddAsync(policy, cancellationToken);

    public async Task AddVersionAsync(PolicyVersion version, CancellationToken cancellationToken) =>
        await _db.PolicyVersions.AddAsync(version, cancellationToken);

    public async Task<IReadOnlyList<PolicyVersion>> GetVersionsForUpdateAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
        await _db.PolicyVersions
            .Where(x => x.OrganizationId == organizationId && x.PolicyId == policyId)
            .ToListAsync(cancellationToken);

    public async Task DeleteAsync(string organizationId, string policyId, CancellationToken cancellationToken)
    {
        var versions = await _db.PolicyVersions
            .Where(x => x.OrganizationId == organizationId && x.PolicyId == policyId)
            .ToListAsync(cancellationToken);
        var policy = await _db.Policies.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.Id == policyId,
            cancellationToken) ?? throw new NotFoundException("Policy not found.");
        _db.PolicyVersions.RemoveRange(versions);
        _db.Policies.Remove(policy);
    }
}

public sealed class SandboxInvoiceStore : ISandboxInvoiceStore
{
    private readonly DeleguardDbContext _db;

    public SandboxInvoiceStore(DeleguardDbContext db) => _db = db;

    public async Task<IReadOnlyList<SandboxInvoice>> ListAsync(string organizationId, CancellationToken cancellationToken) =>
        await _db.SandboxInvoices.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.DisplayId)
            .ToListAsync(cancellationToken);

    public Task<SandboxInvoice?> GetAsync(string organizationId, string invoiceId, CancellationToken cancellationToken) =>
        _db.SandboxInvoices.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == invoiceId, cancellationToken);

    public Task<SandboxInvoice?> GetByDisplayIdAsync(string organizationId, string displayId, CancellationToken cancellationToken) =>
        _db.SandboxInvoices.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.DisplayId == displayId, cancellationToken);

    public async Task AddAsync(SandboxInvoice invoice, CancellationToken cancellationToken) =>
        await _db.SandboxInvoices.AddAsync(invoice, cancellationToken);
}

public sealed class DecisionStore : IDecisionStore
{
    private readonly DeleguardDbContext _db;

    public DecisionStore(DeleguardDbContext db) => _db = db;

    public Task<AuthorizationDecision?> GetAsync(string organizationId, string decisionId, CancellationToken cancellationToken) =>
        _db.AuthorizationDecisions.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == decisionId, cancellationToken);

    public async Task AddAsync(AuthorizationDecision decision, CancellationToken cancellationToken) =>
        await _db.AuthorizationDecisions.AddAsync(decision, cancellationToken);
}

public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly DeleguardDbContext _db;

    public IdempotencyStore(DeleguardDbContext db) => _db = db;

    public Task<IdempotencyRecord?> GetAsync(
        string organizationId,
        string applicationId,
        string key,
        CancellationToken cancellationToken) =>
        _db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Key == key,
                cancellationToken);

    public async Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken) =>
        await _db.IdempotencyRecords.AddAsync(record, cancellationToken);
}

public sealed class ApprovalStore : IApprovalStore
{
    private readonly DeleguardDbContext _db;

    public ApprovalStore(DeleguardDbContext db) => _db = db;

    public async Task AddAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
        await _db.ApprovalRequests.AddAsync(request, cancellationToken);

    public Task<ApprovalRequest?> GetAsync(string organizationId, string approvalId, CancellationToken cancellationToken) =>
        _db.ApprovalRequests.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == approvalId, cancellationToken);

    public async Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(
        string organizationId,
        string? applicationId,
        CancellationToken cancellationToken)
    {
        var query = _db.ApprovalRequests.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Status == ApprovalStatus.Pending);
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            query =
                from approval in query
                join decision in _db.AuthorizationDecisions.AsNoTracking() on approval.DecisionId equals decision.Id
                where decision.ApplicationId == applicationId
                select approval;
        }

        return await query.OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task AddEventAsync(ApprovalEvent approvalEvent, CancellationToken cancellationToken) =>
        await _db.ApprovalEvents.AddAsync(approvalEvent, cancellationToken);

    public async Task<IReadOnlyList<ApprovalRequest>> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await _db.ApprovalRequests
            .Where(x => x.Status == ApprovalStatus.Pending && x.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
        foreach (var request in due)
        {
            request.Status = ApprovalStatus.Expired;
            request.ResolvedAt = now;
        }

        return due;
    }
}

public sealed class PermitStore : IPermitStore
{
    private readonly DeleguardDbContext _db;

    public PermitStore(DeleguardDbContext db) => _db = db;

    public async Task AddAsync(Permit permit, CancellationToken cancellationToken) =>
        await _db.Permits.AddAsync(permit, cancellationToken);

    public Task<Permit?> GetAsync(string organizationId, string permitId, CancellationToken cancellationToken) =>
        _db.Permits.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == permitId, cancellationToken);

    public Task<Permit?> GetByDecisionAsync(string organizationId, string decisionId, CancellationToken cancellationToken) =>
        _db.Permits.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.DecisionId == decisionId)
            .OrderByDescending(x => x.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);
}

public sealed class EvidenceStore : IEvidenceStore
{
    private readonly DeleguardDbContext _db;

    public EvidenceStore(DeleguardDbContext db) => _db = db;

    public async Task AddAsync(ExecutionEvidence evidence, CancellationToken cancellationToken) =>
        await _db.ExecutionEvidence.AddAsync(evidence, cancellationToken);
}

public sealed class AuditLedger : IAuditLedger
{
    private readonly DeleguardDbContext _db;

    public AuditLedger(DeleguardDbContext db) => _db = db;

    public async Task AppendAsync(string organizationId, string eventType, string? decisionId, JsonElement payload, CancellationToken cancellationToken)
    {
        await _db.AuditEvents.AddAsync(new AuditEvent
        {
            Id = IdGenerator.New("aud"), OrganizationId = organizationId,
            ApplicationId = ReadApplicationId(payload), EventType = eventType, DecisionId = decisionId,
            PayloadJson = payload.GetRawText(), PreviousEventHash = string.Empty, EventHash = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        // The database context links all staged events under a transaction lock at SaveChanges.
    }

    public async Task<IReadOnlyList<AuditEvent>> ListAsync(
        string organizationId,
        int take,
        CancellationToken cancellationToken,
        string? eventType = null,
        string? q = null,
        string? applicationId = null)
    {
        var query = _db.AuditEvents.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var prefix = eventType.Trim();
            var dotted = prefix + ".";
            query = query.Where(x => x.EventType == prefix || x.EventType.StartsWith(dotted));
        }

        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            query = query.Where(x => x.ApplicationId == applicationId);
        }

        query = query.OrderByDescending(x => x.CreatedAt);
        if (string.IsNullOrWhiteSpace(q))
        {
            return await query.Take(take).ToListAsync(cancellationToken);
        }

        var scanned = await query.Take(Math.Max(take * 10, 500)).ToListAsync(cancellationToken);
        return scanned.Where(item => AuditListFilter.Matches(item, null, q)).Take(take).ToList();
    }

    private static string? ReadApplicationId(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("application_id", out var node)
            && node.ValueKind == JsonValueKind.String)
        {
            var value = node.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }
}

public sealed class Outbox : IOutbox
{
    private readonly DeleguardDbContext _db;

    public Outbox(DeleguardDbContext db) => _db = db;

    public async Task EnqueueAsync(string organizationId, string eventType, JsonElement payload, CancellationToken cancellationToken) =>
        await _db.OutboxEvents.AddAsync(new OutboxEvent
        {
            Id = IdGenerator.New("obx"),
            OrganizationId = organizationId,
            EventType = eventType,
            PayloadJson = payload.GetRawText(),
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

    public async Task<IReadOnlyList<OutboxEvent>> DequeueUnprocessedAsync(int take, CancellationToken cancellationToken) =>
        await _db.OutboxEvents
            .FromSql($"SELECT * FROM outbox_events WHERE \"ProcessedAt\" IS NULL ORDER BY \"CreatedAt\" LIMIT {take} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);

    public async Task MarkProcessedAsync(string id, DateTimeOffset processedAt, CancellationToken cancellationToken)
    {
        var item = await _db.OutboxEvents.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                   ?? _db.OutboxEvents.Local.FirstOrDefault(x => x.Id == id);
        if (item is not null)
        {
            item.ProcessedAt = processedAt;
        }
    }
}

public sealed class GuardMappingStore : IGuardMappingStore
{
    private readonly DeleguardDbContext _db;

    public GuardMappingStore(DeleguardDbContext db) => _db = db;

    public async Task<IReadOnlyList<GuardMappingRecord>> ListAsync(string organizationId, CancellationToken cancellationToken)
    {
        var rows = await (
            from binding in _db.ActionMcpBindings.AsNoTracking()
            join action in _db.ActionDefinitions.AsNoTracking() on binding.ActionDefinitionId equals action.Id
            where binding.OrganizationId == organizationId && action.OrganizationId == organizationId
            orderby binding.McpServer, binding.McpTool
            select new GuardMappingRecord(
                binding.Id,
                action.Id,
                action.Name,
                action.ResourceType,
                action.Risk,
                binding.McpServer,
                binding.McpTool,
                binding.ResourceIdPath,
                binding.AttributePathsJson)
        ).ToListAsync(cancellationToken);
        return rows;
    }

    public Task<ActionMcpBinding?> GetAsync(string organizationId, string bindingId, CancellationToken cancellationToken) =>
        _db.ActionMcpBindings.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.Id == bindingId,
            cancellationToken);

    public Task<ActionMcpBinding?> FindByToolAsync(
        string organizationId,
        string mcpServer,
        string mcpTool,
        CancellationToken cancellationToken) =>
        _db.ActionMcpBindings.AsNoTracking().FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.McpServer == mcpServer && x.McpTool == mcpTool,
            cancellationToken);

    public async Task AddAsync(ActionMcpBinding binding, CancellationToken cancellationToken) =>
        await _db.ActionMcpBindings.AddAsync(binding, cancellationToken);

    public Task DeleteAsync(ActionMcpBinding binding, CancellationToken cancellationToken)
    {
        _db.ActionMcpBindings.Remove(binding);
        return Task.CompletedTask;
    }

    public async Task DeleteByActionIdAsync(string organizationId, string actionId, CancellationToken cancellationToken)
    {
        var rows = await _db.ActionMcpBindings
            .Where(x => x.OrganizationId == organizationId && x.ActionDefinitionId == actionId)
            .ToListAsync(cancellationToken);
        _db.ActionMcpBindings.RemoveRange(rows);
    }
}
