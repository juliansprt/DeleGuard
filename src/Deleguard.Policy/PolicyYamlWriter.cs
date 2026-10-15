using System.Globalization;
using Deleguard.Domain.Common;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Deleguard.Policy;

public static class PolicyYamlWriter
{
    public static string Write(PolicyEditorDocument document) =>
        Write(PolicyEditorMapper.ToCompiled(document));

    public static string Write(CompiledPolicy policy)
    {
        var root = new YamlMappingNode();
        Add(root, "id", policy.PolicyId);
        Add(root, "version", policy.Version.ToString(CultureInfo.InvariantCulture));
        Add(root, "priority", policy.Priority.ToString(CultureInfo.InvariantCulture));

        var match = new YamlMappingNode();
        Add(match, "action", policy.Action);
        Add(match, "resource_type", policy.ResourceType);
        root.Add("match", match);

        var rules = new YamlSequenceNode { Style = SequenceStyle.Block };
        foreach (var rule in policy.Rules)
        {
            rules.Add(WriteRule(rule));
        }

        root.Add("rules", rules);
        Add(root, "fallback", DecisionNames.ToApi(policy.Fallback));

        var stream = new YamlStream(new YamlDocument(root));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.Save(writer, assignAnchors: false);
        var yaml = writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (yaml.EndsWith("...\n", StringComparison.Ordinal))
        {
            yaml = yaml[..^4];
        }
        else if (yaml.EndsWith("...", StringComparison.Ordinal))
        {
            yaml = yaml[..^3];
        }

        return yaml.TrimEnd() + "\n";
    }

    private static YamlMappingNode WriteRule(CompiledRule rule)
    {
        var node = new YamlMappingNode();
        Add(node, "name", rule.Name);
        if (rule.When is not ConditionNode.Always)
        {
            node.Add("when", WriteCondition(rule.When));
        }

        Add(node, "effect", DecisionNames.ToApi(rule.Effect));
        if (!string.IsNullOrWhiteSpace(rule.Reason))
        {
            Add(node, "reason", rule.Reason);
        }

        if (rule.Effect == DecisionEffect.RequireApproval)
        {
            var approval = new YamlMappingNode();
            if (rule.ApprovalExpiresIn is { } expires)
            {
                Add(approval, "expires_in", DurationParser.Format(expires));
            }

            if (rule.ApproverRoles.Count > 0)
            {
                var roles = new YamlSequenceNode { Style = SequenceStyle.Flow };
                foreach (var role in rule.ApproverRoles)
                {
                    roles.Add(new YamlScalarNode(role));
                }

                approval.Add("approver_roles", roles);
            }

            node.Add("approval", approval);
        }

        return node;
    }

    private static YamlNode WriteCondition(ConditionNode node) => node switch
    {
        ConditionNode.Field field => WriteField(field),
        ConditionNode.All all => new YamlMappingNode
        {
            { "all", new YamlSequenceNode(all.Items.Select(WriteCondition)) { Style = SequenceStyle.Block } }
        },
        ConditionNode.Any any => new YamlMappingNode
        {
            { "any", new YamlSequenceNode(any.Items.Select(WriteCondition)) { Style = SequenceStyle.Block } }
        },
        _ => new YamlMappingNode()
    };

    private static YamlMappingNode WriteField(ConditionNode.Field field)
    {
        var node = new YamlMappingNode();
        Add(node, "field", field.Path);
        Add(node, "op", string.IsNullOrWhiteSpace(field.Op) ? "eq" : field.Op);
        if (!string.Equals(field.Op, "exists", StringComparison.OrdinalIgnoreCase))
        {
            node.Add("value", WriteValue(field.Value));
        }

        return node;
    }

    private static YamlNode WriteValue(object? value)
    {
        if (value is IEnumerable<string> items)
        {
            var sequence = new YamlSequenceNode { Style = SequenceStyle.Flow };
            foreach (var item in items)
            {
                sequence.Add(new YamlScalarNode(item));
            }

            return sequence;
        }

        return Scalar(value);
    }

    private static void Add(YamlMappingNode map, string key, string value) =>
        map.Add(new YamlScalarNode(key), new YamlScalarNode(value));

    private static YamlScalarNode Scalar(object? value)
    {
        switch (value)
        {
            case null:
                return new YamlScalarNode("null") { Style = ScalarStyle.Plain };
            case bool flag:
                return new YamlScalarNode(flag ? "true" : "false") { Style = ScalarStyle.Plain };
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return new YamlScalarNode(Convert.ToString(value, CultureInfo.InvariantCulture)) { Style = ScalarStyle.Plain };
            case float or double or decimal:
                return new YamlScalarNode(Convert.ToString(value, CultureInfo.InvariantCulture)) { Style = ScalarStyle.Plain };
            default:
                return new YamlScalarNode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }
}
