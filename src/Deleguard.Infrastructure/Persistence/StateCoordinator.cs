using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Persistence;

public sealed class StateCoordinator(DeleguardDbContext db) : IStateCoordinator
{
    public async Task<T> RunAsync<T>(string organizationId, Func<Task<T>> action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await LockAsync(organizationId, ct);
            return await action();
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(organizationId, ct);
        try
        {
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private Task<int> LockAsync(string org, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"deleguard-state:" + org}, 0))", ct);

    public async Task EnsureActiveAsync(AgentPrincipal principal, CancellationToken ct)
    {
        var active = await db.Agents.AsNoTracking().AnyAsync(a => a.Id == principal.AgentId && a.Enabled &&
            a.OrganizationId == principal.OrganizationId && a.ApplicationId == principal.ApplicationId &&
            a.EnvironmentId == principal.EnvironmentId, ct);
        var suspended = await db.Applications.AsNoTracking().AnyAsync(a => a.Id == principal.ApplicationId &&
            a.OrganizationId == principal.OrganizationId && a.Suspended, ct);
        if (!active || suspended) throw new ForbiddenException("Agent or application is suspended.");
    }
}
