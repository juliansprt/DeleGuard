using System.Text.Json;
using Deleguard.Domain.Approvals;
using Deleguard.Domain.Audit;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Playground;
using Deleguard.Domain.Policies;
using Deleguard.Policy;

namespace Deleguard.Application.Abstractions;

public sealed record AgentPrincipal(
    string OrganizationId,
    string ApplicationId,
    string EnvironmentId,
    string EnvironmentName,
    string AgentId,
    string AgentExternalId);

public sealed record ApplicationListItem(
    string Id,
    string Slug,
    string Name,
    string Kind,
    DateTimeOffset CreatedAt,
    int AgentCount);

public sealed record EnvironmentInfo(string Id, string Name, string ApplicationId);

public sealed record EnvironmentListItem(string Id, string Name, string ApplicationId, DateTimeOffset CreatedAt, int AgentCount);

public sealed record AgentListItem(
    string Id,
    string ExternalId,
    string DisplayName,
    string ApplicationId,
    string EnvironmentId,
    string EnvironmentName,
    bool Enabled,
    string? ActiveKeyPrefix);

public sealed record CreatedAgentSecret(string AgentId, string ExternalId, string ApiKey, string KeyPrefix);

public sealed record PolicySummary(
    string Id,
    string Slug,
    string ApplicationId,
    string? PublishedVersionId,
    int VersionCount,
    int? PublishedVersionNumber,
    string? Action,
    string? ResourceType);

public interface ITenantContext
{
    string OrganizationId { get; set; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IOrganizationDirectory
{
    Task<Organization?> GetAsync(string organizationId, CancellationToken cancellationToken);
    Task UpdateNameAsync(string organizationId, string name, CancellationToken cancellationToken);
}

public interface IApplicationDirectory
{
    Task<IReadOnlyList<ApplicationListItem>> ListAsync(string organizationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApplicationListItem>> ListProductAsync(string organizationId, CancellationToken cancellationToken);
    Task<ApplicationListItem?> GetAsync(string organizationId, string applicationId, CancellationToken cancellationToken);
    Task<ApplicationListItem> GetAdministrationAsync(string organizationId, CancellationToken cancellationToken);
    Task<ApplicationListItem> GetDefaultProductAsync(string organizationId, CancellationToken cancellationToken);
    Task<ApplicationListItem> ResolveProductAsync(string organizationId, string? idOrSlug, CancellationToken cancellationToken);
    Task<ApplicationListItem> CreateProductAsync(string organizationId, string name, string slug, CancellationToken cancellationToken);
    Task<ApplicationListItem> UpdateAsync(string organizationId, string applicationId, string name, string slug, CancellationToken cancellationToken);
    Task EnsureCanDeleteAsync(string organizationId, string applicationId, CancellationToken cancellationToken);
    Task DeleteAsync(string organizationId, string applicationId, CancellationToken cancellationToken);
}

public interface IAgentDirectory
{
    Task<T> ExecuteLockedAsync<T>(string organizationId, string agentId, Func<Task<T>> action, CancellationToken cancellationToken);
    Task<AgentPrincipal?> AuthenticateAsync(string apiKey, CancellationToken cancellationToken);
    Task<Agent?> GetAsync(string organizationId, string agentId, CancellationToken cancellationToken);
    Task<AgentPrincipal?> GetPrincipalAsync(string organizationId, string agentId, CancellationToken cancellationToken);
    Task<AgentPrincipal?> GetPrincipalByExternalIdAsync(string organizationId, string externalId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentListItem>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EnvironmentInfo>> ListEnvironmentsAsync(string organizationId, string applicationId, CancellationToken cancellationToken);
    Task<CreatedAgentSecret> CreateAsync(string organizationId, string applicationId, string externalId, string displayName, string environmentName, CancellationToken cancellationToken);
    Task<CreatedAgentSecret> RotateKeyAsync(string organizationId, string agentId, CancellationToken cancellationToken);
    Task SetEnabledAsync(string organizationId, string agentId, bool enabled, CancellationToken cancellationToken);
}

public interface IEnvironmentDirectory
{
    Task<IReadOnlyList<EnvironmentListItem>> ListAsync(string organizationId, string applicationId, CancellationToken cancellationToken);
    Task<EnvironmentListItem?> GetAsync(string organizationId, string environmentId, CancellationToken cancellationToken);
    Task<EnvironmentListItem> CreateAsync(string organizationId, string applicationId, string name, CancellationToken cancellationToken);
    Task<EnvironmentListItem> UpdateAsync(string organizationId, string environmentId, string name, CancellationToken cancellationToken);
    Task EnsureCanDeleteAsync(string organizationId, string environmentId, CancellationToken cancellationToken);
    Task DeleteAsync(string organizationId, string environmentId, CancellationToken cancellationToken);
}

public interface IActionRegistry
{
    Task<ActionDefinition?> GetByNameAsync(string organizationId, string applicationId, string name, CancellationToken cancellationToken);
    Task<ActionDefinition?> GetAsync(string organizationId, string actionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActionDefinition>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken);
    Task AddAsync(ActionDefinition action, CancellationToken cancellationToken);
    Task UpdateAsync(string organizationId, string actionId, string resourceType, RiskLevel risk, CancellationToken cancellationToken);
    Task DeleteAsync(string organizationId, string actionId, CancellationToken cancellationToken);
}

public interface IActionGrantStore
{
    Task<bool> HasAsync(string organizationId, string agentId, string actionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListAgentIdsForActionAsync(string organizationId, string actionId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ListAgentIdsByActionIdsAsync(
        string organizationId,
        IReadOnlyList<string> actionIds,
        CancellationToken cancellationToken);
    Task ReplaceAsync(
        string organizationId,
        string applicationId,
        string actionId,
        IReadOnlyList<string> agentIds,
        CancellationToken cancellationToken);
    Task DeleteByActionIdAsync(string organizationId, string actionId, CancellationToken cancellationToken);
}

public interface IPolicyGrantStore
{
    Task<bool> HasAsync(string organizationId, string agentId, string policyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListAgentIdsForPolicyAsync(string organizationId, string policyId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ListAgentIdsByPolicyIdsAsync(
        string organizationId,
        IReadOnlyList<string> policyIds,
        CancellationToken cancellationToken);
    Task ReplaceAsync(
        string organizationId,
        string applicationId,
        string policyId,
        IReadOnlyList<string> agentIds,
        CancellationToken cancellationToken);
    Task DeleteByPolicyIdAsync(string organizationId, string policyId, CancellationToken cancellationToken);
}

public sealed record GuardMappingRecord(
    string BindingId,
    string ActionId,
    string Action,
    string ResourceType,
    RiskLevel Risk,
    string McpServer,
    string McpTool,
    string ResourceIdPath,
    string AttributePathsJson);

public interface IGuardMappingStore
{
    Task<IReadOnlyList<GuardMappingRecord>> ListAsync(string organizationId, CancellationToken cancellationToken);
    Task<ActionMcpBinding?> GetAsync(string organizationId, string bindingId, CancellationToken cancellationToken);
    Task<ActionMcpBinding?> FindByToolAsync(string organizationId, string mcpServer, string mcpTool, CancellationToken cancellationToken);
    Task AddAsync(ActionMcpBinding binding, CancellationToken cancellationToken);
    Task DeleteAsync(ActionMcpBinding binding, CancellationToken cancellationToken);
    Task DeleteByActionIdAsync(string organizationId, string actionId, CancellationToken cancellationToken);
}

public interface IPolicyRepository
{
    /// <summary>Runs a policy mutation under a shared, tenant-scoped database lock and transaction.</summary>
    Task<T> ExecuteLockedAsync<T>(string organizationId, string policyId, Func<Task<T>> action, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompiledPolicy>> GetPublishedAsync(
        string organizationId,
        string applicationId,
        string action,
        string resourceType,
        string agentId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PolicySummary>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken);
    Task<PolicyDefinition?> GetAsync(string organizationId, string policyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PolicyVersion>> ListVersionsAsync(string organizationId, string policyId, CancellationToken cancellationToken);
    Task<PolicyVersion?> GetVersionAsync(string organizationId, string policyId, string versionId, CancellationToken cancellationToken);
    Task<PolicyVersion?> GetLatestVersionAsync(string organizationId, string policyId, CancellationToken cancellationToken);
    Task AddDefinitionAsync(PolicyDefinition policy, CancellationToken cancellationToken);
    Task AddVersionAsync(PolicyVersion version, CancellationToken cancellationToken);
    Task<IReadOnlyList<PolicyVersion>> GetVersionsForUpdateAsync(string organizationId, string policyId, CancellationToken cancellationToken);
    Task DeleteAsync(string organizationId, string policyId, CancellationToken cancellationToken);
}

public interface ISandboxInvoiceStore
{
    Task<IReadOnlyList<SandboxInvoice>> ListAsync(string organizationId, CancellationToken cancellationToken);
    Task<SandboxInvoice?> GetAsync(string organizationId, string invoiceId, CancellationToken cancellationToken);
    Task<SandboxInvoice?> GetByDisplayIdAsync(string organizationId, string displayId, CancellationToken cancellationToken);
    Task AddAsync(SandboxInvoice invoice, CancellationToken cancellationToken);
}

public interface IDecisionStore
{
    Task<AuthorizationDecision?> GetAsync(string organizationId, string decisionId, CancellationToken cancellationToken);
    Task AddAsync(AuthorizationDecision decision, CancellationToken cancellationToken);
}

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> GetAsync(string organizationId, string applicationId, string key, CancellationToken cancellationToken);
    Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken);
}

public interface IApprovalStore
{
    Task AddAsync(ApprovalRequest request, CancellationToken cancellationToken);
    Task<ApprovalRequest?> GetAsync(string organizationId, string approvalId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(string organizationId, string? applicationId, CancellationToken cancellationToken);
    Task AddEventAsync(ApprovalEvent approvalEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApprovalRequest>> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IPermitStore
{
    Task AddAsync(Permit permit, CancellationToken cancellationToken);
    Task<Permit?> GetAsync(string organizationId, string permitId, CancellationToken cancellationToken);
    Task<Permit?> GetByDecisionAsync(string organizationId, string decisionId, CancellationToken cancellationToken);
}

public interface IPermitSigner
{
    string Sign(Permit permit, AgentPrincipal agent, string? actorId);
}

public interface IEvidenceStore
{
    Task AddAsync(ExecutionEvidence evidence, CancellationToken cancellationToken);
}

public interface IAuditLedger
{
    Task AppendAsync(string organizationId, string eventType, string? decisionId, JsonElement payload, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEvent>> ListAsync(
        string organizationId,
        int take,
        CancellationToken cancellationToken,
        string? eventType = null,
        string? q = null,
        string? applicationId = null);
}

public interface IOutbox
{
    Task EnqueueAsync(string organizationId, string eventType, JsonElement payload, CancellationToken cancellationToken);
    Task<IReadOnlyList<OutboxEvent>> DequeueUnprocessedAsync(int take, CancellationToken cancellationToken);
    Task MarkProcessedAsync(string id, DateTimeOffset processedAt, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken);
}
