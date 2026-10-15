using Deleguard.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deleguard.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.Email).HasMaxLength(254).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(256);
        builder.Property(x => x.ExternalIssuer).HasMaxLength(256);
        builder.Property(x => x.ExternalSubject).HasMaxLength(256);
        builder.HasIndex(x => new { x.OrganizationId, x.Email }).IsUnique();
        builder.HasIndex(x => new { x.ExternalIssuer, x.ExternalSubject })
            .IsUnique()
            .HasFilter("\"ExternalIssuer\" IS NOT NULL AND \"ExternalSubject\" IS NOT NULL");
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.Slug).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Slug }).IsUnique();
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.Name).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(x => new { x.UserId, x.RoleId });
        builder.TenantId();
        builder.HasIndex(x => new { x.OrganizationId, x.UserId });
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(x => new { x.RoleId, x.PermissionId });
        builder.TenantId();
    }
}

internal sealed class OrganizationMcpSettingsConfiguration : IEntityTypeConfiguration<OrganizationMcpSettings>
{
    public void Configure(EntityTypeBuilder<OrganizationMcpSettings> builder)
    {
        builder.ToTable("organization_mcp_settings");
        builder.HasKey(x => x.OrganizationId);
        builder.Property(x => x.OrganizationId).HasMaxLength(64);
        builder.Property(x => x.AgentId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EnabledToolsJson).HasColumnType("jsonb").IsRequired();
    }
}

internal sealed class McpCredentialConfiguration : IEntityTypeConfiguration<McpCredential>
{
    public void Configure(EntityTypeBuilder<McpCredential> builder)
    {
        builder.ToTable("mcp_credentials");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.KeyPrefix).HasMaxLength(24).IsRequired();
        builder.Property(x => x.KeyHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.KeyHash).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.UserId });
    }
}
