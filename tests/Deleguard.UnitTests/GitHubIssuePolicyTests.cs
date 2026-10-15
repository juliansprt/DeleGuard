using System.Text.Json;
using Deleguard.Domain.Common;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class GitHubIssuePolicyTests
{
    private readonly NativePolicyEngine _engine = new();
    private readonly CompiledPolicy _policy = PolicyYamlParser.Parse("github-issue",
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "policies", "github-issue.yaml")));

    public static IEnumerable<object[]> Fixtures()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "policies", "fixtures", "github-issue.json")));
        return document.RootElement.EnumerateArray()
            .Select(item => new object[] { item.GetProperty("name").GetString()!, item.GetRawText() })
            .ToArray();
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Reference_fixtures_match_expected_decisions(string name, string fixtureJson)
    {
        using var document = JsonDocument.Parse(fixtureJson);
        var fixture = document.RootElement;
        var result = _engine.Evaluate(Envelope(fixture.GetProperty("request")), [_policy]);
        Assert.True(DecisionNames.ToApi(result.Effect) == fixture.GetProperty("expected").GetString(),
            $"Fixture '{name}': received {result.Effect}. {result.Reason}");
        Assert.Equal(fixture.GetProperty("expected_rule").GetString(), result.MatchedRule);
        if (result.Effect == DecisionEffect.RequireApproval)
        {
            Assert.Equal(TimeSpan.FromMinutes(15), result.ApprovalExpiresIn);
            Assert.Equal(new[] { "org-admin" }, result.ApproverRoles);
        }
    }

    [Fact]
    public void Fixture_pack_covers_all_outcomes_with_unique_names()
    {
        var fixtures = Fixtures().ToArray();
        Assert.Equal(fixtures.Length, fixtures.Select(x => (string)x[0]).Distinct(StringComparer.Ordinal).Count());
        var effects = fixtures.Select(x =>
        {
            using var doc = JsonDocument.Parse((string)x[1]);
            return doc.RootElement.GetProperty("expected").GetString();
        }).ToHashSet(StringComparer.Ordinal);
        Assert.True(effects.SetEquals(["ALLOW", "DENY", "REQUIRE_APPROVAL"]));
    }

    [Theory]
    [InlineData("providerHost")]
    [InlineData("repositoryEnvironment")]
    [InlineData("visibility")]
    [InlineData("archived")]
    [InlineData("issuesEnabled")]
    public void Missing_or_null_trusted_facts_deny_both_allowed_destinations(string attribute)
    {
        foreach (var name in new[] { "private-sandbox-allow", "private-pilot-approval" })
        {
            var envelope = FixtureEnvelope(name);
            var attributes = envelope.ResourceAttributes.ToDictionary(x => x.Key, x => x.Value);
            attributes.Remove(attribute);
            Assert.Equal(DecisionEffect.Deny,
                _engine.Evaluate(envelope with { ResourceAttributes = attributes }, [_policy]).Effect);
            attributes[attribute] = JsonSerializer.SerializeToElement<object?>(null);
            Assert.Equal(DecisionEffect.Deny,
                _engine.Evaluate(envelope with { ResourceAttributes = attributes }, [_policy]).Effect);
        }
    }

    [Theory]
    [InlineData("production-agent-deny")]
    [InlineData("production-repository-deny")]
    [InlineData("foreign-host-deny")]
    [InlineData("public-repository-deny")]
    [InlineData("archived-pilot-deny")]
    [InlineData("disabled-issues-deny")]
    [InlineData("unknown-repository-deny")]
    public void Deny_wins_over_a_competing_allow_policy_even_with_higher_priority(string fixtureName)
    {
        var competing = PolicyYamlParser.Parse("competing", """
            id: competing
            version: 1
            priority: 999
            match:
              action: github.issue.create
              resource_type: repository
            rules:
              - name: broad-allow
                effect: ALLOW
            fallback: DENY
            """);
        Assert.Equal(DecisionEffect.Deny,
            _engine.Evaluate(FixtureEnvelope(fixtureName), [competing, _policy]).Effect);
    }

    [Fact]
    public void Explicit_deny_beats_allow_within_the_same_policy_regardless_of_order()
    {
        var policyWithBroadAllow = _policy with
        {
            Rules = [new CompiledRule("broad-allow", new ConditionNode.Always(), DecisionEffect.Allow,
                null, null, []), .. _policy.Rules]
        };
        var result = _engine.Evaluate(FixtureEnvelope("archived-pilot-deny"), [policyWithBroadAllow]);
        Assert.Equal(DecisionEffect.Deny, result.Effect);
        Assert.Equal("deny-archived", result.MatchedRule);
    }

    private static AuthorizationEnvelope FixtureEnvelope(string name)
    {
        var fixture = Fixtures().Single(x => (string)x[0] == name);
        using var document = JsonDocument.Parse((string)fixture[1]);
        return Envelope(document.RootElement.GetProperty("request"));
    }

    private static AuthorizationEnvelope Envelope(JsonElement request) => new(
        "github-pilot-agent", request.GetProperty("environment").GetString()!, "pilot-operator", "service",
        request.GetProperty("action").GetString()!, request.GetProperty("resource_type").GetString()!,
        request.GetProperty("resource_id").GetString()!,
        request.GetProperty("attributes").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone()),
        request.TryGetProperty("context", out var context)
            ? context.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone())
            : new Dictionary<string, JsonElement>());
}
