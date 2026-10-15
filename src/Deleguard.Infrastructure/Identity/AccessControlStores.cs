using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Persistence;
using Deleguard.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Deleguard.Infrastructure.Identity;

public sealed class UserDirectory : IUserDirectory
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly DeleguardDbContext _db;
    private readonly DemoAuthOptions _options;

    public UserDirectory(DeleguardDbContext db, IOptions<DemoAuthOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<ConsoleUserPrincipal?> AuthenticatePasswordAsync(string organizationId, string email, string password, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Email == normalized && x.Enabled, cancellationToken);
        if (user is null || !PasswordHasher.Verify(password, _options.ApiKeyPepper, user.PasswordHash))
        {
            return null;
        }

        return await GetPrincipalAsync(organizationId, user.Id, cancellationToken);
    }

    public async Task<ConsoleUserPrincipal?> GetPrincipalAsync(string organizationId, string userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == userId && x.Enabled, cancellationToken);
        if (user is null)
        {
            return null;
        }

        return await ToPrincipalAsync(user, isBootstrap: false, cancellationToken);
    }

    public async Task<ConsoleUserPrincipal?> GetBootstrapPrincipalAsync(string organizationId, CancellationToken cancellationToken)
    {
        var adminRole = await _db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Slug == AccessControlIds.OrgAdminRole, cancellationToken);
        if (adminRole is null)
        {
            return null;
        }

        var link = await _db.UserRoles.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.RoleId == adminRole.Id)
            .Join(_db.Users.AsNoTracking().Where(u => u.Enabled), ur => ur.UserId, u => u.Id, (_, user) => user)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (link is null)
        {
            return null;
        }

        return await ToPrincipalAsync(link, isBootstrap: true, cancellationToken);
    }

    public async Task<UserListItem?> GetAsync(string organizationId, string userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = await LoadRoleSlugsAsync(organizationId, [user.Id], cancellationToken);
        return new UserListItem(user.Id, user.Email, user.DisplayName, user.Enabled, roles.GetValueOrDefault(user.Id) ?? []);
    }

    public async Task<IReadOnlyList<UserListItem>> ListAsync(string organizationId, CancellationToken cancellationToken)
    {
        var users = await _db.Users.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);
        var roles = await LoadRoleSlugsAsync(organizationId, users.Select(x => x.Id).ToList(), cancellationToken);
        return users.Select(user => new UserListItem(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Enabled,
            roles.GetValueOrDefault(user.Id) ?? [])).ToList();
    }

    public async Task<CreatedUserSecret> CreateAsync(
        string organizationId,
        string email,
        string displayName,
        IReadOnlyList<string> roleSlugs,
        string? password,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        if (!EmailPattern.IsMatch(normalized))
        {
            throw new RequestValidationException("email is invalid.");
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 200)
        {
            throw new RequestValidationException("display_name is required.");
        }

        if (await _db.Users.AnyAsync(x => x.OrganizationId == organizationId && x.Email == normalized, cancellationToken))
        {
            throw new ConflictException("A user with that email already exists.");
        }

        string? secret = null;
        string? hash = null;
        if (!string.IsNullOrWhiteSpace(password))
        {
            secret = password.Trim();
            if (secret.Length < 10)
            {
                throw new RequestValidationException("password must be at least 10 characters.");
            }

            hash = PasswordHasher.Hash(secret, _options.ApiKeyPepper);
        }

        var user = new User
        {
            Id = IdGenerator.New("usr"),
            OrganizationId = organizationId,
            Email = normalized,
            DisplayName = displayName.Trim(),
            PasswordHash = hash,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _db.Users.AddAsync(user, cancellationToken);
        await ReplaceRolesAsync(organizationId, user.Id, roleSlugs, cancellationToken);
        return new CreatedUserSecret(user.Id, user.Email, secret);
    }

    public async Task DisableAsync(string organizationId, string userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("User not found.");
        user.Enabled = false;
        var tokens = await _db.McpCredentials
            .Where(x => x.OrganizationId == organizationId && x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
    }

    public async Task AssignRolesAsync(string organizationId, string userId, IReadOnlyList<string> roleSlugs, CancellationToken cancellationToken)
    {
        if (!await _db.Users.AnyAsync(x => x.OrganizationId == organizationId && x.Id == userId, cancellationToken))
        {
            throw new NotFoundException("User not found.");
        }

        await ReplaceRolesAsync(organizationId, userId, roleSlugs, cancellationToken);
    }

    private async Task ReplaceRolesAsync(string organizationId, string userId, IReadOnlyList<string> roleSlugs, CancellationToken cancellationToken)
    {
        var slugs = roleSlugs.Where(slug => !string.IsNullOrWhiteSpace(slug)).Select(slug => slug.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var roles = await _db.Roles.Where(x => x.OrganizationId == organizationId && slugs.Contains(x.Slug)).ToListAsync(cancellationToken);
        if (roles.Count != slugs.Count)
        {
            throw new RequestValidationException("One or more roles are unknown.");
        }

        var existing = await _db.UserRoles.Where(x => x.OrganizationId == organizationId && x.UserId == userId).ToListAsync(cancellationToken);
        _db.UserRoles.RemoveRange(existing);
        foreach (var role in roles)
        {
            await _db.UserRoles.AddAsync(new UserRole
            {
                OrganizationId = organizationId,
                UserId = userId,
                RoleId = role.Id
            }, cancellationToken);
        }
    }

    private async Task<ConsoleUserPrincipal> ToPrincipalAsync(User user, bool isBootstrap, CancellationToken cancellationToken)
    {
        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && x.UserId == user.Id)
            .Select(x => x.RoleId)
            .ToListAsync(cancellationToken);
        var roles = await _db.Roles.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && roleIds.Contains(x.Id))
            .OrderBy(x => x.Slug)
            .Select(x => x.Slug)
            .ToListAsync(cancellationToken);
        var permissionIds = await _db.RolePermissions.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && roleIds.Contains(x.RoleId))
            .Select(x => x.PermissionId)
            .ToListAsync(cancellationToken);
        var permissions = await _db.Permissions.AsNoTracking()
            .Where(x => x.OrganizationId == user.OrganizationId && permissionIds.Contains(x.Id))
            .Select(x => x.Name)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
        return new ConsoleUserPrincipal(user.OrganizationId, user.Id, user.Email, user.DisplayName, roles, permissions, isBootstrap);
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> LoadRoleSlugsAsync(
        string organizationId,
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var links = await _db.UserRoles.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && userIds.Contains(x.UserId))
            .ToListAsync(cancellationToken);
        var roleIds = links.Select(x => x.RoleId).Distinct().ToList();
        var roles = await _db.Roles.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && roleIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Slug, cancellationToken);
        return links
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => roles.GetValueOrDefault(x.RoleId) ?? x.RoleId).OrderBy(x => x).ToList());
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

public sealed class RoleDirectory : IRoleDirectory
{
    private readonly DeleguardDbContext _db;

    public RoleDirectory(DeleguardDbContext db) => _db = db;

    public async Task<IReadOnlyList<RoleListItem>> ListAsync(string organizationId, CancellationToken cancellationToken)
    {
        var roles = await _db.Roles.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Slug)
            .ToListAsync(cancellationToken);
        var links = await _db.RolePermissions.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        var permissions = await _db.Permissions.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        return roles.Select(role => new RoleListItem(
            role.Id,
            role.Slug,
            role.DisplayName,
            links.Where(x => x.RoleId == role.Id)
                .Select(x => permissions.GetValueOrDefault(x.PermissionId) ?? x.PermissionId)
                .OrderBy(x => x)
                .ToList())).ToList();
    }

    public async Task SetPermissionsAsync(string organizationId, string roleId, IReadOnlyList<string> permissionNames, CancellationToken cancellationToken)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == roleId, cancellationToken)
                   ?? throw new NotFoundException("Role not found.");
        var catalog = PermissionCatalog.All.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var names = permissionNames.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (names.Any(name => !catalog.Contains(name)))
        {
            throw new RequestValidationException("Permissions must come from the closed catalog.");
        }

        var permissions = await _db.Permissions
            .Where(x => x.OrganizationId == organizationId && names.Contains(x.Name))
            .ToListAsync(cancellationToken);
        if (permissions.Count != names.Count)
        {
            throw new RequestValidationException("One or more permissions are unknown in this organization.");
        }

        var existing = await _db.RolePermissions.Where(x => x.OrganizationId == organizationId && x.RoleId == role.Id).ToListAsync(cancellationToken);
        _db.RolePermissions.RemoveRange(existing);
        foreach (var permission in permissions)
        {
            await _db.RolePermissions.AddAsync(new RolePermission
            {
                OrganizationId = organizationId,
                RoleId = role.Id,
                PermissionId = permission.Id
            }, cancellationToken);
        }
    }
}

public sealed class ActorDirectory : IActorDirectory
{
    private readonly DeleguardDbContext _db;

    public ActorDirectory(DeleguardDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> GetRolesAsync(string organizationId, string? actorId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return [];
        }

        var enabled = await _db.Users.AsNoTracking()
            .AnyAsync(x => x.OrganizationId == organizationId && x.Id == actorId && x.Enabled, cancellationToken);
        if (!enabled)
        {
            return [];
        }

        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.UserId == actorId)
            .Select(x => x.RoleId)
            .ToListAsync(cancellationToken);
        if (roleIds.Count == 0)
        {
            return [];
        }

        return await _db.Roles.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && roleIds.Contains(x.Id))
            .OrderBy(x => x.Slug)
            .Select(x => x.Slug)
            .ToListAsync(cancellationToken);
    }
}

public sealed class McpDirectory : IMcpDirectory
{
    private readonly DeleguardDbContext _db;
    private readonly DemoAuthOptions _options;
    private readonly IUserDirectory _users;

    public McpDirectory(DeleguardDbContext db, IOptions<DemoAuthOptions> options, IUserDirectory users)
    {
        _db = db;
        _options = options.Value;
        _users = users;
    }

    public async Task<McpCallerPrincipal?> AuthenticateAsync(string token, CancellationToken cancellationToken)
    {
        if (!token.StartsWith("dgm_", StringComparison.Ordinal))
        {
            return null;
        }

        var hash = ApiKeyHasher.Hash(token, _options.ApiKeyPepper);
        var credential = await _db.McpCredentials.AsNoTracking()
            .FirstOrDefaultAsync(x => x.KeyHash == hash, cancellationToken);
        if (credential is null || !credential.IsActive(DateTimeOffset.UtcNow))
        {
            return null;
        }

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == credential.UserId && x.OrganizationId == credential.OrganizationId && x.Enabled, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var settings = await _db.McpSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == credential.OrganizationId, cancellationToken);
        if (settings is null || !settings.Enabled)
        {
            return null;
        }

        var agent = await _db.Agents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == settings.AgentId && x.OrganizationId == credential.OrganizationId && x.Enabled, cancellationToken);
        if (agent is null)
        {
            return null;
        }

        var environment = await _db.Environments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == agent.EnvironmentId, cancellationToken);
        if (environment is null)
        {
            return null;
        }

        var principal = await _users.GetPrincipalAsync(user.OrganizationId, user.Id, cancellationToken);
        if (principal is null)
        {
            return null;
        }

        return new McpCallerPrincipal(
            principal.OrganizationId,
            principal.UserId,
            principal.Email,
            principal.DisplayName,
            principal.Roles,
            principal.Permissions,
            new AgentPrincipal(
                agent.OrganizationId,
                agent.ApplicationId,
                environment.Id,
                environment.Name,
                agent.Id,
                agent.ExternalId),
            credential.Id);
    }

    public async Task<McpSettingsView> GetSettingsAsync(string organizationId, CancellationToken cancellationToken)
    {
        var settings = await RequireSettingsAsync(organizationId, cancellationToken);
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == settings.AgentId, cancellationToken);
        return new McpSettingsView(
            settings.Enabled,
            settings.AgentId,
            agent?.ExternalId ?? AccessControlIds.AdminMcpExternalId,
            ReadTools(settings.EnabledToolsJson),
            AdminMcpCatalog.ToolNames);
    }

    public async Task<McpSettingsView> UpdateSettingsAsync(
        string organizationId,
        bool enabled,
        IReadOnlyList<string> enabledTools,
        CancellationToken cancellationToken)
    {
        var settings = await RequireSettingsAsync(organizationId, cancellationToken);
        var allowed = AdminMcpCatalog.ToolNames.ToHashSet(StringComparer.Ordinal);
        var tools = enabledTools.Where(name => allowed.Contains(name)).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToList();
        settings.Enabled = enabled;
        settings.EnabledToolsJson = JsonSerializer.Serialize(tools);
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        return new McpSettingsView(
            settings.Enabled,
            settings.AgentId,
            AccessControlIds.AdminMcpExternalId,
            tools,
            AdminMcpCatalog.ToolNames);
    }

    public async Task<IReadOnlyList<McpCredentialListItem>> ListCredentialsAsync(string organizationId, CancellationToken cancellationToken)
    {
        var credentials = await _db.McpCredentials.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var users = await _db.Users.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .ToDictionaryAsync(x => x.Id, x => x.Email, cancellationToken);
        return credentials.Select(item => new McpCredentialListItem(
            item.Id,
            item.UserId,
            users.GetValueOrDefault(item.UserId) ?? item.UserId,
            item.KeyPrefix,
            item.CreatedAt,
            item.ExpiresAt,
            item.RevokedAt)).ToList();
    }

    public async Task<IssuedMcpSecret> IssueTokenAsync(string organizationId, string userId, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == userId && x.Enabled, cancellationToken)
                   ?? throw new NotFoundException("User not found.");
        var token = NewMcpToken();
        var credential = new McpCredential
        {
            Id = IdGenerator.New("mcp"),
            OrganizationId = organizationId,
            UserId = user.Id,
            KeyPrefix = ApiKeyHasher.Prefix(token),
            KeyHash = ApiKeyHasher.Hash(token, _options.ApiKeyPepper),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };
        await _db.McpCredentials.AddAsync(credential, cancellationToken);
        return new IssuedMcpSecret(credential.Id, user.Id, token, credential.KeyPrefix);
    }

    public async Task RevokeTokenAsync(string organizationId, string credentialId, CancellationToken cancellationToken)
    {
        var credential = await _db.McpCredentials
                             .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == credentialId, cancellationToken)
                         ?? throw new NotFoundException("MCP credential not found.");
        credential.RevokedAt = DateTimeOffset.UtcNow;
    }

    private async Task<OrganizationMcpSettings> RequireSettingsAsync(string organizationId, CancellationToken cancellationToken) =>
        await _db.McpSettings.FirstOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken)
        ?? throw new FailClosedException("Administration MCP is not provisioned for this organization.");

    private static IReadOnlyList<string> ReadTools(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NewMcpToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return "dgm_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
