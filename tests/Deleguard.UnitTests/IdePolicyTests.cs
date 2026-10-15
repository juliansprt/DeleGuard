using System.Text.Json;
using Deleguard.Domain.Common;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class IdePolicyTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "policies", "fixtures"), "ide-*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var fixture in document.RootElement.EnumerateArray())
                yield return [Path.GetFileNameWithoutExtension(path), fixture.GetProperty("name").GetString()!, fixture.GetRawText()];
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixtures_match_deterministic_policy(string policyName, string name, string fixtureJson)
    {
        using var fixture = JsonDocument.Parse(fixtureJson);
        var request = fixture.RootElement.GetProperty("request");
        var policy = PolicyYamlParser.Parse(policyName,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "policies", policyName + ".yaml")));
        var envelope = new AuthorizationEnvelope("ide-agent", "development", "developer", "human",
            request.GetProperty("action").GetString()!, request.GetProperty("resource_type").GetString()!,
            request.GetProperty("resource_id").GetString()!,
            request.GetProperty("attributes").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone()),
            new Dictionary<string, JsonElement>());
        var result = new NativePolicyEngine().Evaluate(envelope, [policy]);
        Assert.True(result.Effect == DecisionNames.Parse(fixture.RootElement.GetProperty("expected").GetString()!), name);
        if (fixture.RootElement.TryGetProperty("expected_rule", out var expectedRule))
            Assert.Equal(expectedRule.GetString(), result.MatchedRule);
        Assert.Equal(DecisionEffect.Deny, policy.Fallback);
        if (result.Effect == DecisionEffect.RequireApproval)
        {
            Assert.Equal(TimeSpan.FromMinutes(15), result.ApprovalExpiresIn);
            Assert.Contains("org-admin", result.ApproverRoles);
        }
    }

    [Fact]
    public void Library_covers_every_independent_action()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "policies"), "ide-*.yaml");
        Assert.Equal(22, files.Length);
        var actions = files.Select(path => PolicyYamlParser.Parse(path, File.ReadAllText(path)).Action).ToHashSet();
        foreach (var verb in new[] { "read", "list", "search", "create", "write", "delete", "move" })
            Assert.Contains("filesystem." + verb, actions);
        Assert.Contains("process.execute", actions);
        Assert.Contains("mcp.call", actions);
        foreach (var action in new[]
        {
            "network.connect", "git.clone", "git.fetch", "git.commit", "git.push",
            "package.install", "service.manage", "credential.use", "mcp.discover",
            "ide.settings.change", "extension.install", "debug.attach", "browser.navigate"
        })
            Assert.Contains(action, actions);
    }
}
