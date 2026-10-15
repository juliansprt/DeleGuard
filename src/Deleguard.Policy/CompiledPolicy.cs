using Deleguard.Domain.Common;

namespace Deleguard.Policy;

public sealed record CompiledPolicy(
    string PolicyId,
    int Version,
    int Priority,
    string Action,
    string ResourceType,
    IReadOnlyList<CompiledRule> Rules,
    DecisionEffect Fallback);

public sealed record CompiledRule(
    string Name,
    ConditionNode When,
    DecisionEffect Effect,
    string? Reason,
    TimeSpan? ApprovalExpiresIn,
    IReadOnlyList<string> ApproverRoles);

public abstract record ConditionNode
{
    public sealed record Always : ConditionNode;
    public sealed record Field(string Path, string Op, object? Value) : ConditionNode;
    public sealed record All(IReadOnlyList<ConditionNode> Items) : ConditionNode;
    public sealed record Any(IReadOnlyList<ConditionNode> Items) : ConditionNode;
}
