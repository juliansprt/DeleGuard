using Deleguard.Application.Abstractions;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Deleguard.Infrastructure.Stores;
public sealed class OperationLookup(DeleguardDbContext db) : IOperationLookup
{
    public Task<string?> FindDecisionAsync(AgentPrincipal a, string operationId, CancellationToken ct) =>
        db.AuthorizationDecisions.AsNoTracking().Where(d => d.OrganizationId == a.OrganizationId &&
            d.ApplicationId == a.ApplicationId && d.EnvironmentId == a.EnvironmentId && d.AgentId == a.AgentId && d.OperationId == operationId)
            .OrderBy(d => d.CreatedAt).Select(d => d.Id).FirstOrDefaultAsync(ct);
}
