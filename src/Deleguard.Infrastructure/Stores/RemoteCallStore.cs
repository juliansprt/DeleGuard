using Deleguard.Application.Abstractions;
using Deleguard.Domain.Evidence;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Deleguard.Infrastructure.Stores;
public sealed class RemoteCallStore(DeleguardDbContext db, IClock clock) : IRemoteCallStore
{
    public async Task SaveAsync(AgentPrincipal a, PendingRemoteCall call, CancellationToken ct)
    {
        db.Set<RemoteGuardCall>().Add(new() { DecisionId = call.DecisionId, OrganizationId = a.OrganizationId,
            ApplicationId = a.ApplicationId, EnvironmentId = a.EnvironmentId, AgentId = a.AgentId,
            Tool = call.Tool, ProtectedArguments = call.ProtectedArguments, BindingHash = call.BindingHash, ExpiresAt = call.ExpiresAt });
        await db.SaveChangesAsync(ct);
    }
    public async Task<PendingRemoteCall?> GetAsync(AgentPrincipal a, string id, CancellationToken ct) =>
        await db.Set<RemoteGuardCall>().AsNoTracking().Where(c => c.DecisionId == id && c.OrganizationId == a.OrganizationId &&
            c.ApplicationId == a.ApplicationId && c.EnvironmentId == a.EnvironmentId && c.AgentId == a.AgentId && c.ExpiresAt > clock.UtcNow)
            .Select(c => new PendingRemoteCall(c.DecisionId, c.Tool, c.ProtectedArguments, c.BindingHash, c.ExpiresAt)).SingleOrDefaultAsync(ct);
}
internal sealed class RemoteGuardCallConfiguration : IEntityTypeConfiguration<RemoteGuardCall>
{
    public void Configure(EntityTypeBuilder<RemoteGuardCall> b)
    {
        b.ToTable("remote_guard_calls"); b.HasKey(c => c.DecisionId); b.HasIndex(c => c.ExpiresAt);
        b.HasIndex(c => new { c.OrganizationId, c.ApplicationId, c.AgentId });
    }
}
