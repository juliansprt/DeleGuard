using Deleguard.Application.Abstractions;
using Deleguard.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Persistence;

public sealed class EfPolicySimulationStore(DeleguardDbContext db) : IPolicySimulationStore
{
    public async Task<IReadOnlyList<PolicySimulationCase>> ListAsync(string organizationId, string applicationId, string policyId, CancellationToken cancellationToken) =>
        await db.Set<PolicySimulationCase>().AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.PolicyId == policyId)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);

    public async Task AddAsync(PolicySimulationCase fixture, CancellationToken cancellationToken) =>
        await db.Set<PolicySimulationCase>().AddAsync(fixture, cancellationToken);

    public async Task DeleteAsync(string organizationId, string applicationId, string policyId, string id, CancellationToken cancellationToken)
    {
        var fixture = await db.Set<PolicySimulationCase>().SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.PolicyId == policyId && x.Id == id, cancellationToken);
        if (fixture is not null) db.Remove(fixture);
    }
}
