using System.Text;
using Deleguard.Application.Identity;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;

namespace Deleguard.Infrastructure.Persistence;

internal static class AdminPolicyYaml
{
    public static string For(AdminMcpToolSpec spec)
    {
        var yaml = new StringBuilder();
        yaml.AppendLine($"id: {spec.Action.Replace('.', '-').Replace('_', '-')}");
        yaml.AppendLine("version: 1");
        yaml.AppendLine("priority: 80");
        yaml.AppendLine("match:");
        yaml.AppendLine($"  action: {spec.Action}");
        yaml.AppendLine($"  resource_type: {spec.ResourceType}");
        yaml.AppendLine("rules:");
        yaml.AppendLine("  - name: deny-foreign-agent");
        yaml.AppendLine("    when:");
        yaml.AppendLine("      field: agent.id");
        yaml.AppendLine("      op: ne");
        yaml.AppendLine($"      value: {AccessControlIds.AdminMcpExternalId}");
        yaml.AppendLine("    effect: DENY");
        yaml.AppendLine("    reason: Administration actions are bound to the admin-mcp agent");
        if (spec.Risk is RiskLevel.High or RiskLevel.Critical)
        {
            yaml.AppendLine("  - name: operator-needs-approval");
            yaml.AppendLine("    when:");
            yaml.AppendLine("      field: actor.roles");
            yaml.AppendLine("      op: contains");
            yaml.AppendLine("      value: operator");
            yaml.AppendLine("    effect: REQUIRE_APPROVAL");
            yaml.AppendLine("    reason: A second org-admin must confirm this administration change");
            yaml.AppendLine("    approval:");
            yaml.AppendLine("      expires_in: 30m");
            yaml.AppendLine("      approver_roles: [org-admin]");
        }

        yaml.AppendLine("  - name: allow-entitled-roles");
        yaml.AppendLine("    when:");
        yaml.AppendLine("      any:");
        foreach (var role in AllowRoles(spec.Risk))
        {
            yaml.AppendLine($"        - {{ field: actor.roles, op: contains, value: {role} }}");
        }

        yaml.AppendLine("    effect: ALLOW");
        yaml.AppendLine("fallback: DENY");
        yaml.AppendLine();
        return yaml.ToString();
    }

    private static IReadOnlyList<string> AllowRoles(RiskLevel risk) => risk switch
    {
        RiskLevel.Low =>
        [
            AccessControlIds.OrgAdminRole,
            AccessControlIds.OperatorRole,
            AccessControlIds.AuditorRole,
            AccessControlIds.BillingManagerRole
        ],
        RiskLevel.Medium => [AccessControlIds.OrgAdminRole, AccessControlIds.OperatorRole],
        _ => [AccessControlIds.OrgAdminRole]
    };
}
