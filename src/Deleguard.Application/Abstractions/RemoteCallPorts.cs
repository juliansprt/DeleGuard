namespace Deleguard.Application.Abstractions;
public sealed record PendingRemoteCall(string DecisionId, string Tool, string ProtectedArguments, string BindingHash, DateTimeOffset ExpiresAt);
public interface IRemoteCallStore
{
    Task SaveAsync(AgentPrincipal principal, PendingRemoteCall call, CancellationToken ct);
    Task<PendingRemoteCall?> GetAsync(AgentPrincipal principal, string decisionId, CancellationToken ct);
}
