using Deleguard.Domain.Common;

namespace Deleguard.Policy;

public sealed record PolicyEditorDocument(
    string Id,
    int Version,
    int Priority,
    string Action,
    string ResourceType,
    string Fallback,
    IReadOnlyList<PolicyEditorRule> Rules,
    bool VisualSupported,
    string? VisualUnsupportedReason,
    int NextVersion);

public sealed record PolicyEditorRule(
    string Name,
    string Effect,
    string? Reason,
    string WhenKind,
    string? Field,
    string? Op,
    object? Value,
    IReadOnlyList<PolicyEditorField> Conditions,
    string? ApprovalExpiresIn,
    IReadOnlyList<string> ApproverRoles);

public sealed record PolicyEditorField(string Field, string Op, object? Value);

public static class PolicyEditorMapper
{
    public static PolicyEditorDocument FromCompiled(CompiledPolicy policy, int nextVersion)
    {
        var unsupported = new List<string>();
        var rules = policy.Rules.Select(rule => FromRule(rule, unsupported)).ToList();
        var visualSupported = unsupported.Count == 0;
        return new PolicyEditorDocument(
            policy.PolicyId,
            policy.Version,
            policy.Priority,
            policy.Action,
            policy.ResourceType,
            DecisionNames.ToApi(policy.Fallback),
            rules,
            visualSupported,
            visualSupported ? null : string.Join(" ", unsupported.Distinct()),
            nextVersion);
    }

    public static CompiledPolicy ToCompiled(PolicyEditorDocument document)
    {
        var fallback = string.IsNullOrWhiteSpace(document.Fallback)
            ? DecisionEffect.Deny
            : DecisionNames.Parse(document.Fallback);
        var rules = document.Rules.Select(ToRule).ToList();
        return new CompiledPolicy(
            document.Id,
            document.Version,
            document.Priority,
            document.Action,
            document.ResourceType,
            rules,
            fallback);
    }

    private static PolicyEditorRule FromRule(CompiledRule rule, List<string> unsupported)
    {
        var (kind, field, op, value, conditions) = FromCondition(rule.When, unsupported, rule.Name);
        var expires = rule.ApprovalExpiresIn is { } span ? DurationParser.Format(span) : null;
        return new PolicyEditorRule(
            rule.Name,
            DecisionNames.ToApi(rule.Effect),
            rule.Reason,
            kind,
            field,
            op,
            value,
            conditions,
            expires,
            rule.ApproverRoles);
    }

    private static CompiledRule ToRule(PolicyEditorRule rule)
    {
        var effect = string.IsNullOrWhiteSpace(rule.Effect)
            ? DecisionEffect.Deny
            : DecisionNames.Parse(rule.Effect);
        TimeSpan? expires = null;
        var roles = new List<string>();
        if (effect == DecisionEffect.RequireApproval)
        {
            if (!string.IsNullOrWhiteSpace(rule.ApprovalExpiresIn))
            {
                expires = DurationParser.Parse(rule.ApprovalExpiresIn);
            }

            roles.AddRange(rule.ApproverRoles.Where(role => !string.IsNullOrWhiteSpace(role)));
        }

        return new CompiledRule(
            string.IsNullOrWhiteSpace(rule.Name) ? "unnamed" : rule.Name,
            ToCondition(rule),
            effect,
            string.IsNullOrWhiteSpace(rule.Reason) ? null : rule.Reason,
            expires,
            roles);
    }

    private static (string Kind, string? Field, string? Op, object? Value, IReadOnlyList<PolicyEditorField> Conditions)
        FromCondition(ConditionNode node, List<string> unsupported, string ruleName)
    {
        switch (node)
        {
            case ConditionNode.Always:
                return ("always", null, null, null, []);
            case ConditionNode.Field field:
                return ("field", field.Path, field.Op, field.Value, []);
            case ConditionNode.All all when all.Items.All(item => item is ConditionNode.Field):
                return ("all", null, null, null, all.Items.OfType<ConditionNode.Field>().Select(ToField).ToList());
            case ConditionNode.Any any when any.Items.All(item => item is ConditionNode.Field):
                return ("any", null, null, null, any.Items.OfType<ConditionNode.Field>().Select(ToField).ToList());
            default:
                unsupported.Add($"Rule '{ruleName}' has nested conditions the visual editor cannot edit.");
                return ("always", null, null, null, []);
        }
    }

    private static PolicyEditorField ToField(ConditionNode.Field field) =>
        new(field.Path, field.Op, field.Value);

    private static ConditionNode ToCondition(PolicyEditorRule rule)
    {
        switch (rule.WhenKind?.Trim().ToLowerInvariant())
        {
            case "field":
                if (string.IsNullOrWhiteSpace(rule.Field))
                {
                    return new ConditionNode.Always();
                }

                return new ConditionNode.Field(rule.Field, string.IsNullOrWhiteSpace(rule.Op) ? "eq" : rule.Op, rule.Value);
            case "all":
                return new ConditionNode.All(rule.Conditions.Select(ToFieldCondition).ToList());
            case "any":
                return new ConditionNode.Any(rule.Conditions.Select(ToFieldCondition).ToList());
            default:
                return new ConditionNode.Always();
        }
    }

    private static ConditionNode ToFieldCondition(PolicyEditorField field) =>
        new ConditionNode.Field(
            field.Field,
            string.IsNullOrWhiteSpace(field.Op) ? "eq" : field.Op,
            field.Value);
}
