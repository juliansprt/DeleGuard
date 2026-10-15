using System.Text.Json;
using Deleguard.Application.Identity;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;

namespace Deleguard.UnitTests;

public sealed class AdminMcpCatalogTests
{
    [Fact]
    public void Policy_create_is_a_closed_write_tool_without_policy_id()
    {
        var spec = AdminMcpCatalog.Find("policy_create");
        Assert.NotNull(spec);
        Assert.Equal("admin.policy.create", spec.Action);
        Assert.Equal("policy", spec.ResourceType);
        Assert.Equal(PermissionCatalog.PoliciesWrite, spec.Permission);
        Assert.Equal(RiskLevel.Medium, spec.Risk);
        Assert.False(spec.ReadOnlyHint);
        var properties = spec.InputSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("yaml", out _));
        Assert.True(properties.TryGetProperty("agent_ids", out _));
        Assert.False(properties.TryGetProperty("policy_id", out _));
        var required = spec.InputSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet();
        Assert.Contains("yaml", required);
        Assert.Contains("agent_ids", required);
        Assert.DoesNotContain("policy_id", required);
    }

    [Fact]
    public void Inherit_enabled_tools_adds_policy_create_next_to_create_version()
    {
        var inherited = AdminMcpCatalog.InheritEnabledTools(["policy_list", "policy_create_version"]);
        Assert.Contains("policy_create", inherited);
        Assert.Contains("policy_create_version", inherited);
        Assert.Contains("docs_list", inherited);
        Assert.Contains("docs_get", inherited);
        Assert.Equal(
            new[] { "docs_list", "docs_get", "policy_list", "policy_create", "policy_create_version" },
            inherited);

        Assert.DoesNotContain("policy_create", AdminMcpCatalog.InheritEnabledTools(["policy_list"]));
        Assert.Contains("docs_list", AdminMcpCatalog.InheritEnabledTools(["policy_list"]));
        Assert.Equal(AdminMcpCatalog.ToolNames, AdminMcpCatalog.InheritEnabledTools(AdminMcpCatalog.ToolNames));
        Assert.Contains("gateway_apply_openai_chat_pair", AdminMcpCatalog.InheritEnabledTools(["gateway_upsert_route"]));
        var fromEnvironments = AdminMcpCatalog.InheritEnabledTools(["environment_list"]);
        Assert.Contains("organization_get", fromEnvironments);
        Assert.Contains("application_list", fromEnvironments);
        Assert.Contains("application_create", AdminMcpCatalog.InheritEnabledTools(["agent_create"]));
    }

    [Fact]
    public void Catalog_order_is_deterministic()
    {
        Assert.Equal(AdminMcpCatalog.Tools.Select(tool => tool.ToolName).ToList(), AdminMcpCatalog.ToolNames.ToList());
        Assert.Equal(AdminMcpCatalog.ToolNames.Distinct(StringComparer.Ordinal).Count(), AdminMcpCatalog.ToolNames.Count);
        Assert.Equal("user_list", AdminMcpCatalog.ToolNames[0]);
        Assert.Equal("audit_list", AdminMcpCatalog.ToolNames[^1]);
        Assert.Contains("docs_list", AdminMcpCatalog.ToolNames);
        Assert.Contains("docs_get", AdminMcpCatalog.ToolNames);
        Assert.Contains("organization_get", AdminMcpCatalog.ToolNames);
        Assert.Contains("organization_update", AdminMcpCatalog.ToolNames);
        Assert.Contains("application_list", AdminMcpCatalog.ToolNames);
        Assert.Contains("application_create", AdminMcpCatalog.ToolNames);
        Assert.Contains("application_update", AdminMcpCatalog.ToolNames);
        Assert.Contains("application_delete", AdminMcpCatalog.ToolNames);
        Assert.Contains("webhook_list", AdminMcpCatalog.ToolNames);
        Assert.Contains("webhook_create", AdminMcpCatalog.ToolNames);
        Assert.Contains("webhook_rotate_secret", AdminMcpCatalog.ToolNames);
        Assert.Contains("gateway_status", AdminMcpCatalog.ToolNames);
        Assert.Contains("gateway_update_settings", AdminMcpCatalog.ToolNames);
        Assert.Contains("gateway_upsert_route", AdminMcpCatalog.ToolNames);
        Assert.Contains("gateway_apply_openai_chat_pair", AdminMcpCatalog.ToolNames);
        Assert.Contains("gateway_delete_route", AdminMcpCatalog.ToolNames);
    }

    [Fact]
    public void Every_tool_has_a_closed_json_schema_and_envelope_output()
    {
        foreach (var tool in AdminMcpCatalog.Tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Title), tool.ToolName);
            Assert.Equal(JsonValueKind.Object, tool.InputSchema.ValueKind);
            Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
            Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean(), tool.ToolName);
            Assert.True(tool.InputSchema.TryGetProperty("properties", out var properties), tool.ToolName);
            Assert.Equal(JsonValueKind.Object, properties.ValueKind);
            Assert.False(tool.OpenWorldHint);

            Assert.Equal(JsonValueKind.Object, tool.OutputSchema.ValueKind);
            Assert.False(tool.OutputSchema.GetProperty("additionalProperties").GetBoolean(), tool.ToolName);
            Assert.Contains("result_type", tool.OutputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        }
    }

    [Fact]
    public void Annotations_match_risk()
    {
        foreach (var tool in AdminMcpCatalog.Tools)
        {
            if (tool.Risk == RiskLevel.Low)
            {
                Assert.True(tool.ReadOnlyHint, tool.ToolName);
                Assert.False(tool.DestructiveHint, tool.ToolName);
                Assert.True(tool.IdempotentHint, tool.ToolName);
            }
            else
            {
                Assert.False(tool.ReadOnlyHint, tool.ToolName);
            }

            if (tool.ToolName is "user_disable" or "agent_rotate_key" or "agent_disable" or "environment_delete" or "action_delete" or "policy_delete" or "mcp_revoke_token" or "webhook_rotate_secret" or "gateway_delete_route" or "application_delete")
            {
                Assert.True(tool.DestructiveHint, tool.ToolName);
            }
        }
    }

    [Fact]
    public void Paged_list_tools_accept_limit_and_cursor()
    {
        foreach (var name in new[] { "user_list", "agent_list", "environment_list", "application_list", "policy_list", "webhook_list", "approval_list", "audit_list" })
        {
            var tool = AdminMcpCatalog.Find(name);
            Assert.NotNull(tool);
            Assert.True(tool.Paged);
            var properties = tool.InputSchema.GetProperty("properties");
            Assert.True(properties.TryGetProperty("limit", out _));
            Assert.True(properties.TryGetProperty("cursor", out _));
        }
    }

    [Fact]
    public void Rejects_unknown_arguments_when_schema_is_closed()
    {
        var spec = AdminMcpCatalog.Find("user_list")!;
        var args = JsonSerializer.SerializeToElement(new { unexpected = true });
        var ex = Assert.Throws<Deleguard.Application.RequestValidationException>(() => AdminMcpSchema.Validate(spec, args));
        Assert.Contains("unexpected", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_envelope_points_at_console_not_an_approve_tool()
    {
        var outcome = new Deleguard.Application.Abstractions.AdminActionOutcome<string>(
            "REQUIRE_APPROVAL", "dec_1", "needs review", "apr_1", null, null);
        var envelope = AdminMcpEnvelope.FromOutcome(outcome);
        Assert.Equal("require_approval", envelope.ResultType);
        Assert.False(envelope.IsError);
        Assert.Contains("/approvals/apr_1", envelope.NextAction, StringComparison.Ordinal);
        Assert.Contains("Do not call an approve tool", envelope.NextAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Deny_envelope_is_a_tool_error()
    {
        var outcome = new Deleguard.Application.Abstractions.AdminActionOutcome<string>(
            "DENY", "dec_2", "default deny", null, null, null);
        var envelope = AdminMcpEnvelope.FromOutcome(outcome);
        Assert.Equal("deny", envelope.ResultType);
        Assert.True(envelope.IsError);
        Assert.Null(envelope.Data);
    }

    [Fact]
    public void Resource_uris_parse()
    {
        var policy = AdminMcpResources.Parse("deleguard://policy/pol_abc");
        Assert.Equal("policy", policy.Kind);
        Assert.Equal("pol_abc", policy.Id);
        var docs = AdminMcpResources.Parse("deleguard://docs/environment-setup");
        Assert.Equal("docs", docs.Kind);
        Assert.Equal("environment-setup", docs.Id);
        Assert.Throws<Deleguard.Application.RequestValidationException>(() => AdminMcpResources.Parse("https://example/policy/1"));
    }

    [Fact]
    public void Approval_facts_redact_passwords()
    {
        var facts = JsonSerializer.SerializeToElement(new { email = "a@b.c", password = "secret-value", nested = new { api_key = "dgk_x" } });
        var redacted = AdminMcpResources.RedactSecrets(facts)!.ToString();
        Assert.Contains("a@b.c", redacted, StringComparison.Ordinal);
        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("dgk_x", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_catalog_never_instructs_an_approve_tool()
    {
        Assert.Contains("review-pending-approvals", AdminMcpPrompts.All.Select(p => p.Name));
        Assert.Contains("explain-last-deny", AdminMcpPrompts.All.Select(p => p.Name));
        Assert.Contains("safe-publish-checklist", AdminMcpPrompts.All.Select(p => p.Name));
        Assert.Contains("configure-environment", AdminMcpPrompts.All.Select(p => p.Name));
        Assert.Contains("configure-ai-gateway", AdminMcpPrompts.All.Select(p => p.Name));
        foreach (var prompt in AdminMcpPrompts.All)
        {
            Assert.DoesNotContain("approve_request", prompt.Template, StringComparison.Ordinal);
            Assert.DoesNotContain("\"approve\"", prompt.Template, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Setup_docs_cover_environment_configuration()
    {
        Assert.Contains("environment-setup", AdminMcpDocs.Slugs);
        Assert.Contains("configuration", AdminMcpDocs.Slugs);
        Assert.Contains("configure-with-mcp", AdminMcpDocs.Slugs);
        Assert.Contains("ai-gateway", AdminMcpDocs.Slugs);
        Assert.Equal(AdminMcpDocs.All.Select(doc => doc.Slug).Distinct(StringComparer.Ordinal).Count(), AdminMcpDocs.Slugs.Length);
        var listed = AdminMcpDocs.List("http://localhost:4321/");
        var json = JsonSerializer.Serialize(listed);
        Assert.Contains("configure-with-mcp", json, StringComparison.Ordinal);
        Assert.Contains("http://localhost:4321/start/environment/", json, StringComparison.Ordinal);
        Assert.Contains("/en/console/ai-gateway/", json, StringComparison.Ordinal);
        var gateway = AdminMcpDocs.Get("ai-gateway", "http://localhost:4321");
        var gatewayJson = JsonSerializer.Serialize(gateway);
        Assert.Contains("http://localhost:4321/en/console/ai-gateway/", gatewayJson, StringComparison.Ordinal);
        Assert.Contains("http://localhost:4321/console/ai-gateway/", gatewayJson, StringComparison.Ordinal);
        var page = AdminMcpDocs.Get("environment-setup", "http://localhost:4321");
        var body = JsonSerializer.Serialize(page);
        Assert.Contains("Fail closed", body, StringComparison.Ordinal);
        Assert.Throws<Deleguard.Application.RequestValidationException>(() => AdminMcpDocs.Get("missing", "http://localhost:4321"));
    }
}

public sealed class AdminMcpPagingTests
{
    [Fact]
    public void Slice_returns_opaque_cursor_then_exhausts()
    {
        var items = Enumerable.Range(0, 3).Select(i => i.ToString()).ToList();
        var first = AdminMcpPaging.Slice(items, 2, 0);
        Assert.Equal(["0", "1"], first.Items);
        Assert.False(string.IsNullOrWhiteSpace(first.NextCursor));
        var second = AdminMcpPaging.Slice(items, 2, AdminMcpPaging.Decode(first.NextCursor));
        Assert.Equal(["2"], second.Items);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public void Invalid_cursor_fails_closed_as_validation()
    {
        Assert.Throws<Deleguard.Application.RequestValidationException>(() => AdminMcpPaging.Decode("%%%"));
    }

    [Fact]
    public void Limit_is_clamped()
    {
        var args = JsonSerializer.SerializeToElement(new { limit = 500 });
        var (limit, offset) = AdminMcpPaging.Read(args);
        Assert.Equal(AdminMcpPaging.MaxLimit, limit);
        Assert.Equal(0, offset);
    }
}
