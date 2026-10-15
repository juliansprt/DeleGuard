using Deleguard.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deleguard.Infrastructure.Persistence.Configurations;

internal sealed class PolicySimulationConfiguration : IEntityTypeConfiguration<PolicySimulationCase>
{
    public void Configure(EntityTypeBuilder<PolicySimulationCase> builder)
    {
        builder.ToTable("policy_simulation_cases");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.ApplicationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PolicyId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ExpectedEffect).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.ApplicationId, x.PolicyId });
        builder.HasOne<PolicyDefinition>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Cascade);
    }
}
