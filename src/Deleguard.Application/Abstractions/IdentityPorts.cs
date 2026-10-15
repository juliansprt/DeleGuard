using System.Text.Json;
using Deleguard.Domain.Identity;

namespace Deleguard.Application.Abstractions;

public sealed record ConsoleUserPrincipal(
    string OrganizationId,
    string UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool IsBootstrap)
{
    public bool IsPlatformAdmin =>
        Roles.Contains(AccessControlIds.PlatformAdminRole, StringComparer.Ordinal);

    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.Ordinal) ||
        Roles.Contains(AccessControlIds.OrgAdminRole, StringComparer.Ordinal);

    public ConsoleUserPrincipal WithPlatformAdmin() =>
        IsPlatformAdmin
            ? this
            : this with { Roles = Roles.Append(AccessControlIds.PlatformAdminRole).ToList() };
}

public sealed record McpCallerPrincipal(
    string OrganizationId,
    string UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    AgentPrincipal Agent,
    string CredentialId);

public sealed record UserListItem(
    string Id,
    string Email,
    string DisplayName,
    bool Enabled,
    IReadOnlyList<string> Roles);

public sealed record RoleListItem(
    string Id,
    string Slug,
    string DisplayName,
    IReadOnlyList<string> Permissions);

public sealed record CreatedUserSecret(string UserId, string Email, string? TemporaryPassword);

public sealed record FederatedIdentity(
    string Issuer,
    string Subject,
    string Email,
    string DisplayName,
    bool EmailVerified,
    string? Company = null);

public sealed record McpSettingsView(
    bool Enabled,
    string AgentId,
    string AgentExternalId,
    IReadOnlyList<string> EnabledTools,
    IReadOnlyList<string> AvailableTools);

public sealed record McpCredentialListItem(
    string Id,
    string UserId,
    string UserEmail,
    string KeyPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);

public sealed record IssuedMcpSecret(string CredentialId, string UserId, string Token, string KeyPrefix);

public interface IUserDirectory
{
    Task<ConsoleUserPrincipal?> AuthenticatePasswordAsync(string organizationId, string email, string password, CancellationToken cancellationToken);
    Task<ConsoleUserPrincipal?> GetPrincipalAsync(string organizationId, string userId, CancellationToken cancellationToken);
    Task<ConsoleUserPrincipal?> GetBootstrapPrincipalAsync(string organizationId, CancellationToken cancellationToken);
    Task<UserListItem?> GetAsync(string organizationId, string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserListItem>> ListAsync(string organizationId, CancellationToken cancellationToken);
    Task<CreatedUserSecret> CreateAsync(string organizationId, string email, string displayName, IReadOnlyList<string> roleSlugs, string? password, CancellationToken cancellationToken);
    Task DisableAsync(string organizationId, string userId, CancellationToken cancellationToken);
    Task AssignRolesAsync(string organizationId, string userId, IReadOnlyList<string> roleSlugs, CancellationToken cancellationToken);
}

public interface IHumanIdentityDirectory
{
    Task<ConsoleUserPrincipal?> FindByExternalSubjectAsync(string issuer, string subject, CancellationToken cancellationToken);
    Task<ConsoleUserPrincipal?> GetPrincipalByUserIdAsync(string userId, CancellationToken cancellationToken);
    Task<ConsoleUserPrincipal?> LinkVerifiedEmailAsync(FederatedIdentity identity, CancellationToken cancellationToken);
}

public interface ITenantBootstrapper
{
    Task<ConsoleUserPrincipal> ProvisionNewOrganizationAsync(string organizationName, FederatedIdentity identity, CancellationToken cancellationToken);
}

public interface IRoleDirectory
{
    Task<IReadOnlyList<RoleListItem>> ListAsync(string organizationId, CancellationToken cancellationToken);
    Task SetPermissionsAsync(string organizationId, string roleId, IReadOnlyList<string> permissionNames, CancellationToken cancellationToken);
}

public interface IActorDirectory
{
    Task<IReadOnlyList<string>> GetRolesAsync(string organizationId, string? actorId, CancellationToken cancellationToken);
}

public interface IMcpDirectory
{
    Task<McpCallerPrincipal?> AuthenticateAsync(string token, CancellationToken cancellationToken);
    Task<McpSettingsView> GetSettingsAsync(string organizationId, CancellationToken cancellationToken);
    Task<McpSettingsView> UpdateSettingsAsync(string organizationId, bool enabled, IReadOnlyList<string> enabledTools, CancellationToken cancellationToken);
    Task<IReadOnlyList<McpCredentialListItem>> ListCredentialsAsync(string organizationId, CancellationToken cancellationToken);
    Task<IssuedMcpSecret> IssueTokenAsync(string organizationId, string userId, DateTimeOffset? expiresAt, CancellationToken cancellationToken);
    Task RevokeTokenAsync(string organizationId, string credentialId, CancellationToken cancellationToken);
}

public sealed record AdminActionOutcome<T>(
    string Decision,
    string DecisionId,
    string? Reason,
    string? ApprovalId,
    string? Permit,
    T? Value)
{
    public bool Allowed => string.Equals(Decision, "ALLOW", StringComparison.Ordinal);
    public bool Pending => string.Equals(Decision, "REQUIRE_APPROVAL", StringComparison.Ordinal);
}

/// <summary>
/// Shared authorize-then-execute path for console mutations and Administration MCP tools.
/// </summary>
public interface IAdminActionGateway
{
    Task<AdminActionOutcome<T>> RunAsync<T>(
        ConsoleUserPrincipal caller,
        string action,
        string resourceType,
        string resourceId,
        Dictionary<string, JsonElement>? attributes,
        string permission,
        Func<CancellationToken, Task<T>> execute,
        CancellationToken cancellationToken);
}
