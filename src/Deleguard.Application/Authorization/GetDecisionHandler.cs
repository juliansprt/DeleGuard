using Deleguard.Application.Abstractions;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;

namespace Deleguard.Application.Authorization;

public sealed record AdminDecisionFacts(
    string DecisionId,
    string Decision,
    string? Reason,
    string Action,
    string ResourceType,
    string ResourceId,
    string AgentExternalId,
    string? ActorId,
    string? ActorType,
    string? PolicyId,
    int? PolicyVersion,
    string? MatchedRule,
    string? ApprovalId,
    DateTimeOffset CreatedAt);

public sealed class GetDecisionHandler
{
    private readonly IDecisionStore _decisions;
    private readonly IPermitStore _permits;
    private readonly IClock _clock;
    private readonly IExecutionControl? _execution;

    public GetDecisionHandler(IDecisionStore decisions, IPermitStore permits, IClock clock, IExecutionControl? execution = null)
    {
        _decisions = decisions;
        _permits = permits;
        _clock = clock; _execution = execution;
    }

    public Task<DecisionDto> HandleAsync(AgentPrincipal principal, string decisionId, CancellationToken cancellationToken) => HandleAsync(principal,decisionId,cancellationToken,null);
    public async Task<DecisionDto> HandleAsync(AgentPrincipal principal, string decisionId, CancellationToken cancellationToken, string? projectId)
    {
        var decision = await _decisions.GetAsync(principal.OrganizationId, decisionId, cancellationToken)
                       ?? throw new NotFoundException("Decision not found.");
        if (!string.Equals(decision.AgentId, principal.AgentId, StringComparison.Ordinal) || decision.ApplicationId != principal.ApplicationId || decision.EnvironmentId != principal.EnvironmentId)
        {
            throw new NotFoundException("Decision not found.");
        }
        if (projectId is not null)
        {
            using var attributes = System.Text.Json.JsonDocument.Parse(decision.ResourceAttributesJson);
            if (!attributes.RootElement.TryGetProperty("project_id", out var project) || project.ValueKind != System.Text.Json.JsonValueKind.String || project.GetString() != projectId)
                throw new NotFoundException("Decision not found.");
        }
        string? permit = null;
        DateTimeOffset? expiresAt = decision.ExpiresAt;
        if (decision.PermitId is not null)
        {
            Permit? stored = await _permits.GetAsync(principal.OrganizationId, decision.PermitId, cancellationToken);
            if (stored is not null && stored.IsActive(_clock.UtcNow))
            {
                permit = stored.Token;
                expiresAt = stored.ExpiresAt;
            }
            else
            {
                permit = null;
            }
        }

        return new DecisionDto(
            DecisionNames.ToApi(decision.Decision),
            decision.Id,
            decision.Reason,
            decision.PolicyId,
            decision.PolicyVersion,
            permit,
            decision.ApprovalId,
            expiresAt, decision.PermitId, decision.OperationId is null ? null : new(decision.OperationId, decision.OperationHash!), decision.ResourceSnapshotHash,
            _execution is null ? null : await _execution.GetAsync(principal, decision.Id, cancellationToken));
    }

    /// <summary>
    /// Tenant-scoped decision facts for Administration MCP resources. Never includes permit tokens.
    /// </summary>
    public async Task<AdminDecisionFacts> GetOrganizationFactsAsync(
        string organizationId,
        string decisionId,
        CancellationToken cancellationToken)
    {
        var decision = await _decisions.GetAsync(organizationId, decisionId, cancellationToken)
                       ?? throw new NotFoundException("Decision not found.");
        return new AdminDecisionFacts(
            decision.Id,
            DecisionNames.ToApi(decision.Decision),
            decision.Reason,
            decision.Action,
            decision.ResourceType,
            decision.ResourceId,
            decision.AgentExternalId,
            decision.ActorId,
            decision.ActorType,
            decision.PolicyId,
            decision.PolicyVersion,
            decision.MatchedRule,
            decision.ApprovalId,
            decision.CreatedAt);
    }
}
