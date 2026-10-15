using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Deleguard.Policy;

public static class PolicyYamlParser
{
    public static CompiledPolicy Parse(string policyId, string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0)
        {
            throw new InvalidOperationException("Policy YAML is empty.");
        }

        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var slug = GetString(root, "id") ?? policyId;
        var version = GetInt(root, "version") ?? 1;
        var priority = GetInt(root, "priority") ?? 0;
        var match = GetMap(root, "match") ?? throw new InvalidOperationException("Policy match is required.");
        var action = GetString(match, "action") ?? throw new InvalidOperationException("Policy match.action is required.");
        var resourceType = GetString(match, "resource_type") ?? throw new InvalidOperationException("Policy match.resource_type is required.");
        var fallback = ParseEffect(GetString(root, "fallback") ?? "DENY");

        var rules = new List<CompiledRule>();
        if (root.Children.TryGetValue(new YamlScalarNode("rules"), out var rulesNode) && rulesNode is YamlSequenceNode sequence)
        {
            foreach (var item in sequence)
            {
                rules.Add(ParseRule((YamlMappingNode)item));
            }
        }

        return new CompiledPolicy(slug, version, priority, action, resourceType, rules, fallback);
    }

    private static CompiledRule ParseRule(YamlMappingNode node)
    {
        var name = GetString(node, "name") ?? "unnamed";
        var effect = ParseEffect(GetString(node, "effect") ?? "DENY");
        var reason = GetString(node, "reason");
        var whenNode = GetChild(node, "when");
        var when = whenNode is null ? new ConditionNode.Always() : ParseCondition(whenNode);
        TimeSpan? expires = null;
        var roles = new List<string>();
        var approval = GetMap(node, "approval");
        if (approval is not null)
        {
            var expiresIn = GetString(approval, "expires_in");
            if (!string.IsNullOrWhiteSpace(expiresIn))
            {
                expires = Domain.Common.DurationParser.Parse(expiresIn);
            }

            if (GetChild(approval, "approver_roles") is YamlSequenceNode roleNodes)
            {
                foreach (var role in roleNodes)
                {
                    roles.Add(((YamlScalarNode)role).Value ?? string.Empty);
                }
            }
        }

        return new CompiledRule(name, when, effect, reason, expires, roles);
    }

    private static ConditionNode ParseCondition(YamlNode node)
    {
        if (node is not YamlMappingNode map)
        {
            return new ConditionNode.Always();
        }

        if (GetChild(map, "all") is YamlSequenceNode all)
        {
            return new ConditionNode.All(all.Select(ParseCondition).ToList());
        }

        if (GetChild(map, "any") is YamlSequenceNode any)
        {
            return new ConditionNode.Any(any.Select(ParseCondition).ToList());
        }

        var field = GetString(map, "field");
        var op = GetString(map, "op") ?? "eq";
        if (string.IsNullOrWhiteSpace(field))
        {
            return new ConditionNode.Always();
        }

        object? value = null;
        var valueNode = GetChild(map, "value");
        if (valueNode is YamlScalarNode scalar)
        {
            value = ParseScalar(scalar);
        }
        else if (valueNode is YamlSequenceNode sequence)
        {
            value = sequence.Children.OfType<YamlScalarNode>().Select(ParseScalar).Select(item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty).ToList();
        }

        return new ConditionNode.Field(field, op, value);
    }

    private static object? ParseScalar(YamlScalarNode scalar)
    {
        var raw = scalar.Value;
        if (raw is null)
        {
            return null;
        }

        if (bool.TryParse(raw, out var b))
        {
            return b;
        }

        if (long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }

        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        return raw;
    }

    private static Domain.Common.DecisionEffect ParseEffect(string value) =>
        Domain.Common.DecisionNames.Parse(value);

    private static YamlNode? GetChild(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static YamlMappingNode? GetMap(YamlMappingNode map, string key) =>
        GetChild(map, key) as YamlMappingNode;

    private static string? GetString(YamlMappingNode map, string key) =>
        GetChild(map, key) is YamlScalarNode scalar ? scalar.Value : null;

    private static int? GetInt(YamlMappingNode map, string key)
    {
        var value = GetString(map, key);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }
}
