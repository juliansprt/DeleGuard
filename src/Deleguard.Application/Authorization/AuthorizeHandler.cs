using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Approvals;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;
using Deleguard.Domain.Identity;
using Deleguard.Policy;

namespace Deleguard.Application.Authorization;

public sealed record AuthorizeCommand(
    AgentPrincipal Principal,
    string RequestedAgentId,
    string? RequestedEnvironment,
    string? ActorId,
    string? ActorType,
    string? DelegationId,
    string Action,
    string ResourceType,
    string ResourceId,
    Dictionary<string, JsonElement>? ResourceAttributes,
    string? SnapshotHash,
    Dictionary<string, JsonElement>? Context,
    string IdempotencyKey,
    string? AuditSource = null, OperationRef? Operation = null);

public sealed record DecisionDto(
    string Decision,
    string DecisionId,
    string? Reason,
    string? PolicyId,
    int? PolicyVersion,
    string? Permit,
    string? ApprovalId,
    DateTimeOffset? ExpiresAt, string? PermitId = null, OperationRef? Operation = null,
    string? ResourceSnapshotHash = null, ExecutionView? Execution = null);

public sealed class AuthorizeHandler
{
    private static readonly Regex ActionPattern = new("^[a-z][a-z0-9_.:-]{1,127}$", RegexOptions.Compiled);
    private readonly IActionRegistry _actions;
    private readonly IActionGrantStore _actionGrants;
    private readonly IPolicyRepository _policies;
    private readonly IPolicyEngine _engine;
    private readonly IDecisionStore _decisions;
    private readonly IIdempotencyStore _idempotency;
    private readonly IApprovalStore _approvals;
    private readonly IPermitStore _permits;
    private readonly IPermitSigner _signer;
    private readonly IAuditLedger _audit;
    private readonly IOutbox _outbox;
    private readonly IActorDirectory _actors;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IIdeControl? _ide;
    private readonly IStateCoordinator? _state;

    public AuthorizeHandler(
        IActionRegistry actions,
        IActionGrantStore actionGrants,
        IPolicyRepository policies,
        IPolicyEngine engine,
        IDecisionStore decisions,
        IIdempotencyStore idempotency,
        IApprovalStore approvals,
        IPermitStore permits,
        IPermitSigner signer,
        IAuditLedger audit,
        IOutbox outbox,
        IActorDirectory actors,
        IUnitOfWork uow,
        IClock clock, IIdeControl? ide = null, IStateCoordinator? state = null)
    {
        _actions = actions;
        _actionGrants = actionGrants;
        _policies = policies;
        _engine = engine;
        _decisions = decisions;
        _idempotency = idempotency;
        _approvals = approvals;
        _permits = permits;
        _signer = signer;
        _audit = audit;
        _outbox = outbox;
        _actors = actors;
        _uow = uow;
        _clock = clock; _ide = ide; _state = state;
    }

    public Task<DecisionDto> HandleAsync(AuthorizeCommand command, CancellationToken cancellationToken) =>
        _state is null ? HandleCoreAsync(command, cancellationToken) : _state.RunAsync(command.Principal.OrganizationId,
            async () => { await _state.EnsureActiveAsync(command.Principal, cancellationToken); return await HandleCoreAsync(command, cancellationToken); }, cancellationToken);

    private async Task<DecisionDto> HandleCoreAsync(AuthorizeCommand command, CancellationToken cancellationToken)
    {
        // Legacy IDE adapters already bind these fields in their canonical facts.
        if (command.Operation is null && IsIdeAction(command.Action) && command.ResourceAttributes is { } a &&
            a.TryGetValue("operation_id", out var id) && id.ValueKind == JsonValueKind.String &&
            a.TryGetValue("operation_hash", out var hash) && hash.ValueKind == JsonValueKind.String)
            command = command with { Operation = new OperationRef(id.GetString()!, hash.GetString()!) };
        Validate(command);
        var now = _clock.UtcNow;
        var attributes = command.ResourceAttributes ?? new Dictionary<string, JsonElement>();
        var context = command.Context ?? new Dictionary<string, JsonElement>();
        var attributesJson = JsonSerializer.Serialize(attributes);
        var contextJson = JsonSerializer.Serialize(context);
        var digest = BuildDigest(command, attributes, context);
        var snapshot = command.SnapshotHash ?? CanonicalJson.HashHex(attributesJson);

        var existing = await _idempotency.GetAsync(
            command.Principal.OrganizationId,
            command.Principal.ApplicationId,
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestDigest, digest, StringComparison.Ordinal))
            {
                throw new IdempotencyConflictException("Idempotency-Key was reused with a different request.");
            }

            return JsonSerializer.Deserialize<DecisionDto>(existing.ResponseJson)
                   ?? throw new FailClosedException("Stored idempotent response could not be read.");
        }

        var evaluation = await new AuthorizationEvaluator(_actions, _actionGrants, _policies, _engine, _actors, _ide)
            .EvaluateAsync(command, cancellationToken);

        var decisionId = IdGenerator.New("dec");
        var decision = new AuthorizationDecision
        {
            Id = decisionId,
            OrganizationId = command.Principal.OrganizationId,
            ApplicationId = command.Principal.ApplicationId,
            EnvironmentId = command.Principal.EnvironmentId,
            AgentId = command.Principal.AgentId,
            AgentExternalId = command.Principal.AgentExternalId,
            ActorId = command.ActorId,
            ActorType = command.ActorType,
            DelegationId = command.DelegationId,
            Action = command.Action,
            ResourceType = command.ResourceType,
            ResourceId = command.ResourceId,
            ResourceAttributesJson = attributesJson,
            ResourceSnapshotHash = snapshot,
            ContextJson = contextJson,
            Decision = evaluation.Effect,
            Reason = evaluation.Reason,
            PolicyId = evaluation.PolicyId,
            PolicyVersion = evaluation.PolicyVersion,
            MatchedRule = evaluation.MatchedRule,
            RequestDigest = digest,
            IdempotencyKey = command.IdempotencyKey,
            OperationId = command.Operation?.Id,
            OperationHash = command.Operation?.Hash,
            CreatedAt = now
        };

        string? permitToken = null;
        if (evaluation.Effect == DecisionEffect.Allow)
        {
            var permit = CreatePermit(command, decision, snapshot, now, TimeSpan.FromMinutes(10));
            decision.PermitId = permit.Id;
            decision.ExpiresAt = permit.ExpiresAt;
            permitToken = permit.Token;
            await _permits.AddAsync(permit, cancellationToken);
        }
        else if (evaluation.Effect == DecisionEffect.RequireApproval)
        {
            var ttl = evaluation.ApprovalExpiresIn ?? TimeSpan.FromMinutes(15);
            var approval = new ApprovalRequest
            {
                Id = IdGenerator.New("apr"),
                OrganizationId = command.Principal.OrganizationId,
                DecisionId = decisionId,
                Status = ApprovalStatus.Pending,
                ExpiresAt = now.Add(ttl),
                ApproverRolesJson = JsonSerializer.Serialize(evaluation.ApproverRoles),
                CanonicalFactsJson = JsonSerializer.Serialize(new
                {
                    agent = command.Principal.AgentExternalId,
                    actor = command.ActorId,
                    action = command.Action,
                    resource_type = command.ResourceType,
                    resource_id = command.ResourceId,
                    attributes,
                    environment = command.Principal.EnvironmentName,
                    policy_id = evaluation.PolicyId,
                    policy_version = evaluation.PolicyVersion,
                    expires_at = now.Add(ttl)
                }),
                CreatedAt = now
            };
            decision.ApprovalId = approval.Id;
            decision.ExpiresAt = approval.ExpiresAt;
            await _approvals.AddAsync(approval, cancellationToken);
        }

        await _decisions.AddAsync(decision, cancellationToken);

        var dto = new DecisionDto(
            DecisionNames.ToApi(decision.Decision),
            decision.Id,
            decision.Reason,
            decision.PolicyId,
            decision.PolicyVersion,
            permitToken,
            decision.ApprovalId,
            decision.ExpiresAt, decision.PermitId, command.Operation, snapshot);

        await _idempotency.AddAsync(new IdempotencyRecord
        {
            OrganizationId = command.Principal.OrganizationId,
            ApplicationId = command.Principal.ApplicationId,
            Key = command.IdempotencyKey,
            RequestDigest = digest,
            DecisionId = decision.Id,
            ResponseJson = JsonSerializer.Serialize(dto),
            CreatedAt = now
        }, cancellationToken);

        var payload = JsonSerializer.SerializeToElement(new
        {
            decision_id = decision.Id,
            decision = dto.Decision,
            action = decision.Action,
            application_id = decision.ApplicationId,
            resource_id = decision.ResourceId,
            policy_id = decision.PolicyId,
            policy_version = decision.PolicyVersion,
            matched_rule = decision.MatchedRule,
            source = ResolveAuditSource(command.AuditSource, context)
        });
        await _audit.AppendAsync(command.Principal.OrganizationId, "authorization.decided", decision.Id, payload, cancellationToken);
        await _outbox.EnqueueAsync(command.Principal.OrganizationId, "authorization.decided", payload, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return dto;
    }

    private Permit CreatePermit(AuthorizeCommand command, AuthorizationDecision decision, string snapshot, DateTimeOffset now, TimeSpan ttl)
    {
        var permitId = IdGenerator.New("perm");
        var permit = new Permit
        {
            Id = permitId,
            OrganizationId = command.Principal.OrganizationId,
            DecisionId = decision.Id,
            Jti = permitId,
            Action = decision.Action,
            ResourceType = decision.ResourceType,
            ResourceId = decision.ResourceId,
            ResourceSnapshotHash = snapshot,
            PolicyVersion = decision.PolicyVersion,
            OperationId = decision.OperationId, OperationHash = decision.OperationHash,
            Token = string.Empty,
            IssuedAt = now,
            ExpiresAt = now.Add(ttl)
        };
        permit = new Permit
        {
            Id = permit.Id,
            OrganizationId = permit.OrganizationId,
            DecisionId = permit.DecisionId,
            Jti = permit.Jti,
            Action = permit.Action,
            ResourceType = permit.ResourceType,
            ResourceId = permit.ResourceId,
            ResourceSnapshotHash = permit.ResourceSnapshotHash,
            PolicyVersion = permit.PolicyVersion,
            OperationId = permit.OperationId, OperationHash = permit.OperationHash,
            Token = _signer.Sign(permit, command.Principal, command.ActorId),
            IssuedAt = permit.IssuedAt,
            ExpiresAt = permit.ExpiresAt
        };
        return permit;
    }

    private static string? ResolveAuditSource(string? attested, Dictionary<string, JsonElement> context)
    {
        if (!string.IsNullOrWhiteSpace(attested))
        {
            return attested.Trim();
        }

        var fromContext = ReadSource(context);
        return string.Equals(fromContext, "playground", StringComparison.Ordinal) ? null : fromContext;
    }

    private static string? ReadSource(Dictionary<string, JsonElement> context)
    {
        if (!context.TryGetValue("source", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    public static void Validate(AuthorizeCommand command)
    {
        if (command.Operation is { } operation && (string.IsNullOrWhiteSpace(operation.Id) || operation.Id.Length > 128 ||
            operation.Id.Any(char.IsControl) || !Regex.IsMatch(operation.Hash ?? "", "^[a-f0-9]{64}$")))
            throw new RequestValidationException("operation requires an id (1-128 characters) and a lowercase SHA-256 hash of canonical arguments.");
        if (command.Operation is { } ideOperation && IsIdeAction(command.Action) &&
            (command.ResourceAttributes is not { } ideAttrs || !ideAttrs.TryGetValue("operation_id", out var ideId) ||
             ideId.ValueKind != JsonValueKind.String || ideId.GetString() != ideOperation.Id ||
             !ideAttrs.TryGetValue("operation_hash", out var ideHash) || ideHash.ValueKind != JsonValueKind.String || ideHash.GetString() != ideOperation.Hash))
            throw new RequestValidationException("IDE operation must match its canonical resource facts.");
        if (command.IdempotencyKey.Length is < 8 or > 128)
        {
            throw new RequestValidationException("Idempotency-Key must be 8-128 characters.");
        }

        if (!ActionPattern.IsMatch(command.Action))
        {
            throw new RequestValidationException("Action name is invalid.");
        }

        if (!string.Equals(command.RequestedAgentId, command.Principal.AgentExternalId, StringComparison.Ordinal))
        {
            throw new RequestValidationException("agent.id does not match the authenticated agent.");
        }

        if (!string.IsNullOrWhiteSpace(command.RequestedEnvironment) &&
            !string.Equals(command.RequestedEnvironment, command.Principal.EnvironmentName, StringComparison.Ordinal))
        {
            throw new RequestValidationException("agent.environment does not match the authenticated agent environment.");
        }

        if (string.IsNullOrWhiteSpace(command.ResourceType) || string.IsNullOrWhiteSpace(command.ResourceId))
        {
            throw new RequestValidationException("resource.type and resource.id are required.");
        }
    }

    private static string BuildDigest(AuthorizeCommand command, Dictionary<string, JsonElement> attributes, Dictionary<string, JsonElement> context)
    {
        var node = new JsonObject
        {
            ["operation"] = JsonSerializer.SerializeToNode(command.Operation),
            ["application_id"] = command.Principal.ApplicationId,
            ["agent"] = command.RequestedAgentId,
            ["environment"] = command.RequestedEnvironment,
            ["actor"] = command.ActorId,
            ["actor_type"] = command.ActorType,
            ["delegation_id"] = command.DelegationId,
            ["action"] = command.Action,
            ["resource_type"] = command.ResourceType,
            ["resource_id"] = command.ResourceId,
            ["attributes"] = JsonSerializer.SerializeToNode(attributes),
            ["snapshot_hash"] = command.SnapshotHash,
            ["context"] = JsonSerializer.SerializeToNode(context)
        };
        return CanonicalJson.HashHex(node);
    }

    public static bool IsIdeAction(string action) => action.StartsWith("filesystem.", StringComparison.Ordinal) || action is
        "process.execute" or "network.connect" or "git.clone" or "git.fetch" or "git.commit" or "git.push" or "package.install" or
        "service.manage" or "credential.use" or "mcp.discover" or "mcp.call" or "ide.settings.change" or "extension.install" or
        "debug.attach" or "browser.navigate";
}
