using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;

namespace Deleguard.Application.Evidence;

public sealed record ReportExecutionCommand(
    AgentPrincipal Principal,
    string DecisionId,
    string? PermitId,
    string Status,
    string? ProviderReference,
    string? ResultHash,
    string? ResourceSnapshotHash,
    Dictionary<string, JsonElement>? Metadata,
    string? AuditSource = null, string? ExecutionId = null);

public sealed class ReportExecutionHandler
{
    private readonly IDecisionStore _decisions;
    private readonly IPermitStore _permits;
    private readonly IEvidenceStore _evidence;
    private readonly IAuditLedger _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IIdeControl? _ide;
    private readonly IExecutionControl? _execution;
    private readonly IStateCoordinator? _state;

    public ReportExecutionHandler(
        IDecisionStore decisions,
        IPermitStore permits,
        IEvidenceStore evidence,
        IAuditLedger audit,
        IOutbox outbox,
        IUnitOfWork uow,
        IClock clock, IIdeControl? ide = null, IExecutionControl? execution = null, IStateCoordinator? state = null)
    {
        _decisions = decisions;
        _permits = permits;
        _evidence = evidence;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _clock = clock; _ide = ide; _execution = execution; _state = state;
    }

    public Task HandleAsync(ReportExecutionCommand command, CancellationToken cancellationToken) =>
        _state is null ? HandleCoreAsync(command, cancellationToken) : _state.RunAsync(command.Principal.OrganizationId,
            async () => { await HandleCoreAsync(command, cancellationToken); return true; }, cancellationToken);

    private async Task HandleCoreAsync(ReportExecutionCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var decision = await _decisions.GetAsync(command.Principal.OrganizationId, command.DecisionId, cancellationToken)
                       ?? throw new NotFoundException("Decision not found.");

        if (decision.AgentId != command.Principal.AgentId || decision.ApplicationId != command.Principal.ApplicationId || decision.EnvironmentId != command.Principal.EnvironmentId)
        {
            throw new NotFoundException("Decision not found.");
        }

        if (decision.Decision != DecisionEffect.Allow || decision.PermitId is null)
        {
            throw new ConflictException("Evidence can only attach to a permitted decision.");
        }

        var permit = await _permits.GetByDecisionAsync(command.Principal.OrganizationId, decision.Id, cancellationToken)
                     ?? throw new ConflictException("Permit not found for decision.");
        var isIde = IsIdeAction(decision.Action);
        var consumed = _execution is not null || isIde && _ide is not null && permit.ResourceSnapshotHash is not null && await _ide.CanReportAsync(command.Principal, decision.Id, permit.ResourceSnapshotHash, permit.ExpiresAt, cancellationToken);
        if (isIde && !consumed) throw new ConflictException("IDE evidence requires a consumed operation permit.");
        // Late client reports are accepted only for IDE permits consumed while valid; this never releases execution.
        if (!consumed && !permit.IsActive(now))
        {
            throw new ConflictException("Permit is expired or revoked.");
        }

        if (!string.IsNullOrWhiteSpace(command.PermitId) &&
            !string.Equals(command.PermitId, permit.Id, StringComparison.Ordinal) &&
            !string.Equals(command.PermitId, permit.Jti, StringComparison.Ordinal))
        {
            throw new ConflictException("permit_id does not match the decision permit.");
        }

        if (!string.IsNullOrWhiteSpace(command.ResourceSnapshotHash) &&
            !string.Equals(command.ResourceSnapshotHash, permit.ResourceSnapshotHash, StringComparison.Ordinal))
        {
            throw new ConflictException("Resource snapshot changed; reauthorization is required.");
        }

        if (_execution is not null && !await _execution.AcceptReportAsync(command.Principal, decision.Id, command.ExecutionId,
            command.Status, command.ProviderReference, command.ResultHash, command.ResourceSnapshotHash, command.Metadata, cancellationToken)) return;
        if (!Enum.TryParse<ExecutionStatus>(command.Status, true, out var status))
        {
            status = command.Status.ToUpperInvariant() switch
            {
                "SUCCESS" => ExecutionStatus.Success,
                "FAILED" => ExecutionStatus.Failed,
                "PARTIAL" => ExecutionStatus.Partial,
                _ => ExecutionStatus.Unknown
            };
        }

        var evidence = new ExecutionEvidence
        {
            Id = IdGenerator.New("evd"),
            OrganizationId = command.Principal.OrganizationId,
            DecisionId = decision.Id,
            PermitId = permit.Id,
            Status = status,
            ProviderReference = command.ProviderReference,
            ResultHash = command.ResultHash,
            ResourceSnapshotHash = command.ResourceSnapshotHash ?? permit.ResourceSnapshotHash,
            MetadataJson = JsonSerializer.Serialize(command.Metadata ?? new Dictionary<string, JsonElement>()),
            CreatedAt = now
        };
        await _evidence.AddAsync(evidence, cancellationToken);
        var payload = JsonSerializer.SerializeToElement(new
        {
            evidence_id = evidence.Id,
            decision_id = decision.Id,
            application_id = decision.ApplicationId,
            permit_id = permit.Id,
            status = command.Status,
            provider_reference = command.ProviderReference,
            source = string.IsNullOrWhiteSpace(command.AuditSource) ? null : command.AuditSource.Trim()
        });
        await _audit.AppendAsync(command.Principal.OrganizationId, "execution.reported", decision.Id, payload, cancellationToken);
        await _outbox.EnqueueAsync(command.Principal.OrganizationId, "execution.reported", payload, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    private static bool IsIdeAction(string action) => action.StartsWith("filesystem.", StringComparison.Ordinal) || action is
        "process.execute" or "network.connect" or "git.clone" or "git.fetch" or "git.commit" or "git.push" or "package.install" or
        "service.manage" or "credential.use" or "mcp.discover" or "mcp.call" or "ide.settings.change" or "extension.install" or
        "debug.attach" or "browser.navigate";
}
