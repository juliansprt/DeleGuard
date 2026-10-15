using Deleguard.Domain.Approvals;
using Deleguard.Domain.Audit;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Evidence;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Notifications;
using Deleguard.Domain.Playground;
using Deleguard.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Deleguard.Domain.Common;
using System.Text.Json;

namespace Deleguard.Infrastructure.Persistence;

public sealed class DeleguardDbContext : DbContext
{
    public DeleguardDbContext(DbContextOptions<DeleguardDbContext> options) : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationApplication> Applications => Set<OrganizationApplication>();
    public DbSet<OrganizationEnvironment> Environments => Set<OrganizationEnvironment>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentCredential> AgentCredentials => Set<AgentCredential>();
    public DbSet<ActionDefinition> ActionDefinitions => Set<ActionDefinition>();
    public DbSet<AgentActionGrant> AgentActionGrants => Set<AgentActionGrant>();
    public DbSet<ActionMcpBinding> ActionMcpBindings => Set<ActionMcpBinding>();
    public DbSet<PolicyDefinition> Policies => Set<PolicyDefinition>();
    public DbSet<AgentPolicyGrant> AgentPolicyGrants => Set<AgentPolicyGrant>();
    public DbSet<PolicyVersion> PolicyVersions => Set<PolicyVersion>();
    public DbSet<AuthorizationDecision> AuthorizationDecisions => Set<AuthorizationDecision>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalEvent> ApprovalEvents => Set<ApprovalEvent>();
    public DbSet<Permit> Permits => Set<Permit>();
    public DbSet<OperationExecution> OperationExecutions => Set<OperationExecution>();
    public DbSet<ExecutionEvidence> ExecutionEvidence => Set<ExecutionEvidence>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<TenantAuditHead> TenantAuditHeads => Set<TenantAuditHead>();
    public DbSet<SandboxInvoice> SandboxInvoices => Set<SandboxInvoice>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookEndpointSecret> WebhookEndpointSecrets => Set<WebhookEndpointSecret>();
    public DbSet<WebhookMessage> WebhookMessages => Set<WebhookMessage>();
    public DbSet<WebhookDeliveryAttempt> WebhookDeliveryAttempts => Set<WebhookDeliveryAttempt>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<OrganizationMcpSettings> McpSettings => Set<OrganizationMcpSettings>();
    public DbSet<McpCredential> McpCredentials => Set<McpCredential>();
    public DbSet<OrganizationGatewaySettings> GatewaySettings => Set<OrganizationGatewaySettings>();
    public DbSet<GatewayModelRoute> GatewayModelRoutes => Set<GatewayModelRoute>();
    public DbSet<GatewaySpendReservation> GatewaySpendReservations => Set<GatewaySpendReservation>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var changedOrganizations = ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(x => x.Metadata.FindProperty("OrganizationId") is null ? null : x.Property("OrganizationId").CurrentValue as string)
            .Where(x => x is not null).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var events = ChangeTracker.Entries<AuditEvent>().Where(x => x.State == EntityState.Added).ToList();
        if (changedOrganizations.Count == 0) return await base.SaveChangesAsync(cancellationToken);
        var ownsTransaction = Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken) : null;
        foreach (var org in changedOrganizations)
            await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"deleguard-state:" + org}, 0))", cancellationToken);
        foreach (var group in events.GroupBy(x => x.Entity.OrganizationId).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"deleguard-audit:" + group.Key}, 0))", cancellationToken);
            var head = await TenantAuditHeads.FindAsync([group.Key], cancellationToken);
            if (head is not null && Entry(head).State != EntityState.Added) await Entry(head).ReloadAsync(cancellationToken);
            var previous = head?.LastEventHash ?? AuditHasher.Genesis;
            foreach (var entry in group)
            {
                var e = entry.Entity;
                var canonical = CanonicalJson.Serialize(JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<JsonElement>(e.PayloadJson)));
                var hash = AuditHasher.Compute(previous, $"{e.EventType}|{canonical}");
                entry.Property(x => x.PreviousEventHash).CurrentValue = previous;
                entry.Property(x => x.EventHash).CurrentValue = hash;
                previous = hash;
            }
            if (head is null) TenantAuditHeads.Add(new TenantAuditHead { OrganizationId = group.Key, LastEventHash = previous, UpdatedAt = DateTimeOffset.UtcNow });
            else { head.LastEventHash = previous; head.UpdatedAt = DateTimeOffset.UtcNow; }
        }
        var count = await base.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return count;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeleguardDbContext).Assembly);
    }
}
