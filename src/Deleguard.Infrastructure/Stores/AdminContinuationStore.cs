using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Deleguard.Infrastructure.Stores;
public sealed class AdminContinuationStore(DeleguardDbContext db, IClock clock) : IAdminContinuationStore
{
    public async Task<string?> FindAsync(AgentPrincipal a, string actor, string hash, CancellationToken ct)
    {
        var d = await db.AuthorizationDecisions.AsNoTracking().Where(d => d.OrganizationId == a.OrganizationId &&
            d.ApplicationId == a.ApplicationId && d.AgentId == a.AgentId && d.ActorId == actor && d.OperationHash == hash && d.Action.StartsWith("admin."))
            .OrderByDescending(d => d.CreatedAt).FirstOrDefaultAsync(ct);
        if (d is null) return null;
        var execution = await db.OperationExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.DecisionId == d.Id, ct);
        if (execution is not null)
        {
            if (execution.State is "UNKNOWN" or "PARTIAL" && execution.ResolvedAt is null)
                throw new ConflictException("The previous administrative operation has an uncertain outcome. Resolve its incident before starting another.");
            return null;
        }
        return d.ExpiresAt > clock.UtcNow && d.Decision is DecisionEffect.Allow or DecisionEffect.RequireApproval ? d.Id : null;
    }
}
