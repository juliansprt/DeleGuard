using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Policies;
using Deleguard.Infrastructure.Persistence;
using Deleguard.Policy;

namespace Deleguard.Infrastructure.Identity;

public sealed class TenantBootstrapper : ITenantBootstrapper
{
    private readonly DeleguardDbContext _db;

    public TenantBootstrapper(DeleguardDbContext db)
    {
        _db = db;
    }

    public async Task<ConsoleUserPrincipal> ProvisionNewOrganizationAsync(
        string organizationName,
        FederatedIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!identity.EmailVerified)
        {
            throw new ForbiddenException("Verify your email before creating an organization.");
        }

        var now = DateTimeOffset.UtcNow;
        var organizationId = IdGenerator.New("org");
        var adminApplication = ApplicationSeed.Administration(organizationId, now);
        var productApplication = ApplicationSeed.DefaultProduct(organizationId, organizationName, now);
        var adminEnvironment = ApplicationSeed.Production(organizationId, adminApplication.Id, now);
        var productEnvironment = ApplicationSeed.Production(organizationId, productApplication.Id, now);
        var adminAgentId = IdGenerator.New("agt");

        await _db.Organizations.AddAsync(new Organization
        {
            Id = organizationId,
            Name = organizationName,
            CreatedAt = now
        }, cancellationToken);
        await _db.Applications.AddAsync(adminApplication, cancellationToken);
        await _db.Applications.AddAsync(productApplication, cancellationToken);
        await _db.Environments.AddAsync(adminEnvironment, cancellationToken);
        await _db.Environments.AddAsync(productEnvironment, cancellationToken);

        var permissions = new List<Permission>();
        foreach (var (name, description) in PermissionCatalog.All)
        {
            var permission = new Permission
            {
                Id = IdGenerator.New("prm"),
                OrganizationId = organizationId,
                Name = name,
                Description = description
            };
            permissions.Add(permission);
            await _db.Permissions.AddAsync(permission, cancellationToken);
        }

        var roles = new (string Slug, string Display)[]
        {
            (AccessControlIds.OrgAdminRole, "Organization admin"),
            (AccessControlIds.OperatorRole, "Operator"),
            (AccessControlIds.AuditorRole, "Auditor"),
            (AccessControlIds.BillingManagerRole, "Billing manager")
        };
        var roleIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (slug, display) in roles)
        {
            var role = new Role
            {
                Id = IdGenerator.New("rol"),
                OrganizationId = organizationId,
                Slug = slug,
                DisplayName = display,
                CreatedAt = now
            };
            roleIds[slug] = role.Id;
            await _db.Roles.AddAsync(role, cancellationToken);
        }

        foreach (var (slug, _) in roles)
        {
            var allowed = PermissionCatalog.ForRole(slug).ToHashSet(StringComparer.Ordinal);
            foreach (var permission in permissions.Where(item => allowed.Contains(item.Name)))
            {
                await _db.RolePermissions.AddAsync(new RolePermission
                {
                    OrganizationId = organizationId,
                    RoleId = roleIds[slug],
                    PermissionId = permission.Id
                }, cancellationToken);
            }
        }

        await _db.Agents.AddAsync(new Agent
        {
            Id = adminAgentId,
            OrganizationId = organizationId,
            ApplicationId = adminApplication.Id,
            EnvironmentId = adminEnvironment.Id,
            ExternalId = AccessControlIds.AdminMcpExternalId,
            DisplayName = "Administration MCP",
            Enabled = true,
            CreatedAt = now
        }, cancellationToken);
        await _db.McpSettings.AddAsync(new OrganizationMcpSettings
        {
            OrganizationId = organizationId,
            AgentId = adminAgentId,
            Enabled = false,
            EnabledToolsJson = System.Text.Json.JsonSerializer.Serialize(AdminMcpCatalog.ToolNames),
            UpdatedAt = now
        }, cancellationToken);
        await ProductGatewaySynchronizer.EnsureAsync(_db, organizationId, now, cancellationToken);

        var seededActions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in AdminMcpCatalog.Tools)
        {
            if (!seededActions.Add(spec.Action))
            {
                continue;
            }

            var actionId = IdGenerator.New("act");
            await _db.ActionDefinitions.AddAsync(new ActionDefinition
            {
                Id = actionId,
                OrganizationId = organizationId,
                ApplicationId = adminApplication.Id,
                Name = spec.Action,
                ResourceType = spec.ResourceType,
                Risk = spec.Risk,
                CreatedAt = now
            }, cancellationToken);
            await _db.AgentActionGrants.AddAsync(new AgentActionGrant
            {
                OrganizationId = organizationId,
                ApplicationId = adminApplication.Id,
                AgentId = adminAgentId,
                ActionDefinitionId = actionId,
                CreatedAt = now
            }, cancellationToken);

            var yaml = AdminPolicyYaml.For(spec);
            var compiled = PolicyYamlParser.Parse(spec.Action, yaml);
            var policyId = IdGenerator.New("pol");
            var versionId = IdGenerator.New("pver");
            await _db.Policies.AddAsync(new PolicyDefinition
            {
                Id = policyId,
                OrganizationId = organizationId,
                ApplicationId = adminApplication.Id,
                Slug = compiled.PolicyId,
                PublishedVersionId = versionId,
                CreatedAt = now
            }, cancellationToken);
            await _db.PolicyVersions.AddAsync(new PolicyVersion
            {
                Id = versionId,
                OrganizationId = organizationId,
                PolicyId = policyId,
                Version = compiled.Version,
                Yaml = yaml,
                Action = compiled.Action,
                ResourceType = compiled.ResourceType,
                Priority = compiled.Priority,
                Published = true,
                CreatedAt = now,
                PublishedAt = now
            }, cancellationToken);
            await _db.AgentPolicyGrants.AddAsync(new AgentPolicyGrant
            {
                OrganizationId = organizationId,
                ApplicationId = adminApplication.Id,
                AgentId = adminAgentId,
                PolicyId = policyId,
                CreatedAt = now
            }, cancellationToken);
        }

        var user = new User
        {
            Id = IdGenerator.New("usr"),
            OrganizationId = organizationId,
            Email = identity.Email.Trim().ToLowerInvariant(),
            DisplayName = string.IsNullOrWhiteSpace(identity.DisplayName) ? identity.Email.Trim() : identity.DisplayName.Trim(),
            PasswordHash = null,
            ExternalIssuer = HumanIdentityDirectory.NormalizeIssuer(identity.Issuer),
            ExternalSubject = identity.Subject,
            Enabled = true,
            CreatedAt = now
        };
        await _db.Users.AddAsync(user, cancellationToken);
        await _db.UserRoles.AddAsync(new UserRole
        {
            OrganizationId = organizationId,
            UserId = user.Id,
            RoleId = roleIds[AccessControlIds.OrgAdminRole]
        }, cancellationToken);

        return new ConsoleUserPrincipal(
            organizationId,
            user.Id,
            user.Email,
            user.DisplayName,
            [AccessControlIds.OrgAdminRole],
            PermissionCatalog.All.Select(item => item.Name).ToList(),
            false);
    }
}
