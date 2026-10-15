namespace Deleguard.Application.Abstractions;
public interface IOperationLookup
{
    Task<string?> FindDecisionAsync(AgentPrincipal principal, string operationId, CancellationToken ct);
}
