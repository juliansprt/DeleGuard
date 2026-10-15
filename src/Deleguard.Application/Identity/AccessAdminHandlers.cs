using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;

namespace Deleguard.Application.Identity;

public sealed class UserAdminHandler
{
    private readonly IUserDirectory _users;
    private readonly IUnitOfWork _uow;

    public UserAdminHandler(IUserDirectory users, IUnitOfWork uow)
    {
        _users = users;
        _uow = uow;
    }

    public Task<IReadOnlyList<UserListItem>> ListAsync(string organizationId, CancellationToken cancellationToken) =>
        _users.ListAsync(organizationId, cancellationToken);

    public async Task<UserListItem> GetAsync(string organizationId, string userId, CancellationToken cancellationToken) =>
        await _users.GetAsync(organizationId, userId, cancellationToken)
        ?? throw new NotFoundException("User not found.");

    public async Task<CreatedUserSecret> CreateAsync(
        string organizationId,
        string email,
        string displayName,
        IReadOnlyList<string> roleSlugs,
        string? password,
        CancellationToken cancellationToken)
    {
        var created = await _users.CreateAsync(organizationId, email, displayName, roleSlugs, password, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task DisableAsync(string organizationId, string userId, CancellationToken cancellationToken)
    {
        await _users.DisableAsync(organizationId, userId, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignRolesAsync(string organizationId, string userId, IReadOnlyList<string> roleSlugs, CancellationToken cancellationToken)
    {
        await _users.AssignRolesAsync(organizationId, userId, roleSlugs, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}

public sealed class RoleAdminHandler
{
    private readonly IRoleDirectory _roles;
    private readonly IUnitOfWork _uow;

    public RoleAdminHandler(IRoleDirectory roles, IUnitOfWork uow)
    {
        _roles = roles;
        _uow = uow;
    }

    public Task<IReadOnlyList<RoleListItem>> ListAsync(string organizationId, CancellationToken cancellationToken) =>
        _roles.ListAsync(organizationId, cancellationToken);

    public IReadOnlyList<(string Name, string Description)> Catalog() => PermissionCatalog.All;

    public async Task SetPermissionsAsync(string organizationId, string roleId, IReadOnlyList<string> permissionNames, CancellationToken cancellationToken)
    {
        await _roles.SetPermissionsAsync(organizationId, roleId, permissionNames, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}

public sealed class McpAdminHandler
{
    private readonly IMcpDirectory _mcp;
    private readonly IUnitOfWork _uow;

    public McpAdminHandler(IMcpDirectory mcp, IUnitOfWork uow)
    {
        _mcp = mcp;
        _uow = uow;
    }

    public Task<McpSettingsView> GetSettingsAsync(string organizationId, CancellationToken cancellationToken) =>
        _mcp.GetSettingsAsync(organizationId, cancellationToken);

    public async Task<McpSettingsView> UpdateSettingsAsync(
        string organizationId,
        bool enabled,
        IReadOnlyList<string> enabledTools,
        CancellationToken cancellationToken)
    {
        var updated = await _mcp.UpdateSettingsAsync(organizationId, enabled, enabledTools, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public Task<IReadOnlyList<McpCredentialListItem>> ListCredentialsAsync(string organizationId, CancellationToken cancellationToken) =>
        _mcp.ListCredentialsAsync(organizationId, cancellationToken);

    public async Task<IssuedMcpSecret> IssueTokenAsync(string organizationId, string userId, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        var issued = await _mcp.IssueTokenAsync(organizationId, userId, expiresAt, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return issued;
    }

    public async Task RevokeTokenAsync(string organizationId, string credentialId, CancellationToken cancellationToken)
    {
        await _mcp.RevokeTokenAsync(organizationId, credentialId, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
