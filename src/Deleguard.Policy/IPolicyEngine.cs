using System.Text.Json;
using Deleguard.Domain.Common;

namespace Deleguard.Policy;

public sealed record AuthorizationEnvelope(
    string AgentId,
    string Environment,
    string? ActorId,
    string? ActorType,
    string Action,
    string ResourceType,
    string ResourceId,
    IReadOnlyDictionary<string, JsonElement> ResourceAttributes,
    IReadOnlyDictionary<string, JsonElement> Context,
    IReadOnlyList<string>? ActorRoles = null);

public sealed record PolicyEvaluation(
    DecisionEffect Effect,
    string Reason,
    string? PolicyId,
    int? PolicyVersion,
    string? MatchedRule,
    TimeSpan? ApprovalExpiresIn,
    IReadOnlyList<string> ApproverRoles);

public interface IPolicyEngine
{
    PolicyEvaluation Evaluate(AuthorizationEnvelope envelope, IReadOnlyList<CompiledPolicy> policies);
}
