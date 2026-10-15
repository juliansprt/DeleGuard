using Deleguard.Domain.Policies;

namespace Deleguard.Application.Abstractions;

public interface IPolicySimulationStore
{
    Task<IReadOnlyList<PolicySimulationCase>> ListAsync(string organizationId, string applicationId, string policyId, CancellationToken cancellationToken);
    Task AddAsync(PolicySimulationCase fixture, CancellationToken cancellationToken);
    Task DeleteAsync(string organizationId, string applicationId, string policyId, string id, CancellationToken cancellationToken);
}
