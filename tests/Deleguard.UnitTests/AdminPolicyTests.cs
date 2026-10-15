using System.Text.Json;
using Deleguard.Application.Identity;
using Deleguard.Application.Policies;
using Deleguard.Domain.Common;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class AdminPolicyTests
{
    private readonly NativePolicyEngine _engine = new();

    [Theory]
    [InlineData("admin-user-list.yaml", "admin.user.list", "user", "auditor", "ALLOW")]
    [InlineData("admin-user-list.yaml", "admin.user.list", "user", null, "DENY")]
    [InlineData("admin-user-create.yaml", "admin.user.create", "user", "org-admin", "ALLOW")]
    [InlineData("admin-user-create.yaml", "admin.user.create", "user", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-user-create.yaml", "admin.user.create", "user", "auditor", "DENY")]
    [InlineData("admin-agent-create.yaml", "admin.agent.create", "agent", "operator", "ALLOW")]
    [InlineData("admin-agent-create.yaml", "admin.agent.create", "agent", "auditor", "DENY")]
    [InlineData("admin-agent-rotate-key.yaml", "admin.agent.rotate_key", "agent", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-agent-disable.yaml", "admin.agent.disable", "agent", "org-admin", "ALLOW")]
    [InlineData("admin-agent-disable.yaml", "admin.agent.disable", "agent", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-agent-disable.yaml", "admin.agent.disable", "agent", "auditor", "DENY")]
    [InlineData("admin-agent-enable.yaml", "admin.agent.enable", "agent", "org-admin", "ALLOW")]
    [InlineData("admin-agent-enable.yaml", "admin.agent.enable", "agent", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-agent-enable.yaml", "admin.agent.enable", "agent", "auditor", "DENY")]
    [InlineData("admin-organization-get.yaml", "admin.organization.get", "organization", "auditor", "ALLOW")]
    [InlineData("admin-organization-get.yaml", "admin.organization.get", "organization", null, "DENY")]
    [InlineData("admin-organization-update.yaml", "admin.organization.update", "organization", "org-admin", "ALLOW")]
    [InlineData("admin-organization-update.yaml", "admin.organization.update", "organization", "operator", "ALLOW")]
    [InlineData("admin-organization-update.yaml", "admin.organization.update", "organization", "auditor", "DENY")]
    [InlineData("admin-application-list.yaml", "admin.application.list", "application", "auditor", "ALLOW")]
    [InlineData("admin-application-create.yaml", "admin.application.create", "application", "operator", "ALLOW")]
    [InlineData("admin-application-create.yaml", "admin.application.create", "application", "auditor", "DENY")]
    [InlineData("admin-application-update.yaml", "admin.application.update", "application", "operator", "ALLOW")]
    [InlineData("admin-application-delete.yaml", "admin.application.delete", "application", "org-admin", "ALLOW")]
    [InlineData("admin-application-delete.yaml", "admin.application.delete", "application", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-application-delete.yaml", "admin.application.delete", "application", "auditor", "DENY")]
    [InlineData("admin-environment-get.yaml", "admin.environment.get", "environment", "auditor", "ALLOW")]
    [InlineData("admin-environment-get.yaml", "admin.environment.get", "environment", null, "DENY")]
    [InlineData("admin-docs-list.yaml", "admin.docs.list", "doc", "auditor", "ALLOW")]
    [InlineData("admin-docs-list.yaml", "admin.docs.list", "doc", null, "DENY")]
    [InlineData("admin-docs-get.yaml", "admin.docs.get", "doc", "operator", "ALLOW")]
    [InlineData("admin-docs-get.yaml", "admin.docs.get", "doc", "auditor", "ALLOW")]
    [InlineData("admin-environment-create.yaml", "admin.environment.create", "environment", "operator", "ALLOW")]
    [InlineData("admin-environment-create.yaml", "admin.environment.create", "environment", "auditor", "DENY")]
    [InlineData("admin-environment-update.yaml", "admin.environment.update", "environment", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-environment-delete.yaml", "admin.environment.delete", "environment", "org-admin", "ALLOW")]
    [InlineData("admin-environment-delete.yaml", "admin.environment.delete", "environment", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-environment-delete.yaml", "admin.environment.delete", "environment", "auditor", "DENY")]
    [InlineData("admin-action-create.yaml", "admin.action.create", "action", "operator", "ALLOW")]
    [InlineData("admin-action-update.yaml", "admin.action.update", "action", "operator", "ALLOW")]
    [InlineData("admin-action-update.yaml", "admin.action.update", "action", "auditor", "DENY")]
    [InlineData("admin-action-delete.yaml", "admin.action.delete", "action", "org-admin", "ALLOW")]
    [InlineData("admin-action-delete.yaml", "admin.action.delete", "action", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-action-delete.yaml", "admin.action.delete", "action", "auditor", "DENY")]
    [InlineData("admin-policy-create.yaml", "admin.policy.create", "policy", "org-admin", "ALLOW")]
    [InlineData("admin-policy-create.yaml", "admin.policy.create", "policy", "operator", "ALLOW")]
    [InlineData("admin-policy-create.yaml", "admin.policy.create", "policy", "auditor", "DENY")]
    [InlineData("admin-policy-publish.yaml", "admin.policy.publish", "policy", "org-admin", "ALLOW")]
    [InlineData("admin-policy-publish.yaml", "admin.policy.publish", "policy", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-policy-delete.yaml", "admin.policy.delete", "policy", "org-admin", "ALLOW")]
    [InlineData("admin-policy-delete.yaml", "admin.policy.delete", "policy", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-policy-delete.yaml", "admin.policy.delete", "policy", "auditor", "DENY")]
    [InlineData("admin-mcp-set-tools.yaml", "admin.mcp.set_tools", "mcp", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-gateway-status.yaml", "admin.gateway.status", "gateway", "auditor", "ALLOW")]
    [InlineData("admin-gateway-status.yaml", "admin.gateway.status", "gateway", null, "DENY")]
    [InlineData("admin-gateway-update-settings.yaml", "admin.gateway.update_settings", "gateway", "org-admin", "ALLOW")]
    [InlineData("admin-gateway-update-settings.yaml", "admin.gateway.update_settings", "gateway", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-gateway-update-settings.yaml", "admin.gateway.update_settings", "gateway", "auditor", "DENY")]
    [InlineData("admin-gateway-upsert-route.yaml", "admin.gateway.upsert_route", "gateway", "org-admin", "ALLOW")]
    [InlineData("admin-gateway-upsert-route.yaml", "admin.gateway.upsert_route", "gateway", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-gateway-upsert-route.yaml", "admin.gateway.upsert_route", "gateway", "auditor", "DENY")]
    [InlineData("admin-gateway-apply-openai-chat-pair.yaml", "admin.gateway.apply_openai_chat_pair", "gateway", "org-admin", "ALLOW")]
    [InlineData("admin-gateway-apply-openai-chat-pair.yaml", "admin.gateway.apply_openai_chat_pair", "gateway", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-gateway-apply-openai-chat-pair.yaml", "admin.gateway.apply_openai_chat_pair", "gateway", "auditor", "DENY")]
    [InlineData("admin-gateway-delete-route.yaml", "admin.gateway.delete_route", "gateway", "org-admin", "ALLOW")]
    [InlineData("admin-gateway-delete-route.yaml", "admin.gateway.delete_route", "gateway", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-gateway-delete-route.yaml", "admin.gateway.delete_route", "gateway", "auditor", "DENY")]
    [InlineData("admin-webhook-list.yaml", "admin.webhook.list", "webhook", "auditor", "ALLOW")]
    [InlineData("admin-webhook-get.yaml", "admin.webhook.get", "webhook", "auditor", "ALLOW")]
    [InlineData("admin-webhook-get.yaml", "admin.webhook.get", "webhook", null, "DENY")]
    [InlineData("admin-webhook-create.yaml", "admin.webhook.create", "webhook", "operator", "ALLOW")]
    [InlineData("admin-webhook-create.yaml", "admin.webhook.create", "webhook", "auditor", "DENY")]
    [InlineData("admin-webhook-update.yaml", "admin.webhook.update", "webhook", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-webhook-rotate-secret.yaml", "admin.webhook.rotate_secret", "webhook", "org-admin", "ALLOW")]
    [InlineData("admin-webhook-rotate-secret.yaml", "admin.webhook.rotate_secret", "webhook", "operator", "REQUIRE_APPROVAL")]
    [InlineData("admin-webhook-rotate-secret.yaml", "admin.webhook.rotate_secret", "webhook", "auditor", "DENY")]
    public void Admin_policy_files_match_expected_effect(string file, string action, string resourceType, string? role, string expected)
    {
        var policy = PolicyYamlParser.Parse(Path.GetFileNameWithoutExtension(file), File.ReadAllText(Find(file)));
        var roles = role is null ? Array.Empty<string>() : new[] { role };
        var result = _engine.Evaluate(Envelope("admin-mcp", action, resourceType, roles), [policy]);
        Assert.Equal(DecisionNames.Parse(expected), result.Effect);
    }

    [Fact]
    public void Foreign_agent_is_denied_even_with_org_admin_roles()
    {
        var policy = PolicyYamlParser.Parse("admin-user-create", File.ReadAllText(Find("admin-user-create.yaml")));
        var result = _engine.Evaluate(Envelope("invoice-agent", "admin.user.create", "user", ["org-admin"]), [policy]);
        Assert.Equal(DecisionEffect.Deny, result.Effect);
        Assert.Equal("deny-foreign-agent", result.MatchedRule);
    }

    [Fact]
    public void Fixture_file_covers_allow_deny_and_approval()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var seen = new HashSet<string>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var expected = item.GetProperty("expected").GetString()!;
            seen.Add(expected);
            var action = item.GetProperty("request").GetProperty("action").GetString()!;
            var file = action.Replace('.', '-').Replace('_', '-') + ".yaml";
            var policy = PolicyYamlParser.Parse(action, File.ReadAllText(Find(file)));
            var agent = item.GetProperty("agent_id").GetString()!;
            var roles = item.GetProperty("actor_roles").EnumerateArray().Select(x => x.GetString()!).ToArray();
            var resourceType = item.GetProperty("request").GetProperty("resource_type").GetString()!;
            var result = _engine.Evaluate(Envelope(agent, action, resourceType, roles), [policy]);
            Assert.Equal(DecisionNames.Parse(expected), result.Effect);
        }

        Assert.Contains("ALLOW", seen);
        Assert.Contains("DENY", seen);
        Assert.Contains("REQUIRE_APPROVAL", seen);
    }

    [Fact]
    public void Administration_catalog_policies_are_protected()
    {
        Assert.True(PolicyAdminHandler.IsAdministrationCatalog("admin-action-create", "admin.action.create"));
        Assert.True(PolicyAdminHandler.IsAdministrationCatalog("invoice-cancel", "admin.policy.delete"));
        Assert.False(PolicyAdminHandler.IsAdministrationCatalog("invoice-cancel", "invoice.cancel"));
    }

    [Fact]
    public void Every_catalog_tool_has_a_policy_file()
    {
        foreach (var tool in AdminMcpCatalog.Tools)
        {
            var file = tool.Action.Replace('.', '-').Replace('_', '-') + ".yaml";
            Assert.True(File.Exists(Find(file)), file);
        }
    }

    private static AuthorizationEnvelope Envelope(string agent, string action, string resourceType, IReadOnlyList<string> roles) =>
        new(agent, "production", "usr_demo_admin", "human", action, resourceType, "resource",
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), roles);

    private static string Find(string fileName)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "policies", "admin", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(dir, "..", "..", "..", "..", "..", "policies", "admin", fileName);
            if (File.Exists(Path.GetFullPath(candidate)))
            {
                return Path.GetFullPath(candidate);
            }

            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "policies", "admin", fileName));
    }

    private static string FindFixture()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "policies", "fixtures", "admin-actions.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "policies", "fixtures", "admin-actions.json"));
    }
}
