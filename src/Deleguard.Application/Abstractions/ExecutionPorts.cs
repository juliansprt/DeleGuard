using System.Text.Json;
using Deleguard.Application.Authorization;

namespace Deleguard.Application.Abstractions;

/// <summary>Serializes security state transitions for an organization in one durable transaction.</summary>
public interface IStateCoordinator
{
    Task<T> RunAsync<T>(string organizationId, Func<Task<T>> action, CancellationToken ct);
    Task EnsureActiveAsync(AgentPrincipal principal, CancellationToken ct);
}

public sealed record OperationRef(string Id, string Hash);
public interface IAdminContinuationStore
{
    Task<string?> FindAsync(AgentPrincipal agent, string actorId, string operationHash, CancellationToken ct);
}
public sealed record ConsumePermitRequest(string OperationId, string OperationHash, string ResourceSnapshotHash);
public sealed record ConsumePermitResult(bool Acquired, bool AlreadyConsumed, string ExecutionId);
public sealed record ExecutionView(string ExecutionId, string OperationId, string State, string Verification,
    DateTimeOffset ConsumedAt, DateTimeOffset? ReportedAt, string? ProviderReference);
public sealed record IncidentView(string ExecutionId, string DecisionId, string ApplicationId, string AgentId,
    string State, DateTimeOffset CreatedAt, string? AssignedTo, string? Resolution);

public interface IExecutionControl
{
    Task<ConsumePermitResult> ConsumeAsync(AgentPrincipal principal, string permitId, ConsumePermitRequest request, CancellationToken ct);
    Task<ExecutionView?> GetAsync(AgentPrincipal principal, string decisionId, CancellationToken ct);
    Task<bool> AcceptReportAsync(AgentPrincipal principal, string decisionId, string? executionId, string status,
        string? providerReference, string? resultHash, string? snapshotHash, Dictionary<string, JsonElement>? metadata, CancellationToken ct);
    Task<IReadOnlyList<IncidentView>> IncidentsAsync(string organizationId, string applicationId, CancellationToken ct);
    Task ResolveIncidentAsync(string organizationId, string applicationId, string executionId, string actor,
        string? assignedTo, string? resolution, CancellationToken ct);
    Task SuspendApplicationAsync(string organizationId, string applicationId, bool suspended, string actor, CancellationToken ct);
}
