using System.Text.Json;
using Deleguard.Domain.Common;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class ConditionOperatorTests
{
    [Fact]
    public void Contains_matches_actor_roles()
    {
        var yaml = """
id: role-check
version: 1
priority: 1
match:
  action: admin.user.list
  resource_type: user
rules:
  - name: allow-admin
    when:
      field: actor.roles
      op: contains
      value: org-admin
    effect: ALLOW
fallback: DENY
""";
        var policy = PolicyYamlParser.Parse("role-check", yaml);
        var engine = new NativePolicyEngine();
        var allow = engine.Evaluate(Envelope(["org-admin"]), [policy]);
        var deny = engine.Evaluate(Envelope(["auditor"]), [policy]);
        var missing = engine.Evaluate(Envelope(null), [policy]);
        Assert.Equal(DecisionEffect.Allow, allow.Effect);
        Assert.Equal(DecisionEffect.Deny, deny.Effect);
        Assert.Equal(DecisionEffect.Deny, missing.Effect);
    }

    [Fact]
    public void In_matches_scalar_against_list()
    {
        var yaml = """
id: in-check
version: 1
priority: 1
match:
  action: admin.user.list
  resource_type: user
rules:
  - name: allow-known
    when:
      field: actor.id
      op: in
      value: [usr_1, usr_2]
    effect: ALLOW
fallback: DENY
""";
        var policy = PolicyYamlParser.Parse("in-check", yaml);
        var engine = new NativePolicyEngine();
        var allow = engine.Evaluate(Envelope(["org-admin"], "usr_1"), [policy]);
        var deny = engine.Evaluate(Envelope(["org-admin"], "usr_9"), [policy]);
        Assert.Equal(DecisionEffect.Allow, allow.Effect);
        Assert.Equal(DecisionEffect.Deny, deny.Effect);
    }

    private static AuthorizationEnvelope Envelope(IReadOnlyList<string>? roles, string actorId = "usr_1") =>
        new("admin-mcp", "production", actorId, "human", "admin.user.list", "user", "directory",
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), roles);
}
