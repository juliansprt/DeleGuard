using System.Text.Json;
using Deleguard.Domain.Common;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class PolicyYamlWriterTests
{
    private readonly NativePolicyEngine _engine = new();

    [Fact]
    public void Invoice_policy_round_trips_identity()
    {
        var original = PolicyYamlParser.Parse("invoice-cancel-production", File.ReadAllText(FindPolicy()));
        var yaml = PolicyYamlWriter.Write(original);
        var roundTrip = PolicyYamlParser.Parse("invoice-cancel-production", yaml);

        Assert.Equal(original.PolicyId, roundTrip.PolicyId);
        Assert.Equal(original.Version, roundTrip.Version);
        Assert.Equal(original.Priority, roundTrip.Priority);
        Assert.Equal(original.Action, roundTrip.Action);
        Assert.Equal(original.ResourceType, roundTrip.ResourceType);
        Assert.Equal(original.Fallback, roundTrip.Fallback);
        Assert.Equal(original.Rules.Count, roundTrip.Rules.Count);
        for (var i = 0; i < original.Rules.Count; i++)
        {
            Assert.Equal(original.Rules[i].Name, roundTrip.Rules[i].Name);
            Assert.Equal(original.Rules[i].Effect, roundTrip.Rules[i].Effect);
            Assert.Equal(original.Rules[i].Reason, roundTrip.Rules[i].Reason);
            Assert.Equal(original.Rules[i].ApprovalExpiresIn, roundTrip.Rules[i].ApprovalExpiresIn);
            Assert.Equal(original.Rules[i].ApproverRoles, roundTrip.Rules[i].ApproverRoles);
        }
    }

    [Fact]
    public void Regenerated_invoice_yaml_keeps_engine_outcomes()
    {
        var yaml = PolicyYamlWriter.Write(PolicyYamlParser.Parse("invoice-cancel-production", File.ReadAllText(FindPolicy())));
        var policy = PolicyYamlParser.Parse("invoice-cancel-production", yaml);

        Assert.Equal(DecisionEffect.Allow, Evaluate(policy, "draft", 5_000_000, false).Effect);
        Assert.Equal("allow-draft-low-risk", Evaluate(policy, "draft", 5_000_000, false).MatchedRule);
        Assert.Equal(DecisionEffect.RequireApproval, Evaluate(policy, "issued", 35_000_000, false).Effect);
        Assert.Equal(DecisionEffect.Deny, Evaluate(policy, "issued", 1_000, true).Effect);
        Assert.Equal(DecisionEffect.Deny, Evaluate(policy, "issued", 100, false).Effect);
    }

    [Fact]
    public void Require_approval_emits_approval_block_and_deny_does_not()
    {
        var deny = new CompiledPolicy(
            "sample",
            1,
            1,
            "invoice.cancel",
            "invoice",
            [
                new CompiledRule("block", new ConditionNode.Always(), DecisionEffect.Deny, "no", null, [])
            ],
            DecisionEffect.Deny);
        var approval = new CompiledPolicy(
            "sample",
            1,
            1,
            "invoice.cancel",
            "invoice",
            [
                new CompiledRule(
                    "ask",
                    new ConditionNode.Field("resource.attributes.amount", "gt", 10L),
                    DecisionEffect.RequireApproval,
                    "too high",
                    TimeSpan.FromMinutes(15),
                    ["billing-manager"])
            ],
            DecisionEffect.Deny);

        var denyYaml = PolicyYamlWriter.Write(deny);
        var approvalYaml = PolicyYamlWriter.Write(approval);

        Assert.DoesNotContain("approval:", denyYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("effect: ALLOW", denyYaml, StringComparison.Ordinal);
        Assert.Contains("approval:", approvalYaml, StringComparison.Ordinal);
        Assert.Contains("expires_in: 15m", approvalYaml, StringComparison.Ordinal);
        Assert.Contains("billing-manager", approvalYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("approval:", PolicyYamlWriter.Write(new CompiledPolicy(
            "sample", 1, 1, "invoice.cancel", "invoice",
            [new CompiledRule("ok", new ConditionNode.Always(), DecisionEffect.Allow, null, TimeSpan.FromMinutes(5), ["x"])],
            DecisionEffect.Deny)), StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_document_round_trips_through_writer()
    {
        var compiled = PolicyYamlParser.Parse("invoice-cancel-production", File.ReadAllText(FindPolicy()));
        var document = PolicyEditorMapper.FromCompiled(compiled, compiled.Version + 1);
        Assert.True(document.VisualSupported);
        var yaml = PolicyYamlWriter.Write(document);
        var again = PolicyYamlParser.Parse(compiled.PolicyId, yaml);
        Assert.Equal(compiled.Rules.Select(r => r.Name), again.Rules.Select(r => r.Name));
        Assert.Equal(DecisionEffect.Allow, Evaluate(again, "draft", 5_000_000, false).Effect);
    }

    [Fact]
    public void Empty_rules_draft_is_canonical_and_fail_closed()
    {
        var yaml = PolicyYamlWriter.Write(new CompiledPolicy(
            "example-policy",
            1,
            10,
            "invoice.cancel",
            "invoice",
            [],
            DecisionEffect.Deny));
        var parsed = PolicyYamlParser.Parse("example-policy", yaml);

        Assert.Contains("rules: []", yaml, StringComparison.Ordinal);
        Assert.Contains("fallback: DENY", yaml, StringComparison.Ordinal);
        Assert.Empty(parsed.Rules);
        Assert.Equal(DecisionEffect.Deny, parsed.Fallback);
        Assert.Equal(DecisionEffect.Deny, Evaluate(parsed, "draft", 150000, false).Effect);
        Assert.Null(Evaluate(parsed, "draft", 150000, false).MatchedRule);
    }

    [Fact]
    public void Nested_groups_are_not_visual_supported()
    {
        var nested = new CompiledPolicy(
            "nested",
            1,
            1,
            "invoice.cancel",
            "invoice",
            [
                new CompiledRule(
                    "deep",
                    new ConditionNode.All([
                        new ConditionNode.Any([
                            new ConditionNode.Field("resource.attributes.status", "eq", "draft")
                        ])
                    ]),
                    DecisionEffect.Deny,
                    null,
                    null,
                    [])
            ],
            DecisionEffect.Deny);

        var document = PolicyEditorMapper.FromCompiled(nested, 2);
        Assert.False(document.VisualSupported);
        Assert.Contains("nested conditions", document.VisualUnsupportedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duration_format_round_trips_minutes()
    {
        Assert.Equal("15m", DurationParser.Format(DurationParser.Parse("15m")));
        Assert.Equal("2h", DurationParser.Format(DurationParser.Parse("2h")));
        Assert.Equal("1d", DurationParser.Format(DurationParser.Parse("1d")));
    }

    private PolicyEvaluation Evaluate(CompiledPolicy policy, string status, long amount, bool reported)
    {
        var attrs = new Dictionary<string, JsonElement>
        {
            ["status"] = JsonSerializer.SerializeToElement(status),
            ["amount"] = JsonSerializer.SerializeToElement(amount),
            ["reportedToTaxAuthority"] = JsonSerializer.SerializeToElement(reported)
        };
        var envelope = new AuthorizationEnvelope("invoice-agent", "production", "usr_481", "human", "invoice.cancel", "invoice", "FAC-1", attrs, new Dictionary<string, JsonElement>());
        return _engine.Evaluate(envelope, [policy]);
    }

    private static string FindPolicy()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "policies", "invoice-cancel-production.yaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "policies", "invoice-cancel-production.yaml"));
    }
}
