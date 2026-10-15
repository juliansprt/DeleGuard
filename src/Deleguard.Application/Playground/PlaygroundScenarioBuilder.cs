using System.Globalization;
using System.Text.Json;
using Deleguard.Domain.Common;
using Deleguard.Policy;

namespace Deleguard.Application.Playground;

public sealed record PlaygroundScenario(
    string Label,
    string Effect,
    string ResourceId,
    Dictionary<string, JsonElement> Attributes);

public static class PlaygroundScenarioBuilder
{
    private static readonly NativePolicyEngine Engine = new();
    private static readonly DecisionEffect[] Effects =
    [
        DecisionEffect.Allow,
        DecisionEffect.RequireApproval,
        DecisionEffect.Deny
    ];

    public static IReadOnlyList<PlaygroundScenario> Build(
        IReadOnlyList<CompiledPolicy> policies,
        string action,
        string resourceType)
    {
        var applicable = policies
            .Where(policy => string.Equals(policy.Action, action, StringComparison.Ordinal)
                             && string.Equals(policy.ResourceType, resourceType, StringComparison.Ordinal))
            .ToList();
        if (applicable.Count == 0)
        {
            return [];
        }

        var scenarios = new List<PlaygroundScenario>();
        foreach (var effect in Effects)
        {
            if (TryScenario(applicable, action, resourceType, effect, out var scenario))
            {
                scenarios.Add(scenario);
            }
        }

        return scenarios;
    }

    public static PlaygroundScenario? Default(IReadOnlyList<PlaygroundScenario> scenarios) =>
        scenarios.FirstOrDefault(scenario => scenario.Effect == DecisionNames.Allow)
        ?? scenarios.FirstOrDefault();

    public static string DefaultResourceId(string resourceType) =>
        string.IsNullOrWhiteSpace(resourceType) ? "resource-1" : $"{resourceType.Trim()}-1";

    private static bool TryScenario(
        IReadOnlyList<CompiledPolicy> policies,
        string action,
        string resourceType,
        DecisionEffect effect,
        out PlaygroundScenario scenario)
    {
        foreach (var candidate in Candidates(policies, effect))
        {
            var bag = new AttributeBag();
            candidate.Apply(bag);
            NeutralizeHigher(policies, action, resourceType, effect, bag);
            var evaluation = Evaluate(policies, action, resourceType, bag);
            if (evaluation.Effect != effect)
            {
                continue;
            }

            scenario = new PlaygroundScenario(
                ChipLabel(effect),
                DecisionNames.ToApi(effect),
                bag.ResourceId ?? DefaultResourceId(resourceType),
                bag.ToAttributes());
            return true;
        }

        scenario = null!;
        return false;
    }

    private static IEnumerable<ICandidate> Candidates(IReadOnlyList<CompiledPolicy> policies, DecisionEffect effect)
    {
        foreach (var policy in policies.OrderByDescending(policy => policy.Priority))
        {
            foreach (var rule in policy.Rules.Where(rule => rule.Effect == effect))
            {
                yield return new RuleCandidate(rule);
            }

            if (policy.Fallback == effect)
            {
                yield return new FallbackCandidate();
            }
        }
    }

    private static void NeutralizeHigher(
        IReadOnlyList<CompiledPolicy> policies,
        string action,
        string resourceType,
        DecisionEffect target,
        AttributeBag bag)
    {
        var targetRank = DecisionNames.Precedence(target);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var envelope = Envelope(action, resourceType, bag);
            var changed = false;
            foreach (var policy in policies)
            {
                foreach (var rule in policy.Rules)
                {
                    if (DecisionNames.Precedence(rule.Effect) <= targetRank)
                    {
                        continue;
                    }

                    if (!ConditionEvaluator.Matches(rule.When, envelope))
                    {
                        continue;
                    }

                    if (!Mutator.Violate(rule.When, bag))
                    {
                        continue;
                    }

                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }
        }
    }

    private static PolicyEvaluation Evaluate(
        IReadOnlyList<CompiledPolicy> policies,
        string action,
        string resourceType,
        AttributeBag bag) =>
        Engine.Evaluate(Envelope(action, resourceType, bag), policies);

    private static AuthorizationEnvelope Envelope(string action, string resourceType, AttributeBag bag) =>
        new(
            "sample-agent",
            "production",
            null,
            null,
            action,
            resourceType,
            bag.ResourceId ?? DefaultResourceId(resourceType),
            bag.ToAttributes(),
            new Dictionary<string, JsonElement>());

    private static string ChipLabel(DecisionEffect effect) => effect switch
    {
        DecisionEffect.RequireApproval => "APPROVAL",
        DecisionEffect.Allow => "ALLOW",
        _ => "DENY"
    };

    private interface ICandidate
    {
        void Apply(AttributeBag bag);
    }

    private sealed class RuleCandidate(CompiledRule rule) : ICandidate
    {
        public void Apply(AttributeBag bag) => Mutator.Satisfy(rule.When, bag);
    }

    private sealed class FallbackCandidate : ICandidate
    {
        public void Apply(AttributeBag bag)
        {
        }
    }

    private sealed class AttributeBag
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public string? ResourceId { get; set; }

        public void SetAttribute(string key, object? value) => _values[key] = value;

        public void RemoveAttribute(string key) => _values.Remove(key);

        public Dictionary<string, JsonElement> ToAttributes()
        {
            var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (key, value) in _values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                attributes[key] = ToElement(value);
            }

            return attributes;
        }
    }

    private static class Mutator
    {
        public static bool Satisfy(ConditionNode node, AttributeBag bag) => node switch
        {
            ConditionNode.Always => true,
            ConditionNode.All all => all.Items.All(item => Satisfy(item, bag)),
            ConditionNode.Any any => any.Items.Count == 0 || any.Items.Take(1).All(item => Satisfy(item, bag)),
            ConditionNode.Field field => ApplyField(field, bag, wantMatch: true),
            _ => false
        };

        public static bool Violate(ConditionNode node, AttributeBag bag) => node switch
        {
            ConditionNode.Always => false,
            ConditionNode.All all => all.Items.Count > 0 && Violate(all.Items[0], bag),
            ConditionNode.Any any => any.Items.All(item => Violate(item, bag)),
            ConditionNode.Field field => ApplyField(field, bag, wantMatch: false),
            _ => false
        };

        private static bool ApplyField(ConditionNode.Field field, AttributeBag bag, bool wantMatch)
        {
            if (field.Path.StartsWith("resource.attributes.", StringComparison.Ordinal))
            {
                var key = field.Path["resource.attributes.".Length..];
                if (string.IsNullOrWhiteSpace(key))
                {
                    return false;
                }

                ApplyAttribute(key, field.Op, field.Value, bag, wantMatch);
                return true;
            }

            if (string.Equals(field.Path, "resource.id", StringComparison.Ordinal))
            {
                var text = Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? "sample";
                bag.ResourceId = wantMatch && string.Equals(field.Op, "eq", StringComparison.OrdinalIgnoreCase)
                    ? text
                    : $"{text}-other";
                return true;
            }

            return true;
        }

        private static void ApplyAttribute(string key, string op, object? expected, AttributeBag bag, bool wantMatch)
        {
            var normalized = op.ToLowerInvariant();
            switch (normalized)
            {
                case "exists":
                    if (wantMatch)
                    {
                        bag.SetAttribute(key, "present");
                    }
                    else
                    {
                        bag.RemoveAttribute(key);
                    }

                    return;
                case "eq":
                    bag.SetAttribute(key, wantMatch ? expected : Alternate(expected));
                    return;
                case "ne":
                    bag.SetAttribute(key, wantMatch ? Alternate(expected) : expected);
                    return;
                case "gt":
                    bag.SetAttribute(key, wantMatch ? ShiftNumber(expected, 1) : expected);
                    return;
                case "gte":
                    bag.SetAttribute(key, wantMatch ? expected : ShiftNumber(expected, -1));
                    return;
                case "lt":
                    bag.SetAttribute(key, wantMatch ? ShiftNumber(expected, -1) : expected);
                    return;
                case "lte":
                    bag.SetAttribute(key, wantMatch ? expected : ShiftNumber(expected, 1));
                    return;
                case "in":
                    bag.SetAttribute(key, wantMatch ? FirstListItem(expected) : "other");
                    return;
                case "contains":
                    var needle = Convert.ToString(expected, CultureInfo.InvariantCulture) ?? "x";
                    bag.SetAttribute(key, wantMatch ? needle : "x");
                    return;
                default:
                    if (wantMatch)
                    {
                        bag.SetAttribute(key, expected);
                    }

                    return;
            }
        }

        private static object? FirstListItem(object? expected)
        {
            if (expected is IEnumerable<string> items)
            {
                return items.FirstOrDefault() ?? "sample";
            }

            return expected ?? "sample";
        }

        private static object Alternate(object? expected) => expected switch
        {
            bool flag => !flag,
            byte or sbyte or short or ushort or int or uint or long or ulong => ShiftNumber(expected, 1),
            float or double or decimal => ShiftNumber(expected, 1),
            string text when string.Equals(text, "other", StringComparison.Ordinal) => "other-2",
            _ => "other"
        };

        private static object ShiftNumber(object? expected, int delta)
        {
            if (expected is IConvertible)
            {
                try
                {
                    var number = Convert.ToDecimal(expected, CultureInfo.InvariantCulture) + delta;
                    if (expected is long or int or uint or short or ushort or byte or sbyte)
                    {
                        return decimal.ToInt64(number);
                    }

                    return number;
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    return delta;
                }
            }

            return delta;
        }
    }

    private static JsonElement ToElement(object? value) => value switch
    {
        null => JsonSerializer.SerializeToElement((string?)null),
        JsonElement element => element.Clone(),
        bool flag => JsonSerializer.SerializeToElement(flag),
        byte or sbyte or short or ushort or int or uint or long or ulong =>
            JsonSerializer.SerializeToElement(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        float or double or decimal =>
            JsonSerializer.SerializeToElement(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
        string text => JsonSerializer.SerializeToElement(text),
        IEnumerable<string> items => JsonSerializer.SerializeToElement(items.ToList()),
        _ => JsonSerializer.SerializeToElement(Convert.ToString(value, CultureInfo.InvariantCulture))
    };
}
