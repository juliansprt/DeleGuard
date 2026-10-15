using System.Text.Json;

namespace Deleguard.DemoAgent.Agent;

public sealed record DeleguardDecision(
    string Decision,
    string DecisionId,
    string? Reason,
    string? Permit,
    string? ApprovalId,
    string? PolicyId,
    int? PolicyVersion);

public sealed record DeleguardAuthorizeRequest(
    string Action,
    string ResourceId,
    IReadOnlyDictionary<string, JsonElement> Attributes);

public interface IDeleguardClient
{
    Task<DeleguardDecision> AuthorizeAsync(DeleguardAuthorizeRequest request, CancellationToken cancellationToken);
    Task<DeleguardDecision> GetDecisionAsync(string decisionId, CancellationToken cancellationToken);
    Task ReportExecutionAsync(string decisionId, string? permit, string invoiceId, CancellationToken cancellationToken);
}
