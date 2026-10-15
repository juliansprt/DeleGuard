using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Approvals;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;
using Deleguard.Domain.Identity;

namespace Deleguard.Application.Approvals;

public sealed record ResolveApprovalCommand(
    string OrganizationId,
    string ApprovalId,
    string Actor,
    IReadOnlyList<string> ActorRoles,
    IReadOnlyList<string> Permissions,
    bool Approve,
    DateTimeOffset? ExpiresAt,
    string? Note);

public sealed class ResolveApprovalHandler
{
    private readonly IApprovalStore _approvals;
    private readonly IDecisionStore _decisions;
    private readonly IPermitStore _permits;
    private readonly IPermitSigner _signer;
    private readonly IAgentDirectory _agents;
    private readonly IAuditLedger _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IStateCoordinator? _state;
    private readonly IUserDirectory? _users;

    public ResolveApprovalHandler(
        IApprovalStore approvals,
        IDecisionStore decisions,
        IPermitStore permits,
        IPermitSigner signer,
        IAgentDirectory agents,
        IAuditLedger audit,
        IOutbox outbox,
        IUnitOfWork uow,
        IClock clock, IStateCoordinator? state = null, IUserDirectory? users = null)
    {
        _approvals = approvals;
        _decisions = decisions;
        _permits = permits;
        _signer = signer;
        _agents = agents;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _clock = clock; _state = state; _users = users;
    }

    public Task<DecisionDto> HandleAsync(ResolveApprovalCommand command, CancellationToken cancellationToken) =>
        _state is null ? HandleCoreAsync(command, cancellationToken) : _state.RunAsync(command.OrganizationId,
            () => HandleCoreAsync(command, cancellationToken), cancellationToken);

    private async Task<DecisionDto> HandleCoreAsync(ResolveApprovalCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var approval = await _approvals.GetAsync(command.OrganizationId, command.ApprovalId, cancellationToken)
                       ?? throw new NotFoundException("Approval not found.");

        if (approval.Status == ApprovalStatus.Expired || approval.ExpiresAt <= now)
        {
            approval.Status = ApprovalStatus.Expired;
            await _uow.SaveChangesAsync(cancellationToken);
            throw new ConflictException("Approval has expired.");
        }

        if (approval.Status != ApprovalStatus.Pending)
        {
            throw new ConflictException("Approval already resolved.");
        }

        var roles = command.ActorRoles; var permissions = command.Permissions;
        if (_users is not null)
        {
            var user = (await _users.ListAsync(command.OrganizationId, cancellationToken)).SingleOrDefault(u =>
                string.Equals(u.Email, command.Actor, StringComparison.OrdinalIgnoreCase) || u.Id == command.Actor);
            var current = user is null ? null : await _users.GetPrincipalAsync(command.OrganizationId, user.Id, cancellationToken);
            if (current is null) throw new ForbiddenException("Approver is no longer active.");
            roles = current.Roles; permissions = current.Permissions;
        }
        if (!CanResolve(approval.ApproverRolesJson, roles, permissions))
        {
            throw new ForbiddenException("Approver is not in the required role for this request.");
        }

        var decision = await _decisions.GetAsync(command.OrganizationId, approval.DecisionId, cancellationToken)
                       ?? throw new FailClosedException("Approval is missing its decision.");

        approval.ResolvedAt = now;
        approval.ResolvedBy = command.Actor;
        approval.Status = command.Approve ? ApprovalStatus.Approved : ApprovalStatus.Denied;
        await _approvals.AddEventAsync(new ApprovalEvent
        {
            Id = IdGenerator.New("aev"),
            OrganizationId = command.OrganizationId,
            ApprovalRequestId = approval.Id,
            Action = command.Approve ? "APPROVE" : "DENY",
            Actor = command.Actor,
            Note = command.Note,
            CreatedAt = now
        }, cancellationToken);

        string? token = null;
        if (command.Approve)
        {
            decision.Decision = DecisionEffect.Allow;
            decision.Reason = "Approved by human.";
            var expires = command.ExpiresAt ?? now.AddMinutes(10);
            if (expires <= now || expires > now.AddMinutes(10)) throw new RequestValidationException("Approval permit must expire within ten minutes.");
            var agent = await _agents.GetAsync(command.OrganizationId, decision.AgentId, cancellationToken)
                        ?? throw new FailClosedException("Agent no longer exists.");
            var principal = await _agents.GetPrincipalAsync(command.OrganizationId, agent.Id, cancellationToken)
                            ?? throw new FailClosedException("Agent no longer exists.");
            if (_state is not null) await _state.EnsureActiveAsync(principal, cancellationToken);
            var permit = new Permit
            {
                Id = IdGenerator.New("perm"),
                OrganizationId = command.OrganizationId,
                DecisionId = decision.Id,
                Jti = string.Empty,
                Action = decision.Action,
                ResourceType = decision.ResourceType,
                ResourceId = decision.ResourceId,
                ResourceSnapshotHash = decision.ResourceSnapshotHash,
                PolicyVersion = decision.PolicyVersion,
            OperationId = decision.OperationId, OperationHash = decision.OperationHash,
                Token = string.Empty,
                IssuedAt = now,
                ExpiresAt = expires
            };
            permit = new Permit
            {
                Id = permit.Id,
                OrganizationId = permit.OrganizationId,
                DecisionId = permit.DecisionId,
                Jti = permit.Id,
                Action = permit.Action,
                ResourceType = permit.ResourceType,
                ResourceId = permit.ResourceId,
                ResourceSnapshotHash = permit.ResourceSnapshotHash,
                PolicyVersion = permit.PolicyVersion,
            OperationId = permit.OperationId, OperationHash = permit.OperationHash,
                Token = _signer.Sign(permit, principal, decision.ActorId),
                IssuedAt = permit.IssuedAt,
                ExpiresAt = permit.ExpiresAt
            };
            decision.PermitId = permit.Id;
            decision.ExpiresAt = permit.ExpiresAt;
            token = permit.Token;
            await _permits.AddAsync(permit, cancellationToken);
        }
        else
        {
            decision.Decision = DecisionEffect.Deny;
            decision.Reason = string.IsNullOrWhiteSpace(command.Note) ? "Denied by human." : command.Note;
        }

        var payload = JsonSerializer.SerializeToElement(new
        {
            approval_id = approval.Id,
            decision_id = decision.Id,
            application_id = decision.ApplicationId,
            action = command.Approve ? "APPROVE" : "DENY",
            actor = command.Actor
        });
        await _audit.AppendAsync(command.OrganizationId, "approval.resolved", decision.Id, payload, cancellationToken);
        await _outbox.EnqueueAsync(command.OrganizationId, "approval.resolved", payload, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return new DecisionDto(
            DecisionNames.ToApi(decision.Decision),
            decision.Id,
            decision.Reason,
            decision.PolicyId,
            decision.PolicyVersion,
            token,
            decision.ApprovalId,
            decision.ExpiresAt, decision.PermitId, decision.OperationId is null ? null : new(decision.OperationId, decision.OperationHash!), decision.ResourceSnapshotHash);
    }

    private static bool CanResolve(string approverRolesJson, IReadOnlyList<string> actorRoles, IReadOnlyList<string> permissions)
    {
        var isOrgAdmin = actorRoles.Contains(AccessControlIds.OrgAdminRole, StringComparer.Ordinal);
        var canResolve = permissions.Contains(PermissionCatalog.ApprovalsResolve, StringComparer.Ordinal) || isOrgAdmin;
        if (!canResolve)
        {
            return false;
        }

        var required = ParseRoles(approverRolesJson);
        if (required.Count == 0)
        {
            return isOrgAdmin;
        }

        return isOrgAdmin || required.Overlaps(actorRoles);
    }

    private static HashSet<string> ParseRoles(string json)
    {
        try
        {
            var roles = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return roles.Where(role => !string.IsNullOrWhiteSpace(role)).ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public sealed class ListApprovalsHandler
{
    private readonly IApprovalStore _approvals;

    public ListApprovalsHandler(IApprovalStore approvals)
    {
        _approvals = approvals;
    }

    public Task<IReadOnlyList<Domain.Approvals.ApprovalRequest>> HandleAsync(
        string organizationId,
        CancellationToken cancellationToken,
        string? applicationId = null) =>
        _approvals.ListPendingAsync(organizationId, applicationId, cancellationToken);

    public async Task<Domain.Approvals.ApprovalRequest> GetAsync(
        string organizationId,
        string approvalId,
        CancellationToken cancellationToken) =>
        await _approvals.GetAsync(organizationId, approvalId, cancellationToken)
        ?? throw new NotFoundException("Approval not found.");
}
