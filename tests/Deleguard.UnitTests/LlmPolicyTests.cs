using System.Text.Json;
using Deleguard.Domain.Common;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class LlmPolicyTests
{
    private readonly NativePolicyEngine _engine = new();

    [Fact]
    public void Cheap_catalog_model_is_allow()
    {
        var result = EvaluateComplete(true, "cheap", 0.002m, false);
        Assert.Equal(DecisionEffect.Allow, result.Effect);
        Assert.Equal("allow-catalog-chat", result.MatchedRule);
    }

    [Fact]
    public void High_estimated_cost_requires_approval()
    {
        var result = EvaluateComplete(true, "strong", 2.5m, false);
        Assert.Equal(DecisionEffect.RequireApproval, result.Effect);
        Assert.Equal("approval-expensive", result.MatchedRule);
    }

    [Fact]
    public void Unknown_model_is_deny()
    {
        var result = EvaluateComplete(false, "cheap", 0.01m, false, "gpt-secret");
        Assert.Equal(DecisionEffect.Deny, result.Effect);
        Assert.Equal("deny-unknown-model", result.MatchedRule);
    }

    [Fact]
    public void Over_budget_is_deny()
    {
        var result = EvaluateComplete(true, "cheap", 0.01m, true);
        Assert.Equal(DecisionEffect.Deny, result.Effect);
        Assert.Equal("deny-over-budget", result.MatchedRule);
    }

    [Fact]
    public void Unknown_tier_is_fallback_deny()
    {
        var result = EvaluateComplete(true, "frontier", 0.01m, false, "chat.frontier");
        Assert.Equal(DecisionEffect.Deny, result.Effect);
        Assert.Null(result.MatchedRule);
    }

    [Fact]
    public void Complete_fixtures_match_engine()
    {
        AssertFixtures("llm-complete.json", "llm-complete-production.yaml", "llm.complete");
    }

    [Fact]
    public void Embed_catalog_is_allow()
    {
        var policy = Parse("llm-embed-production.yaml", "llm.embed");
        var result = _engine.Evaluate(Envelope("llm.embed", "embed.default", true, "embed", 0.0001m, false), [policy]);
        Assert.Equal(DecisionEffect.Allow, result.Effect);
    }

    [Fact]
    public void Embed_unknown_is_deny()
    {
        var policy = Parse("llm-embed-production.yaml", "llm.embed");
        var result = _engine.Evaluate(Envelope("llm.embed", "mystery", false, "embed", 0.0001m, false), [policy]);
        Assert.Equal(DecisionEffect.Deny, result.Effect);
        Assert.Equal("deny-unknown-model", result.MatchedRule);
    }

    [Fact]
    public void Embed_wrong_tier_is_fallback_deny()
    {
        var policy = Parse("llm-embed-production.yaml", "llm.embed");
        var result = _engine.Evaluate(Envelope("llm.embed", "chat.cheap", true, "cheap", 0.0001m, false), [policy]);
        Assert.Equal(DecisionEffect.Deny, result.Effect);
        Assert.Null(result.MatchedRule);
    }

    [Fact]
    public void Embed_fixtures_match_engine()
    {
        AssertFixtures("llm-embed.json", "llm-embed-production.yaml", "llm.embed");
    }

    private PolicyEvaluation EvaluateComplete(bool inCatalog, string tier, decimal cost, bool overBudget, string resourceId = "chat.cheap")
    {
        var policy = Parse("llm-complete-production.yaml", "llm.complete");
        return _engine.Evaluate(Envelope("llm.complete", resourceId, inCatalog, tier, cost, overBudget), [policy]);
    }

    private void AssertFixtures(string fixtureFile, string yamlFile, string action)
    {
        var policy = Parse(yamlFile, action);
        using var document = JsonDocument.Parse(File.ReadAllText(Find("fixtures", fixtureFile)));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var expected = item.GetProperty("expected").GetString()!;
            seen.Add(expected);
            var request = item.GetProperty("request");
            var attrs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in request.GetProperty("attributes").EnumerateObject())
            {
                attrs[property.Name] = property.Value.Clone();
            }

            var envelope = new AuthorizationEnvelope(
                "invoice-agent",
                "production",
                "svc",
                "service",
                request.GetProperty("action").GetString()!,
                request.GetProperty("resource_type").GetString()!,
                request.GetProperty("resource_id").GetString()!,
                attrs,
                new Dictionary<string, JsonElement>());
            var result = _engine.Evaluate(envelope, [policy]);
            Assert.Equal(DecisionNames.Parse(expected), result.Effect);
        }

        Assert.Contains("ALLOW", seen);
        Assert.Contains("DENY", seen);
        if (string.Equals(action, "llm.complete", StringComparison.Ordinal))
        {
            Assert.Contains("REQUIRE_APPROVAL", seen);
        }
    }

    private static CompiledPolicy Parse(string file, string action) =>
        PolicyYamlParser.Parse(action, File.ReadAllText(Find(null, file)));

    private static AuthorizationEnvelope Envelope(
        string action,
        string resourceId,
        bool inCatalog,
        string tier,
        decimal cost,
        bool overBudget) =>
        new(
            "invoice-agent",
            "production",
            "svc",
            "service",
            action,
            "model",
            resourceId,
            new Dictionary<string, JsonElement>
            {
                ["in_catalog"] = JsonSerializer.SerializeToElement(inCatalog),
                ["model_tier"] = JsonSerializer.SerializeToElement(tier),
                ["estimated_cost_usd"] = JsonSerializer.SerializeToElement(cost),
                ["over_budget"] = JsonSerializer.SerializeToElement(overBudget)
            },
            new Dictionary<string, JsonElement>());

    private static string Find(string? folder, string fileName)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = folder is null
                ? Path.Combine(dir, "policies", fileName)
                : Path.Combine(dir, "policies", folder, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "policies",
            folder ?? string.Empty,
            fileName));
    }
}
