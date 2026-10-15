using System.Text.Json;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Stores;

public sealed class ExecutionControl(DeleguardDbContext db, IStateCoordinator state,
    AuthorizationEvaluator evaluator, IAuditLedger audit, IClock clock) : IExecutionControl
{
    public Task<ConsumePermitResult> ConsumeAsync(AgentPrincipal a, string permitId, ConsumePermitRequest request, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) throw new ConflictException("Execution consumption must own its commit boundary.");
        return state.RunAsync(a.OrganizationId, async () =>
        {
            await state.EnsureActiveAsync(a, ct);
            var d = await db.AuthorizationDecisions.SingleOrDefaultAsync(d => d.OrganizationId == a.OrganizationId &&
                d.ApplicationId == a.ApplicationId && d.EnvironmentId == a.EnvironmentId && d.AgentId == a.AgentId && d.PermitId == permitId, ct)
                ?? throw new NotFoundException("Permit not found.");
            if (d.OperationId is null || d.OperationHash is null)
                throw new ConflictException("Legacy decision is not execution-bound. Request a new authorization.");
            if (d.OperationId != request.OperationId || d.OperationHash != request.OperationHash ||
                d.ResourceSnapshotHash != request.ResourceSnapshotHash)
                throw new ConflictException("Operation, arguments or resource snapshot changed. Request a new authorization.");
            var previous = await db.OperationExecutions.SingleOrDefaultAsync(x => x.OrganizationId == a.OrganizationId &&
                x.ApplicationId == a.ApplicationId && x.AgentId == a.AgentId && x.OperationId == request.OperationId, ct);
            if (previous is not null)
            {
                if (previous.DecisionId != d.Id) throw new ConflictException("Operation already consumed under another decision. Do not execute again.");
                return new ConsumePermitResult(false, true, previous.Id);
            }
            var permit = await db.Permits.SingleAsync(p => p.OrganizationId == a.OrganizationId && p.Id == permitId && p.DecisionId == d.Id, ct);
            if (d.Decision != DecisionEffect.Allow || !permit.IsActive(clock.UtcNow))
                throw new ConflictException("A live ALLOW permit is required.");
            if (d.Action is "llm.complete" or "llm.embed")
            {
                var route = await db.GatewayModelRoutes.AsNoTracking().SingleOrDefaultAsync(r =>
                    r.OrganizationId == a.OrganizationId && r.Enabled && r.Alias == d.ResourceId, ct);
                using var facts = JsonDocument.Parse(d.ResourceAttributesJson);
                var f = facts.RootElement;
                if (!await db.GatewaySettings.AnyAsync(g => g.OrganizationId == a.OrganizationId && g.Enabled, ct) || route is null ||
                    !f.TryGetProperty("provider", out var provider) || provider.GetString() != route.Provider ||
                    !f.TryGetProperty("provider_model", out var model) || model.GetString() != route.Model ||
                    !f.TryGetProperty("model_tier", out var tier) || tier.GetString() != route.Tier ||
                    !f.TryGetProperty("region", out var region) || region.GetString() != route.Region ||
                    !f.TryGetProperty("input_price_per_million", out var input) || input.GetDecimal() != route.InputPricePerMillion ||
                    !f.TryGetProperty("output_price_per_million", out var output) || output.GetDecimal() != route.OutputPricePerMillion)
                    throw new ConflictException("Gateway route changed or lacks an execution binding. Request new authorization.");
                if (!f.TryGetProperty("estimated_cost_usd", out var estimated) || estimated.GetDecimal() < 0 ||
                    !await db.GatewaySpendReservations.AnyAsync(r => r.OrganizationId == a.OrganizationId && r.AgentId == a.AgentId &&
                        r.DecisionId == d.Id && r.Status == "reserved" && r.ReservedUsd >= estimated.GetDecimal(), ct))
                    throw new ConflictException("An active, sufficient budget reservation is required before model execution.");
            }
            if (d.Action.StartsWith("admin.webhook.", StringComparison.Ordinal))
            {
                using var facts = JsonDocument.Parse(d.ResourceAttributesJson);
                if (facts.RootElement.TryGetProperty("reviewed_destination", out var destination))
                {
                    var endpoint = await db.WebhookEndpoints.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == a.OrganizationId && x.Id == d.ResourceId, ct);
                    if (endpoint is null || endpoint.Url != destination.GetString() ||
                        !facts.RootElement.TryGetProperty("reviewed_endpoint_version", out var version) || endpoint.UpdatedAt != version.GetDateTimeOffset())
                        throw new ConflictException("Webhook destination changed after review. Request a new authorization.");
                }
            }
            var fresh = await evaluator.EvaluateAsync(new AuthorizeCommand(a, a.AgentExternalId, a.EnvironmentName,
                d.ActorId, d.ActorType, d.DelegationId, d.Action, d.ResourceType, d.ResourceId,
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(d.ResourceAttributesJson), d.ResourceSnapshotHash,
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(d.ContextJson), d.IdempotencyKey,
                Operation: new(d.OperationId, d.OperationHash)), ct);
            if (fresh.Effect == DecisionEffect.Deny || fresh.PolicyId != d.PolicyId || fresh.PolicyVersion != d.PolicyVersion)
                throw new ConflictException("Authorization changed. Request a new authorization.");
            if (fresh.Effect == DecisionEffect.RequireApproval && !await db.ApprovalRequests.AnyAsync(r =>
                r.OrganizationId == a.OrganizationId && r.Id == d.ApprovalId && r.DecisionId == d.Id && r.Status == ApprovalStatus.Approved, ct))
                throw new ConflictException("A valid human approval is required.");
            if (!permit.IsActive(clock.UtcNow)) throw new ConflictException("Permission expired during consumption.");
            var deadline = permit.ExpiresAt;
            const string grantPrefix = "IDE temporary grant: ";
            if (fresh.Reason.StartsWith(grantPrefix, StringComparison.Ordinal))
            {
                var grantId = fresh.Reason[grantPrefix.Length..];
                var grant = await db.Set<Deleguard.Domain.Ide.IdeGrant>().AsNoTracking().SingleOrDefaultAsync(g =>
                    g.Id == grantId && g.OrganizationId == a.OrganizationId && g.ApplicationId == a.ApplicationId &&
                    g.AgentId == a.AgentId && g.RevokedAt == null, ct) ?? throw new ConflictException("Grant revoked.");
                if (grant.ExpiresAt < deadline) deadline = grant.ExpiresAt;
            }
            if (AuthorizeHandler.IsIdeAction(d.Action))
            {
                // Shared tombstone also prevents replay while an older IDE revision is draining.
                var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO ide_permit_uses ("DecisionId","OrganizationId","ApplicationId","AgentId","OperationId","SnapshotHash","ConsumedAt")
                    SELECT {d.Id},{a.OrganizationId},{a.ApplicationId},{a.AgentId},{d.OperationId},{d.ResourceSnapshotHash},clock_timestamp()
                    WHERE clock_timestamp() < {deadline} ON CONFLICT DO NOTHING
                    """, ct);
                if (inserted != 1) throw new ConflictException("IDE operation already consumed or expired. Do not execute again.");
            }
            var execution = new OperationExecution
            {
                Id = IdGenerator.New("exe"), OrganizationId = a.OrganizationId, ApplicationId = a.ApplicationId,
                EnvironmentId = a.EnvironmentId, AgentId = a.AgentId, DecisionId = d.Id, PermitId = permit.Id,
                OperationId = d.OperationId, OperationHash = d.OperationHash, ResourceSnapshotHash = d.ResourceSnapshotHash!,
                ConsumedAt = clock.UtcNow, ExecuteBefore = deadline
            };
            db.OperationExecutions.Add(execution);
            await audit.AppendAsync(a.OrganizationId, "execution.consumed", d.Id, JsonSerializer.SerializeToElement(new
                { execution_id = execution.Id, operation_id = d.OperationId, application_id = a.ApplicationId, permit_id = permit.Id }), ct);
            return new ConsumePermitResult(true, false, execution.Id);
        }, ct);
    }

    public async Task<ExecutionView?> GetAsync(AgentPrincipal a, string decisionId, CancellationToken ct)
    {
        var e = await db.OperationExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == a.OrganizationId &&
            x.ApplicationId == a.ApplicationId && x.EnvironmentId == a.EnvironmentId && x.AgentId == a.AgentId && x.DecisionId == decisionId, ct);
        return e is null ? null : new(e.Id, e.OperationId, e.State, e.Verification, e.ConsumedAt, e.ReportedAt, e.ProviderReference);
    }

    public Task<bool> AcceptReportAsync(AgentPrincipal a, string decisionId, string? executionId, string status,
        string? providerReference, string? resultHash, string? snapshotHash, Dictionary<string, JsonElement>? metadata, CancellationToken ct) =>
        state.RunAsync(a.OrganizationId, async () =>
        {
            var e = await db.OperationExecutions.SingleOrDefaultAsync(x => x.OrganizationId == a.OrganizationId &&
                x.ApplicationId == a.ApplicationId && x.EnvironmentId == a.EnvironmentId && x.AgentId == a.AgentId && x.DecisionId == decisionId, ct)
                ?? throw new ConflictException("Evidence requires a consumed execution.");
            if (executionId != e.Id || snapshotHash != e.ResourceSnapshotHash)
                throw new ConflictException("Execution identity and resource snapshot must match the consumed permit.");
            status = status.ToUpperInvariant();
            if (status is not ("SUCCESS" or "FAILED" or "PARTIAL" or "UNKNOWN")) throw new RequestValidationException("Invalid execution status.");
            var digest = CanonicalJson.HashHex(JsonSerializer.SerializeToNode(new { status, providerReference, resultHash, snapshotHash, metadata }));
            if (e.ReportDigest == digest) return false;
            if (e.ReportDigest is not null && e.State != "UNKNOWN") throw new ConflictException("Conflicting terminal evidence. Document a correction in the incident.");
            // An unknown result may become known later, even after revocation or expiry. The claim remains consumed.
            e.State = status; e.Verification = status == "UNKNOWN" ? "UNKNOWN" : "REPORTED";
            e.ReportDigest = digest; e.ReportedAt = clock.UtcNow; e.ProviderReference = providerReference; e.ResultHash = resultHash;
            return true;
        }, ct);

    public async Task<IReadOnlyList<IncidentView>> IncidentsAsync(string org, string app, CancellationToken ct) =>
        await db.OperationExecutions.AsNoTracking().Where(x => x.OrganizationId == org && x.ApplicationId == app &&
            (x.State == "UNKNOWN" || x.State == "PARTIAL") && x.ResolvedAt == null).OrderBy(x => x.ConsumedAt).Take(200)
            .Select(x => new IncidentView(x.Id, x.DecisionId, x.ApplicationId, x.AgentId, x.State, x.ConsumedAt, x.AssignedTo, x.Resolution)).ToListAsync(ct);

    public Task ResolveIncidentAsync(string org, string app, string executionId, string actor, string? assignedTo, string? resolution, CancellationToken ct) =>
        state.RunAsync(org, async () =>
        {
            if (assignedTo?.Length > 256 || resolution?.Length > 4000) throw new RequestValidationException("Incident text is too long.");
            var e = await db.OperationExecutions.SingleOrDefaultAsync(x => x.Id == executionId && x.OrganizationId == org && x.ApplicationId == app, ct)
                ?? throw new NotFoundException("Execution not found.");
            e.AssignedTo = assignedTo; e.Resolution = resolution;
            e.ResolvedAt = string.IsNullOrWhiteSpace(resolution) ? null : clock.UtcNow;
            await audit.AppendAsync(org, "execution.incident.updated", e.DecisionId,
                JsonSerializer.SerializeToElement(new { execution_id = e.Id, application_id = app, actor, assignedTo, resolution }), ct);
            return true;
        }, ct);

    public Task SuspendApplicationAsync(string org, string app, bool suspended, string actor, CancellationToken ct) =>
        state.RunAsync(org, async () =>
        {
            var application = await db.Applications.SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == app, ct)
                ?? throw new NotFoundException("Application not found.");
            if (application.IsAdministration) throw new ConflictException("The administration application cannot suspend itself.");
            application.Suspended = suspended;
            await audit.AppendAsync(org, "application.suspension.changed", null,
                JsonSerializer.SerializeToElement(new { application_id = app, suspended, actor }), ct);
            return true;
        }, ct);
}
