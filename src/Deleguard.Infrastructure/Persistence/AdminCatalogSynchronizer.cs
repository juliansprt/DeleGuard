using System.Text.Json;
using Deleguard.Application.Identity;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Policies;
using Deleguard.Policy;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Persistence;

internal static class AdminCatalogSynchronizer
{
    public static async Task EnsureAllAsync(DeleguardDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var organizationIds = await db.Organizations.Select(item => item.Id).ToListAsync(cancellationToken);
        foreach (var organizationId in organizationIds)
        {
            await EnsureAsync(db, organizationId, now, cancellationToken);
            await ProductGatewaySynchronizer.EnsureAsync(db, organizationId, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureAsync(
        DeleguardDbContext db,
        string organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsurePermissionsAsync(db, organizationId, cancellationToken);
        var adminApp = await RequireAdministrationAsync(db, organizationId, now, cancellationToken);
        var adminAgentId = await RequireAdminAgentAsync(db, organizationId, adminApp.Id, now, cancellationToken);

        foreach (var spec in AdminMcpCatalog.Tools)
        {
            var action = await db.ActionDefinitions.FirstOrDefaultAsync(
                             x => x.OrganizationId == organizationId && x.ApplicationId == adminApp.Id && x.Name == spec.Action,
                             cancellationToken)
                         ?? db.ActionDefinitions.Local.FirstOrDefault(x =>
                             x.OrganizationId == organizationId &&
                             x.ApplicationId == adminApp.Id &&
                             string.Equals(x.Name, spec.Action, StringComparison.Ordinal));
            if (action is null)
            {
                action = new ActionDefinition
                {
                    Id = IdGenerator.New("act"),
                    OrganizationId = organizationId,
                    ApplicationId = adminApp.Id,
                    Name = spec.Action,
                    ResourceType = spec.ResourceType,
                    Risk = spec.Risk,
                    CreatedAt = now
                };
                db.ActionDefinitions.Add(action);
            }

            if (!await db.AgentActionGrants.AnyAsync(
                    x => x.OrganizationId == organizationId && x.AgentId == adminAgentId && x.ActionDefinitionId == action.Id,
                    cancellationToken)
                && !db.AgentActionGrants.Local.Any(x =>
                    x.OrganizationId == organizationId && x.AgentId == adminAgentId && x.ActionDefinitionId == action.Id))
            {
                db.AgentActionGrants.Add(new AgentActionGrant
                {
                    OrganizationId = organizationId,
                    ApplicationId = adminApp.Id,
                    AgentId = adminAgentId,
                    ActionDefinitionId = action.Id,
                    CreatedAt = now
                });
            }

            var yaml = AdminPolicyYaml.For(spec);
            var compiled = PolicyYamlParser.Parse(spec.Action, yaml);
            var policy = await db.Policies.FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.ApplicationId == adminApp.Id && x.Slug == compiled.PolicyId,
                cancellationToken);
            if (policy is null)
            {
                var policyId = IdGenerator.New("pol");
                var versionId = IdGenerator.New("pver");
                policy = new PolicyDefinition
                {
                    Id = policyId,
                    OrganizationId = organizationId,
                    ApplicationId = adminApp.Id,
                    Slug = compiled.PolicyId,
                    PublishedVersionId = versionId,
                    CreatedAt = now
                };
                db.Policies.Add(policy);
                db.PolicyVersions.Add(new PolicyVersion
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
                });
            }
            else
            {
                var published = await db.PolicyVersions.AnyAsync(
                    x => x.OrganizationId == organizationId && x.PolicyId == policy.Id && x.Published && x.Action == spec.Action,
                    cancellationToken);
                if (!published)
                {
                    var draft = await db.PolicyVersions.FirstOrDefaultAsync(
                        x => x.OrganizationId == organizationId && x.PolicyId == policy.Id && x.Action == spec.Action,
                        cancellationToken);
                    if (draft is not null)
                    {
                        draft.Published = true;
                        draft.PublishedAt = now;
                        policy.PublishedVersionId = draft.Id;
                    }
                    else
                    {
                        var next = (await db.PolicyVersions
                            .Where(x => x.PolicyId == policy.Id)
                            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
                        var newVersionId = IdGenerator.New("pver");
                        db.PolicyVersions.Add(new PolicyVersion
                        {
                            Id = newVersionId,
                            OrganizationId = organizationId,
                            PolicyId = policy.Id,
                            Version = next,
                            Yaml = yaml,
                            Action = compiled.Action,
                            ResourceType = compiled.ResourceType,
                            Priority = compiled.Priority,
                            Published = true,
                            CreatedAt = now,
                            PublishedAt = now
                        });
                        policy.PublishedVersionId = newVersionId;
                    }
                }
            }

            if (!await db.AgentPolicyGrants.AnyAsync(
                    x => x.OrganizationId == organizationId && x.AgentId == adminAgentId && x.PolicyId == policy.Id,
                    cancellationToken)
                && !db.AgentPolicyGrants.Local.Any(x =>
                    x.OrganizationId == organizationId && x.AgentId == adminAgentId && x.PolicyId == policy.Id))
            {
                db.AgentPolicyGrants.Add(new AgentPolicyGrant
                {
                    OrganizationId = organizationId,
                    ApplicationId = adminApp.Id,
                    AgentId = adminAgentId,
                    PolicyId = policy.Id,
                    CreatedAt = now
                });
            }
        }

        var settings = await db.McpSettings.FirstOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken);
        if (settings is null)
        {
            return;
        }

        IReadOnlyList<string> current;
        try
        {
            current = JsonSerializer.Deserialize<List<string>>(settings.EnabledToolsJson) ?? [];
        }
        catch (JsonException)
        {
            current = [];
        }

        var inherited = AdminMcpCatalog.InheritEnabledTools(current);
        if (inherited.SequenceEqual(current, StringComparer.Ordinal))
        {
            return;
        }

        settings.EnabledToolsJson = JsonSerializer.Serialize(inherited);
        settings.UpdatedAt = now;
    }

    private static async Task EnsurePermissionsAsync(
        DeleguardDbContext db,
        string organizationId,
        CancellationToken cancellationToken)
    {
        foreach (var (name, description) in PermissionCatalog.All)
        {
            if (!await db.Permissions.AnyAsync(
                    x => x.OrganizationId == organizationId && x.Name == name,
                    cancellationToken))
            {
                db.Permissions.Add(new Permission
                {
                    Id = IdGenerator.New("prm"),
                    OrganizationId = organizationId,
                    Name = name,
                    Description = description
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var roles = await db.Roles
            .Where(x => x.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        foreach (var role in roles)
        {
            var allowed = PermissionCatalog.ForRole(role.Slug).ToHashSet(StringComparer.Ordinal);
            if (allowed.Count == 0)
            {
                continue;
            }

            var existingIds = (await db.RolePermissions
                    .Where(x => x.OrganizationId == organizationId && x.RoleId == role.Id)
                    .Select(x => x.PermissionId)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            var permissions = await db.Permissions
                .Where(x => x.OrganizationId == organizationId && allowed.Contains(x.Name))
                .ToListAsync(cancellationToken);
            foreach (var permission in permissions)
            {
                if (existingIds.Contains(permission.Id))
                {
                    continue;
                }

                db.RolePermissions.Add(new RolePermission
                {
                    OrganizationId = organizationId,
                    RoleId = role.Id,
                    PermissionId = permission.Id
                });
            }
        }
    }

    private static async Task<OrganizationApplication> RequireAdministrationAsync(
        DeleguardDbContext db,
        string organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var app = await db.Applications.FirstOrDefaultAsync(
                      x => x.OrganizationId == organizationId && x.Kind == ApplicationKinds.Administration,
                      cancellationToken)
                  ?? db.Applications.Local.FirstOrDefault(x =>
                      x.OrganizationId == organizationId && x.Kind == ApplicationKinds.Administration);
        if (app is not null)
        {
            return app;
        }

        app = ApplicationSeed.Administration(organizationId, now);
        db.Applications.Add(app);
        db.Environments.Add(ApplicationSeed.Production(organizationId, app.Id, now));
        return app;
    }

    private static async Task<string> RequireAdminAgentAsync(
        DeleguardDbContext db,
        string organizationId,
        string applicationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var agent = await db.Agents.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.ExternalId == AccessControlIds.AdminMcpExternalId,
            cancellationToken);
        if (agent is not null)
        {
            return agent.Id;
        }

        var environment = await db.Environments.FirstOrDefaultAsync(
                              x => x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Name == "production",
                              cancellationToken)
                          ?? db.Environments.Local.FirstOrDefault(x =>
                              x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Name == "production");
        if (environment is null)
        {
            environment = ApplicationSeed.Production(organizationId, applicationId, now);
            db.Environments.Add(environment);
        }

        agent = new Agent
        {
            Id = IdGenerator.New("agt"),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            EnvironmentId = environment.Id,
            ExternalId = AccessControlIds.AdminMcpExternalId,
            DisplayName = "Administration MCP",
            Enabled = true,
            CreatedAt = now
        };
        db.Agents.Add(agent);
        return agent.Id;
    }
}
