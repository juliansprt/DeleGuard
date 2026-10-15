using Deleguard.Application.Identity;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Policies;
using Deleguard.Infrastructure.Identity;
using Deleguard.Infrastructure.Persistence;
using Deleguard.Infrastructure.Security;
using Deleguard.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Deleguard.Infrastructure.Persistence;

public sealed class DemoSeeder
{
    public const string DemoAgentKey = "dgk_demo_invoice_agent_local_dev_only";
    public const string DemoConsoleToken = "dgc_demo_console_local_dev_only";
    public const string DemoMcpToken = "dgm_demo_admin_local_dev_only";
    public const string DemoMcpCredentialId = "mcp_demo_admin";
    public const string OrganizationId = "org_demo";
    public const string ApplicationId = "app_demo";
    public const string AdminApplicationId = "app_demo_admin";
    public const string EnvironmentId = "env_demo_production";
    public const string AdminEnvironmentId = "env_demo_admin_production";
    public const string AgentId = "agt_invoice";
    public const string PolicyId = "pol_invoice_cancel";
    public const string AdminMcpAgentId = "agt_admin_mcp";
    public const string DemoAdminUserId = "usr_demo_admin";
    public const string DemoAdminEmail = "admin@demo.local";
    public const string DemoAdminPassword = "DeleguardDemo!23";

    private readonly DeleguardDbContext _db;
    private readonly DemoAuthOptions _auth;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DemoSeeder> _logger;

    public DemoSeeder(
        DeleguardDbContext db,
        IOptions<DemoAuthOptions> auth,
        IHostEnvironment environment,
        ILogger<DemoSeeder> logger)
    {
        _db = db;
        _auth = auth.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!await _db.Organizations.AnyAsync(x => x.Id == OrganizationId, cancellationToken))
        {
            await SeedDemoTenantAsync(cancellationToken);
        }

        await EnsureApplicationsAsync(cancellationToken);
        await EnsureAccessControlAsync(cancellationToken);
        await AdminCatalogSynchronizer.EnsureAllAsync(_db, cancellationToken);
        await EnsureDemoAdministrationMcpAsync(cancellationToken);
        await EnsureGuardBindingAsync(cancellationToken);
        await EnsureGatewayAsync(cancellationToken);
    }

    private async Task SeedDemoTenantAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        _db.Organizations.Add(new Organization { Id = OrganizationId, Name = "Deleguard Demo", CreatedAt = now });
        var adminApp = ApplicationSeed.Administration(OrganizationId, now, AdminApplicationId);
        var productApp = ApplicationSeed.DefaultProduct(OrganizationId, "Deleguard Demo", now, ApplicationId);
        _db.Applications.Add(adminApp);
        _db.Applications.Add(productApp);
        _db.Environments.Add(ApplicationSeed.Production(OrganizationId, productApp.Id, now, EnvironmentId));
        _db.Environments.Add(ApplicationSeed.Production(OrganizationId, adminApp.Id, now, AdminEnvironmentId));
        _db.Agents.Add(new Agent
        {
            Id = AgentId,
            OrganizationId = OrganizationId,
            ApplicationId = ApplicationId,
            EnvironmentId = EnvironmentId,
            ExternalId = "invoice-agent",
            DisplayName = "Invoice Agent",
            Enabled = true,
            CreatedAt = now
        });
        _db.AgentCredentials.Add(new AgentCredential
        {
            Id = "crd_invoice_demo",
            OrganizationId = OrganizationId,
            AgentId = AgentId,
            KeyPrefix = ApiKeyHasher.Prefix(DemoAgentKey),
            KeyHash = ApiKeyHasher.Hash(DemoAgentKey, _auth.ApiKeyPepper),
            CreatedAt = now
        });
        _db.ActionDefinitions.Add(new ActionDefinition
        {
            Id = "act_invoice_cancel",
            OrganizationId = OrganizationId,
            ApplicationId = ApplicationId,
            Name = "invoice.cancel",
            ResourceType = "invoice",
            Risk = RiskLevel.High,
            CreatedAt = now
        });
        _db.AgentActionGrants.Add(new AgentActionGrant
        {
            OrganizationId = OrganizationId,
            ApplicationId = ApplicationId,
            AgentId = AgentId,
            ActionDefinitionId = "act_invoice_cancel",
            CreatedAt = now
        });

        var yaml = await LoadPolicyYamlAsync(cancellationToken);
        var compiled = PolicyYamlParser.Parse(PolicyId, yaml);
        _db.Policies.Add(new PolicyDefinition
        {
            Id = PolicyId,
            OrganizationId = OrganizationId,
            ApplicationId = ApplicationId,
            Slug = compiled.PolicyId,
            PublishedVersionId = "pver_invoice_v4",
            CreatedAt = now
        });
        _db.PolicyVersions.Add(new PolicyVersion
        {
            Id = "pver_invoice_v4",
            OrganizationId = OrganizationId,
            PolicyId = PolicyId,
            Version = compiled.Version,
            Yaml = yaml,
            Action = compiled.Action,
            ResourceType = compiled.ResourceType,
            Priority = compiled.Priority,
            Published = true,
            CreatedAt = now,
            PublishedAt = now
        });
        _db.AgentPolicyGrants.Add(new AgentPolicyGrant
        {
            OrganizationId = OrganizationId,
            ApplicationId = ApplicationId,
            AgentId = AgentId,
            PolicyId = PolicyId,
            CreatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded demo tenant {OrganizationId} with invoice.cancel policy.", OrganizationId);
    }

    private async Task EnsureApplicationsAsync(CancellationToken cancellationToken)
    {
        if (!await _db.Organizations.AnyAsync(x => x.Id == OrganizationId, cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!await _db.Applications.AnyAsync(x => x.Id == AdminApplicationId, cancellationToken))
        {
            _db.Applications.Add(ApplicationSeed.Administration(OrganizationId, now, AdminApplicationId));
        }

        if (!await _db.Applications.AnyAsync(x => x.Id == ApplicationId, cancellationToken))
        {
            _db.Applications.Add(ApplicationSeed.DefaultProduct(OrganizationId, "Deleguard Demo", now, ApplicationId));
        }

        if (!await _db.Environments.AnyAsync(x => x.Id == AdminEnvironmentId, cancellationToken))
        {
            _db.Environments.Add(ApplicationSeed.Production(OrganizationId, AdminApplicationId, now, AdminEnvironmentId));
        }

        if (!await _db.Environments.AnyAsync(x => x.Id == EnvironmentId, cancellationToken))
        {
            _db.Environments.Add(ApplicationSeed.Production(OrganizationId, ApplicationId, now, EnvironmentId));
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAccessControlAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await _db.Agents.AnyAsync(x => x.Id == AdminMcpAgentId, cancellationToken))
        {
            _db.Agents.Add(new Agent
            {
                Id = AdminMcpAgentId,
                OrganizationId = OrganizationId,
                ApplicationId = AdminApplicationId,
                EnvironmentId = AdminEnvironmentId,
                ExternalId = AccessControlIds.AdminMcpExternalId,
                DisplayName = "Administration MCP",
                Enabled = true,
                CreatedAt = now
            });
        }

        foreach (var (name, description) in PermissionCatalog.All)
        {
            var id = "prm_" + name.Replace('.', '_');
            if (!await _db.Permissions.AnyAsync(x => x.Id == id, cancellationToken))
            {
                _db.Permissions.Add(new Permission
                {
                    Id = id,
                    OrganizationId = OrganizationId,
                    Name = name,
                    Description = description
                });
            }
        }

        var roles = new (string Id, string Slug, string Display)[]
        {
            ("rol_org_admin", AccessControlIds.OrgAdminRole, "Organization admin"),
            ("rol_operator", AccessControlIds.OperatorRole, "Operator"),
            ("rol_auditor", AccessControlIds.AuditorRole, "Auditor"),
            ("rol_billing_manager", AccessControlIds.BillingManagerRole, "Billing manager")
        };
        foreach (var (id, slug, display) in roles)
        {
            if (!await _db.Roles.AnyAsync(x => x.Id == id, cancellationToken))
            {
                _db.Roles.Add(new Role
                {
                    Id = id,
                    OrganizationId = OrganizationId,
                    Slug = slug,
                    DisplayName = display,
                    CreatedAt = now
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var (id, slug, _) in roles)
        {
            var allowed = PermissionCatalog.ForRole(slug).ToHashSet(StringComparer.Ordinal);
            var existingIds = (await _db.RolePermissions.Where(x => x.RoleId == id).Select(x => x.PermissionId).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            var permissions = await _db.Permissions.Where(x => x.OrganizationId == OrganizationId && allowed.Contains(x.Name)).ToListAsync(cancellationToken);
            foreach (var permission in permissions)
            {
                if (existingIds.Contains(permission.Id))
                {
                    continue;
                }

                _db.RolePermissions.Add(new RolePermission
                {
                    OrganizationId = OrganizationId,
                    RoleId = id,
                    PermissionId = permission.Id
                });
            }
        }

        if (!await _db.Users.AnyAsync(x => x.Id == DemoAdminUserId, cancellationToken))
        {
            _db.Users.Add(new User
            {
                Id = DemoAdminUserId,
                OrganizationId = OrganizationId,
                Email = _auth.DemoAdminEmail.Trim().ToLowerInvariant(),
                DisplayName = "Demo Admin",
                PasswordHash = PasswordHasher.Hash(_auth.DemoAdminPassword, _auth.ApiKeyPepper),
                Enabled = true,
                CreatedAt = now
            });
            _db.UserRoles.Add(new UserRole
            {
                OrganizationId = OrganizationId,
                UserId = DemoAdminUserId,
                RoleId = "rol_org_admin"
            });
        }

        if (!await _db.McpSettings.AnyAsync(x => x.OrganizationId == OrganizationId, cancellationToken))
        {
            _db.McpSettings.Add(new OrganizationMcpSettings
            {
                OrganizationId = OrganizationId,
                AgentId = AdminMcpAgentId,
                Enabled = true,
                EnabledToolsJson = System.Text.Json.JsonSerializer.Serialize(AdminMcpCatalog.ToolNames),
                UpdatedAt = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Ensured demo IAM and admin-mcp agent.");
    }

    private async Task EnsureDemoAdministrationMcpAsync(CancellationToken cancellationToken)
    {
        var settings = await _db.McpSettings.FirstOrDefaultAsync(x => x.OrganizationId == OrganizationId, cancellationToken);
        if (settings is null)
        {
            return;
        }

        settings.Enabled = true;
        settings.EnabledToolsJson = System.Text.Json.JsonSerializer.Serialize(AdminMcpCatalog.ToolNames);
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        var hash = ApiKeyHasher.Hash(DemoMcpToken, _auth.ApiKeyPepper);
        var prefix = ApiKeyHasher.Prefix(DemoMcpToken);
        var credential = await _db.McpCredentials.FirstOrDefaultAsync(x => x.Id == DemoMcpCredentialId, cancellationToken);
        if (credential is null)
        {
            _db.McpCredentials.Add(new McpCredential
            {
                Id = DemoMcpCredentialId,
                OrganizationId = OrganizationId,
                UserId = DemoAdminUserId,
                KeyPrefix = prefix,
                KeyHash = hash,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            credential.RevokedAt = null;
            credential.ExpiresAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Administration MCP is enabled for the demo tenant.");
    }

    private async Task EnsureGuardBindingAsync(CancellationToken cancellationToken)
    {
        if (await _db.ActionMcpBindings.AnyAsync(
                x => x.OrganizationId == OrganizationId && x.McpServer == "invoice-demo" && x.McpTool == "cancel_invoice",
                cancellationToken))
        {
            return;
        }

        var action = await _db.ActionDefinitions.FirstOrDefaultAsync(
            x => x.OrganizationId == OrganizationId && x.Name == "invoice.cancel",
            cancellationToken);
        if (action is null)
        {
            return;
        }

        _db.ActionMcpBindings.Add(new ActionMcpBinding
        {
            Id = "map_invoice_cancel_demo",
            OrganizationId = OrganizationId,
            ActionDefinitionId = action.Id,
            McpServer = "invoice-demo",
            McpTool = "cancel_invoice",
            ResourceIdPath = "invoice_id",
            AttributePathsJson = """{"amount":"amount","status":"status","reportedToTaxAuthority":"reportedToTaxAuthority"}""",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded Guard MCP binding invoice-demo__cancel_invoice -> invoice.cancel.");
    }

    private async Task EnsureGatewayAsync(CancellationToken cancellationToken)
    {
        var settings = await _db.GatewaySettings.FirstOrDefaultAsync(x => x.OrganizationId == OrganizationId, cancellationToken);
        if (settings is null)
        {
            return;
        }

        settings.Enabled = true;
        settings.RoutingMode = string.IsNullOrWhiteSpace(settings.RoutingMode) ? GatewayRoutingModes.Static : settings.RoutingMode;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Enabled AI Gateway for the demo tenant.");
    }

    private async Task<string> LoadPolicyYamlAsync(CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            Path.Combine(_environment.ContentRootPath, "policies", "invoice-cancel-production.yaml"),
            Path.Combine(AppContext.BaseDirectory, "policies", "invoice-cancel-production.yaml"),
            Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "..", "..", "policies", "invoice-cancel-production.yaml"))
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path, cancellationToken);
            }
        }

        return """
id: invoice-cancel-production
version: 4
priority: 100
match:
  action: invoice.cancel
  resource_type: invoice
rules:
  - name: never-cancel-tax-reported
    when:
      field: resource.attributes.reportedToTaxAuthority
      op: eq
      value: true
    effect: DENY
    reason: Tax-reported invoices require a separate regulated process
  - name: high-value-approval
    when:
      field: resource.attributes.amount
      op: gt
      value: 10000000
    effect: REQUIRE_APPROVAL
    approval:
      expires_in: 15m
      approver_roles: [billing-manager]
  - name: allow-draft-low-risk
    when:
      all:
        - { field: resource.attributes.status, op: eq, value: draft }
        - { field: resource.attributes.amount, op: lte, value: 10000000 }
    effect: ALLOW
fallback: DENY
""";
    }
}
