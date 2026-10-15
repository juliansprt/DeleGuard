namespace Deleguard.Domain.Identity;

public sealed class User
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? PasswordHash { get; set; }
    public string? ExternalIssuer { get; set; }
    public string? ExternalSubject { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class Role
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string Slug { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class Permission
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
}

public sealed class UserRole
{
    public required string OrganizationId { get; init; }
    public required string UserId { get; init; }
    public required string RoleId { get; init; }
}

public sealed class RolePermission
{
    public required string OrganizationId { get; init; }
    public required string RoleId { get; init; }
    public required string PermissionId { get; init; }
}

public sealed class OrganizationMcpSettings
{
    public required string OrganizationId { get; init; }
    public required string AgentId { get; set; }
    public bool Enabled { get; set; }
    public required string EnabledToolsJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class McpCredential
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string UserId { get; init; }
    public required string KeyPrefix { get; init; }
    public required string KeyHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}

public static class AccessControlIds
{
    public const string AdminMcpExternalId = "admin-mcp";
    public const string OrgAdminRole = "org-admin";
    public const string OperatorRole = "operator";
    public const string AuditorRole = "auditor";
    public const string BillingManagerRole = "billing-manager";
    public const string PlatformAdminRole = "platform-admin";
}

public static class PermissionCatalog
{
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string RolesRead = "roles.read";
    public const string RolesWrite = "roles.write";
    public const string OrganizationWrite = "organization.write";
    public const string ApplicationsRead = "applications.read";
    public const string ApplicationsWrite = "applications.write";
    public const string AgentsRead = "agents.read";
    public const string AgentsWrite = "agents.write";
    public const string EnvironmentsRead = "environments.read";
    public const string EnvironmentsWrite = "environments.write";
    public const string ActionsRead = "actions.read";
    public const string ActionsWrite = "actions.write";
    public const string PoliciesRead = "policies.read";
    public const string PoliciesWrite = "policies.write";
    public const string PoliciesPublish = "policies.publish";
    public const string ApprovalsRead = "approvals.read";
    public const string ApprovalsResolve = "approvals.resolve";
    public const string AuditRead = "audit.read";
    public const string McpRead = "mcp.read";
    public const string McpWrite = "mcp.write";
    public const string WebhooksRead = "webhooks.read";
    public const string WebhooksWrite = "webhooks.write";
    public const string GatewayRead = "gateway.read";
    public const string GatewayWrite = "gateway.write";

    public static readonly IReadOnlyList<(string Name, string Description)> All =
    [
        (UsersRead, "List users"),
        (UsersWrite, "Create, disable, and assign users"),
        (RolesRead, "List roles and permissions"),
        (RolesWrite, "Change role permissions"),
        (OrganizationWrite, "Rename the organization"),
        (ApplicationsRead, "List applications"),
        (ApplicationsWrite, "Create, rename, and delete applications"),
        (AgentsRead, "List agents"),
        (AgentsWrite, "Create, disable, and rotate agent keys"),
        (EnvironmentsRead, "List environments"),
        (EnvironmentsWrite, "Create, rename, and delete environments"),
        (ActionsRead, "List actions"),
        (ActionsWrite, "Register actions"),
        (PoliciesRead, "List and inspect policies"),
        (PoliciesWrite, "Create policy drafts and versions"),
        (PoliciesPublish, "Publish policy versions"),
        (ApprovalsRead, "View the approval queue"),
        (ApprovalsResolve, "Approve or deny pending actions"),
        (AuditRead, "Read the audit ledger"),
        (McpRead, "View Administration MCP status"),
        (McpWrite, "Enable MCP, choose tools, and issue tokens"),
        (WebhooksRead, "List webhook endpoints and delivery attempts"),
        (WebhooksWrite, "Create, update, ping, and rotate webhook endpoints"),
        (GatewayRead, "View AI Gateway status, catalog, and budgets"),
        (GatewayWrite, "Enable AI Gateway and change routing or spend limits")
    ];

    public static IReadOnlyList<string> ForRole(string slug) => slug switch
    {
        AccessControlIds.OrgAdminRole => All.Select(item => item.Name).ToList(),
        AccessControlIds.OperatorRole =>
        [
            ApplicationsRead, ApplicationsWrite, AgentsRead, AgentsWrite, EnvironmentsRead, EnvironmentsWrite,
            ActionsRead, ActionsWrite, PoliciesRead, PoliciesWrite, ApprovalsRead, AuditRead, WebhooksRead,
            WebhooksWrite, GatewayRead, GatewayWrite
        ],
        AccessControlIds.AuditorRole =>
        [
            UsersRead, RolesRead, ApplicationsRead, AgentsRead, EnvironmentsRead, ActionsRead, PoliciesRead,
            ApprovalsRead, AuditRead, McpRead, WebhooksRead, GatewayRead
        ],
        AccessControlIds.BillingManagerRole =>
        [
            ApprovalsRead, ApprovalsResolve, PoliciesRead, AuditRead, ApplicationsRead, AgentsRead,
            EnvironmentsRead, ActionsRead, WebhooksRead, GatewayRead
        ],
        _ => []
    };
}
