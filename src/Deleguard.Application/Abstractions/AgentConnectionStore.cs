namespace Deleguard.Application.Abstractions;

public interface IAgentConnectionStore
{
    /// <summary>Atomically add one agent's action and policy grants, preserving other assignments and action settings.</summary>
    Task ConnectAsync(string organizationId, string applicationId, string agentId, string actionId, string policyId, CancellationToken ct);
}
