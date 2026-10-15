using Deleguard.Domain.Ide;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Deleguard.Infrastructure.Persistence.Configurations;
public sealed class IdeProjectConfiguration : IEntityTypeConfiguration<IdeProject>
{
    public void Configure(EntityTypeBuilder<IdeProject> b) { b.ToTable("ide_projects"); b.HasKey(x=>new {x.OrganizationId,x.ApplicationId,x.Id}); b.Property(x=>x.ProtectedPathsJson).HasColumnType("jsonb"); b.Property(x=>x.DeniedPathsJson).HasColumnType("jsonb"); }
}
public sealed class IdeSessionConfiguration : IEntityTypeConfiguration<IdeSession>
{
    public void Configure(EntityTypeBuilder<IdeSession> b) { b.ToTable("ide_sessions"); b.HasKey(x=>new {x.OrganizationId,x.ApplicationId,x.AgentId,x.Id}); b.Property(x=>x.CapabilitiesJson).HasColumnType("jsonb"); }
}
public sealed class IdeGrantConfiguration : IEntityTypeConfiguration<IdeGrant>
{
    public void Configure(EntityTypeBuilder<IdeGrant> b) { b.ToTable("ide_grants"); b.HasKey(x=>x.Id); b.HasIndex(x=>new {x.OrganizationId,x.ApplicationId,x.ProjectId,x.AgentId}); b.Property(x=>x.ActionsJson).HasColumnType("jsonb"); }
}
public sealed class IdePermitUseConfiguration : IEntityTypeConfiguration<IdePermitUse>
{
    public void Configure(EntityTypeBuilder<IdePermitUse> b) { b.ToTable("ide_permit_uses"); b.HasKey(x=>x.DecisionId); b.HasIndex(x=>new {x.OrganizationId,x.ApplicationId,x.AgentId,x.OperationId}).IsUnique(); }
}
