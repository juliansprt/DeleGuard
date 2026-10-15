using Deleguard.Domain.Common;

namespace Deleguard.Policy;

public sealed class NativePolicyEngine : IPolicyEngine
{
    public PolicyEvaluation Evaluate(AuthorizationEnvelope envelope, IReadOnlyList<CompiledPolicy> policies)
    {
        var applicable = policies
            .Where(p => string.Equals(p.Action, envelope.Action, StringComparison.Ordinal) &&
                        string.Equals(p.ResourceType, envelope.ResourceType, StringComparison.Ordinal))
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.Version)
            .ToList();

        if (applicable.Count == 0)
        {
            return new PolicyEvaluation(DecisionEffect.Deny, "No applicable policy.", null, null, null, null, []);
        }

        PolicyEvaluation? selected = null;
        foreach (var policy in applicable)
        {
            var matches = new List<CompiledRule>();
            foreach (var rule in policy.Rules)
            {
                if (ConditionEvaluator.Matches(rule.When, envelope))
                {
                    matches.Add(rule);
                }
            }

            CompiledRule? winner = null;
            foreach (var rule in matches)
            {
                if (winner is null || DecisionNames.Precedence(rule.Effect) > DecisionNames.Precedence(winner.Effect))
                {
                    winner = rule;
                }
            }

            var effect = winner?.Effect ?? policy.Fallback;
            var reason = winner?.Reason
                         ?? (winner is null ? "Fallback deny." : $"Matched rule '{winner.Name}'.");
            var candidate = new PolicyEvaluation(
                effect,
                reason,
                policy.PolicyId,
                policy.Version,
                winner?.Name,
                winner?.ApprovalExpiresIn,
                winner?.ApproverRoles ?? []);

            if (selected is null || DecisionNames.Precedence(candidate.Effect) > DecisionNames.Precedence(selected.Effect))
            {
                selected = candidate;
            }

            if (selected.Effect == DecisionEffect.Deny)
            {
                break;
            }
        }

        return selected ?? new PolicyEvaluation(DecisionEffect.Deny, "Default deny.", null, null, null, null, []);
    }
}
