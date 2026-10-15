namespace Deleguard.Application.Abstractions;

public sealed record ControlSummary(int ChecksToday, int DenialsToday, int PendingApprovals,
    int MissingEvidence, int FailedToday, bool AuthorizationTested, bool ExecutionReported,
    decimal GatewaySpendUsd, int BudgetBlocksToday);
public sealed record CoveragePolicy(string Id, string Slug, int Version);
public sealed record CoverageRow(string? AgentId, string AgentName, bool AgentEnabled, string ActionId,
    string Action, string ResourceType, bool ActionAssigned, IReadOnlyList<CoveragePolicy> Policies)
{
    public string Status => AgentId is null ? "No agent assigned" : !AgentEnabled ? "Agent disabled" :
        !ActionAssigned ? "Action not assigned" : Policies.Count == 0 ? "No assigned published policy" : "Ready to evaluate";
}
public sealed record DecisionActivity(string Id, string AgentId, string Agent, string Action, string Resource,
    string Decision, string? Reason, DateTimeOffset CreatedAt, bool HasEvidence,
    string? RequestedModel, string? UsedModel, string? RouteReason, decimal? CostUsd, string? ExecutionStatus, string Source="application");
public sealed record EvidenceRecord(string Id, string Status, string? ProviderReference, string? ResultHash, DateTimeOffset CreatedAt);
public sealed record ApprovalRecord(string Id, string Status, DateTimeOffset ExpiresAt, string? ResolvedBy, DateTimeOffset? ResolvedAt);
public sealed record PermitRecord(string Id, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);
public sealed record DecisionCase(DecisionActivity Decision, string? InitialDecision, string ApplicationId, string? ActorId,
    string? PolicyId, string? PolicyDefinitionId, int? PolicyVersion, string? MatchedRule, string? SnapshotHash,
    IReadOnlyList<ApprovalRecord> Approvals, IReadOnlyList<PermitRecord> Permits,
    IReadOnlyList<EvidenceRecord> Evidence);
public sealed record ActivityPage(IReadOnlyList<DecisionActivity> Items, int Page, bool HasNext);

/// <summary>Read-only projections. Every query requires both organization and application scope.</summary>
public interface IControlQueries
{
    Task<ControlSummary> SummaryAsync(string organizationId, string applicationId, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<CoverageRow>> CoverageAsync(string organizationId, string applicationId, string? agentId, CancellationToken ct);
    Task<ActivityPage> ActivityAsync(string organizationId, string applicationId, string? agentId, string? filter, int page, CancellationToken ct);
    Task<DecisionCase?> CaseAsync(string organizationId, string applicationId, string decisionId, CancellationToken ct);
    Task<decimal> AgentSpendAsync(string organizationId, string applicationId, string agentId, DateTimeOffset now, CancellationToken ct);
    Task<bool> AgentHasEvidenceAsync(string organizationId, string applicationId, string agentId, CancellationToken ct);
    Task<bool> AgentHasAuthorizationAsync(string organizationId,string applicationId,string agentId,CancellationToken ct);
}
