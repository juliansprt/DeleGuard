using Deleguard.Domain.Approvals;
using Deleguard.Domain.Audit;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Evidence;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Playground;
using Deleguard.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deleguard.Infrastructure.Persistence.Configurations;

internal static class Mapping
{
    public static void TenantId<T>(this EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property("OrganizationId").HasMaxLength(64).IsRequired();
        builder.HasIndex("OrganizationId");
    }
}

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
    }
}

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<OrganizationApplication>
{
    public void Configure(EntityTypeBuilder<OrganizationApplication> builder)
    {
        builder.ToTable("organization_applications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.Slug).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Slug }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.Kind });
        builder.Ignore(x => x.IsAdministration);
        builder.Ignore(x => x.IsProduct);
    }
}

internal sealed class EnvironmentConfiguration : IEntityTypeConfiguration<OrganizationEnvironment>
{
    public void Configure(EntityTypeBuilder<OrganizationEnvironment> builder)
    {
        builder.ToTable("organization_environments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.ApplicationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.ApplicationId, x.Name }).IsUnique();
        builder.HasIndex(x => x.ApplicationId);
    }
}

internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("agents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.ApplicationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.ApplicationId, x.EnvironmentId, x.ExternalId }).IsUnique();
        builder.HasIndex(x => x.ApplicationId);
    }
}

internal sealed class AgentCredentialConfiguration : IEntityTypeConfiguration<AgentCredential>
{
    public void Configure(EntityTypeBuilder<AgentCredential> builder)
    {
        builder.ToTable("agent_credentials");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.KeyPrefix).HasMaxLength(24).IsRequired();
        builder.Property(x => x.KeyHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.KeyHash).IsUnique();
    }
}

internal sealed class ActionDefinitionConfiguration : IEntityTypeConfiguration<ActionDefinition>
{
    public void Configure(EntityTypeBuilder<ActionDefinition> builder)
    {
        builder.ToTable("action_definitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.ApplicationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.ApplicationId, x.Name }).IsUnique();
        builder.Property(x => x.Risk).HasConversion<string>().HasMaxLength(16);
    }
}

internal sealed class AgentActionGrantConfiguration : IEntityTypeConfiguration<AgentActionGrant>
{
    public void Configure(EntityTypeBuilder<AgentActionGrant> builder)
    {
        builder.ToTable("agent_action_grants");
        builder.HasKey(x => new { x.OrganizationId, x.AgentId, x.ActionDefinitionId });
        builder.TenantId();
        builder.Property(x => x.ApplicationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AgentId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActionDefinitionId).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.ActionDefinitionId });
        builder.HasIndex(x => new { x.OrganizationId, x.AgentId });
    }
}

internal sealed class ActionMcpBindingConfiguration : IEntityTypeConfiguration<ActionMcpBinding>
{
    public void Configure(EntityTypeBuilder<ActionMcpBinding> builder)
    {
        builder.ToTable("action_mcp_bindings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.ActionDefinitionId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.McpServer).HasMaxLength(64).IsRequired();
        builder.Property(x => x.McpTool).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResourceIdPath).HasMaxLength(256).IsRequired();
        builder.Property(x => x.AttributePathsJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.McpServer, x.McpTool }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.ActionDefinitionId });
    }
}

internal sealed class PolicyConfiguration : IEntityTypeConfiguration<PolicyDefinition>
{
    public void Configure(EntityTypeBuilder<PolicyDefinition> builder)
    {
        builder.ToTable("policies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.ApplicationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.ApplicationId, x.Slug }).IsUnique();
    }
}

internal sealed class AgentPolicyGrantConfiguration : IEntityTypeConfiguration<AgentPolicyGrant>
{
    public void Configure(EntityTypeBuilder<AgentPolicyGrant> builder)
    {
        builder.ToTable("agent_policy_grants");
        builder.HasKey(x => new { x.OrganizationId, x.AgentId, x.PolicyId });
        builder.TenantId();
        builder.Property(x => x.ApplicationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AgentId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PolicyId).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.PolicyId });
        builder.HasIndex(x => new { x.OrganizationId, x.AgentId });
    }
}

internal sealed class PolicyVersionConfiguration : IEntityTypeConfiguration<PolicyVersion>
{
    public void Configure(EntityTypeBuilder<PolicyVersion> builder)
    {
        builder.ToTable("policy_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.Yaml).IsRequired();
        builder.HasIndex(x => new { x.PolicyId, x.Version }).IsUnique();
    }
}

internal sealed class AuthorizationDecisionConfiguration : IEntityTypeConfiguration<AuthorizationDecision>
{
    public void Configure(EntityTypeBuilder<AuthorizationDecision> builder)
    {
        builder.ToTable("authorization_decisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.ApplicationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ResourceAttributesJson).HasColumnType("jsonb");
        builder.Property(x => x.ContextJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.OrganizationId, x.IdempotencyKey });
        builder.HasIndex(x => new { x.OrganizationId, x.ApplicationId });
        builder.HasIndex(x => x.RequestDigest);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(x => new { x.OrganizationId, x.ApplicationId, x.Key });
        builder.Property(x => x.ApplicationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Key).HasMaxLength(128);
        builder.Property(x => x.ResponseJson).HasColumnType("jsonb");
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("approval_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsConcurrencyToken();
        builder.Property(x => x.CanonicalFactsJson).HasColumnType("jsonb");
        builder.Property(x => x.ApproverRolesJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.Status);
    }
}

internal sealed class ApprovalEventConfiguration : IEntityTypeConfiguration<ApprovalEvent>
{
    public void Configure(EntityTypeBuilder<ApprovalEvent> builder)
    {
        builder.ToTable("approval_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
    }
}

internal sealed class PermitConfiguration : IEntityTypeConfiguration<Permit>
{
    public void Configure(EntityTypeBuilder<Permit> builder)
    {
        builder.ToTable("permits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.Token).IsRequired();
        builder.HasIndex(x => x.Jti).IsUnique();
        builder.HasIndex(x => x.DecisionId);
    }
}

internal sealed class ExecutionEvidenceConfiguration : IEntityTypeConfiguration<ExecutionEvidence>
{
    public void Configure(EntityTypeBuilder<ExecutionEvidence> builder)
    {
        builder.ToTable("execution_evidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.DecisionId);
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.ApplicationId).HasMaxLength(64);
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb");
        builder.Property(x => x.EventHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PreviousEventHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        builder.HasIndex(x => new { x.OrganizationId, x.ApplicationId, x.CreatedAt });
        builder.HasIndex(x => new { x.OrganizationId, x.EventHash }).IsUnique();
    }
}

internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.ProcessedAt);
    }
}

internal sealed class TenantAuditHeadConfiguration : IEntityTypeConfiguration<TenantAuditHead>
{
    public void Configure(EntityTypeBuilder<TenantAuditHead> builder)
    {
        builder.ToTable("tenant_audit_heads");
        builder.HasKey(x => x.OrganizationId);
        builder.Property(x => x.LastEventHash).HasMaxLength(64).IsRequired();
    }
}

internal sealed class SandboxInvoiceConfiguration : IEntityTypeConfiguration<SandboxInvoice>
{
    public void Configure(EntityTypeBuilder<SandboxInvoice> builder)
    {
        builder.ToTable("sandbox_invoices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.TenantId();
        builder.Property(x => x.DisplayId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.DisplayId }).IsUnique();
        builder.Ignore(x => x.IsCancelled);
    }
}
