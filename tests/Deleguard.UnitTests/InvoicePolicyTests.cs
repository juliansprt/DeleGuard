using System.Text.Json;
using Deleguard.Domain.Common;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class InvoicePolicyTests
{
    private readonly NativePolicyEngine _engine = new();
    private readonly CompiledPolicy _policy = PolicyYamlParser.Parse(
        "invoice-cancel-production",
        File.ReadAllText(FindPolicy()));

    [Fact]
    public void Draft_low_amount_is_allow()
    {
        var result = Evaluate("draft", 5_000_000, false);
        Assert.Equal(DecisionEffect.Allow, result.Effect);
        Assert.Equal("allow-draft-low-risk", result.MatchedRule);
    }

    [Fact]
    public void High_value_requires_approval()
    {
        var result = Evaluate("issued", 35_000_000, false);
        Assert.Equal(DecisionEffect.RequireApproval, result.Effect);
        Assert.Equal("high-value-approval", result.MatchedRule);
    }

    [Fact]
    public void Tax_reported_is_deny()
    {
        var result = Evaluate("issued", 1_000, true);
        Assert.Equal(DecisionEffect.Deny, result.Effect);
        Assert.Equal("never-cancel-tax-reported", result.MatchedRule);
    }

    [Fact]
    public void Tax_reported_beats_high_value()
    {
        var result = Evaluate("issued", 35_000_000, true);
        Assert.Equal(DecisionEffect.Deny, result.Effect);
    }

    [Fact]
    public void Issued_low_value_falls_back_to_deny()
    {
        var result = Evaluate("issued", 100, false);
        Assert.Equal(DecisionEffect.Deny, result.Effect);
    }

    [Fact]
    public void Unknown_action_is_denied()
    {
        var envelope = Envelope("sql.delete", "issued", 1, false);
        var result = _engine.Evaluate(envelope, [_policy]);
        Assert.Equal(DecisionEffect.Deny, result.Effect);
        Assert.Equal("No applicable policy.", result.Reason);
    }

    [Fact]
    public void Missing_amount_does_not_match_gt()
    {
        var attrs = new Dictionary<string, JsonElement>
        {
            ["status"] = JsonSerializer.SerializeToElement("draft")
        };
        var envelope = new AuthorizationEnvelope("invoice-agent", "production", "usr", "human", "invoice.cancel", "invoice", "FAC-1", attrs, new Dictionary<string, JsonElement>());
        var result = _engine.Evaluate(envelope, [_policy]);
        Assert.Equal(DecisionEffect.Deny, result.Effect);
    }

    private PolicyEvaluation Evaluate(string status, long amount, bool reported)
    {
        return _engine.Evaluate(Envelope("invoice.cancel", status, amount, reported), [_policy]);
    }

    private static AuthorizationEnvelope Envelope(string action, string status, long amount, bool reported)
    {
        var attrs = new Dictionary<string, JsonElement>
        {
            ["status"] = JsonSerializer.SerializeToElement(status),
            ["amount"] = JsonSerializer.SerializeToElement(amount),
            ["reportedToTaxAuthority"] = JsonSerializer.SerializeToElement(reported)
        };
        return new AuthorizationEnvelope("invoice-agent", "production", "usr_481", "human", action, "invoice", "FAC-1", attrs, new Dictionary<string, JsonElement>());
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
