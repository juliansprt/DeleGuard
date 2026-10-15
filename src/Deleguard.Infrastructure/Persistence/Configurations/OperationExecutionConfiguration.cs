using Deleguard.Domain.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deleguard.Infrastructure.Persistence.Configurations;

internal sealed class OperationExecutionConfiguration : IEntityTypeConfiguration<OperationExecution>
{
    public void Configure(EntityTypeBuilder<OperationExecution> b)
    {
        b.ToTable("operation_executions");
        b.HasKey(x => x.Id);
        b.Property(x => x.OperationId).HasMaxLength(128);
        b.Property(x => x.OperationHash).HasMaxLength(64);
        b.Property(x => x.ResourceSnapshotHash).HasMaxLength(64);
        b.Property(x => x.ReportDigest).HasMaxLength(64);
        b.HasIndex(x => x.DecisionId).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.ApplicationId, x.AgentId, x.OperationId }).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.ApplicationId, x.State, x.ConsumedAt });
        b.HasOne<Permit>().WithMany().HasForeignKey(x => x.PermitId).OnDelete(DeleteBehavior.Restrict);
    }
}
