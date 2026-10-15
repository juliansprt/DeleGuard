using System.Text.Json;
using Deleguard.Application.Playground;
using Deleguard.Domain.Common;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class PlaygroundScenarioBuilderTests
{
    [Fact]
    public void Invoice_policy_yields_allow_approval_and_deny_that_evaluate()
    {
        var policy = Load("invoice-cancel-production.yaml");
        var scenarios = PlaygroundScenarioBuilder.Build([policy], "invoice.cancel", "invoice");

        Assert.Equal(["ALLOW", "REQUIRE_APPROVAL", "DENY"], scenarios.Select(scenario => scenario.Effect));
        Assert.All(scenarios, scenario => Assert.NotEqual("FAC-DRAFT", scenario.ResourceId));
        AssertEvaluates(policy, "invoice.cancel", "invoice", scenarios);
    }

    [Fact]
    public void Llm_complete_policy_yields_working_attribute_json()
    {
        var policy = Load("llm-complete-production.yaml");
        var scenarios = PlaygroundScenarioBuilder.Build([policy], "llm.complete", "model");

        Assert.Contains(scenarios, scenario => scenario.Effect == "ALLOW");
        Assert.Contains(scenarios, scenario => scenario.Effect == "REQUIRE_APPROVAL");
        Assert.Contains(scenarios, scenario => scenario.Effect == "DENY");
        var allow = scenarios.First(scenario => scenario.Effect == "ALLOW");
        Assert.True(allow.Attributes["in_catalog"].GetBoolean());
        Assert.Contains(allow.Attributes["model_tier"].GetString(), new[] { "cheap", "strong" });
        AssertEvaluates(policy, "llm.complete", "model", scenarios);
    }

    [Fact]
    public void Unknown_action_has_no_invoice_fixture()
    {
        var policy = Load("llm-complete-production.yaml");
        Assert.Empty(PlaygroundScenarioBuilder.Build([policy], "pendulo.budget.update", "budget"));
    }

    private static CompiledPolicy Load(string fileName)
    {
        var yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "policies", fileName));
        return PolicyYamlParser.Parse(Path.GetFileNameWithoutExtension(fileName), yaml);
    }

    private static void AssertEvaluates(
        CompiledPolicy policy,
        string action,
        string resourceType,
        IReadOnlyList<PlaygroundScenario> scenarios)
    {
        var engine = new NativePolicyEngine();
        foreach (var scenario in scenarios)
        {
            var evaluation = engine.Evaluate(
                new AuthorizationEnvelope(
                    "sample-agent",
                    "production",
                    null,
                    null,
                    action,
                    resourceType,
                    scenario.ResourceId,
                    scenario.Attributes,
                    new Dictionary<string, JsonElement>()),
                [policy]);
            Assert.Equal(scenario.Effect, DecisionNames.ToApi(evaluation.Effect));
        }
    }
}
