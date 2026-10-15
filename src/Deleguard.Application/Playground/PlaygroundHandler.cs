using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Audit;
using Deleguard.Application.Authorization;
using Deleguard.Application.Evidence;
using Deleguard.Application.Policies;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;

namespace Deleguard.Application.Playground;

public sealed record PlaygroundCatalog(
    IReadOnlyList<AgentListItem> Agents,
    IReadOnlyList<ActionDefinition> Actions,
    IReadOnlyList<PolicySummary> Policies,
    IReadOnlyDictionary<string, IReadOnlyList<PlaygroundScenario>> Scenarios);

public sealed record PlaygroundAuthorizeResult(
    DecisionDto Decision,
    string AgentExternalId,
    string EnvironmentName,
    string Message,
    bool EvidenceReported = false);

public sealed class PlaygroundHandler
{
    private readonly IAgentDirectory _agents;
    private readonly IActionRegistry _actions;
    private readonly IPolicyRepository _policies;
    private readonly Func<AuthorizeCommand, CancellationToken, Task<DecisionDto>> _authorize;
    private readonly Func<AgentPrincipal, string, CancellationToken, Task<DecisionDto>> _getDecision;
    private readonly Func<ReportExecutionCommand, CancellationToken, Task> _reportExecution;

    public PlaygroundHandler(
        IAgentDirectory agents,
        IActionRegistry actions,
        IPolicyRepository policies,
        AuthorizeHandler authorize,
        GetDecisionHandler decisions,
        ReportExecutionHandler evidence)
        : this(agents, actions, policies, authorize.HandleAsync, decisions.HandleAsync, evidence.HandleAsync)
    {
    }

    public PlaygroundHandler(
        IAgentDirectory agents,
        IActionRegistry actions,
        IPolicyRepository policies,
        Func<AuthorizeCommand, CancellationToken, Task<DecisionDto>> authorize,
        Func<AgentPrincipal, string, CancellationToken, Task<DecisionDto>> getDecision,
        Func<ReportExecutionCommand, CancellationToken, Task> reportExecution)
    {
        _agents = agents;
        _actions = actions;
        _policies = policies;
        _authorize = authorize;
        _getDecision = getDecision;
        _reportExecution = reportExecution;
    }

    public async Task<PlaygroundCatalog> LoadCatalogAsync(
        string organizationId,
        string applicationId,
        CancellationToken cancellationToken)
    {
        var agents = (await _agents.ListAsync(organizationId, applicationId, cancellationToken))
            .Where(agent => agent.Enabled
                            && !string.Equals(agent.ExternalId, AccessControlIds.AdminMcpExternalId, StringComparison.Ordinal))
            .OrderBy(agent => agent.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var actions = (await _actions.ListAsync(organizationId, applicationId, cancellationToken))
            .Where(action => !ActionAdminHandler.IsAdministration(action.Name))
            .OrderBy(action => action.Name, StringComparer.Ordinal)
            .ToList();
        var policies = (await _policies.ListAsync(organizationId, applicationId, cancellationToken))
            .Where(policy => policy.PublishedVersionNumber is not null
                             && !PolicyAdminHandler.IsAdministrationCatalog(policy.Slug, policy.Action))
            .OrderBy(policy => policy.Slug, StringComparer.Ordinal)
            .ToList();
        var scenarios = new Dictionary<string, IReadOnlyList<PlaygroundScenario>>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            var published = agents.Count == 0
                ? []
                : await _policies.GetPublishedAsync(
                    organizationId,
                    applicationId,
                    action.Name,
                    action.ResourceType,
                    agents[0].Id,
                    cancellationToken);
            var samples = PlaygroundScenarioBuilder.Build(published, action.Name, action.ResourceType);
            if (samples.Count > 0)
            {
                scenarios[action.Name] = samples;
            }
        }

        return new PlaygroundCatalog(agents, actions, policies, scenarios);
    }

    public async Task<PlaygroundAuthorizeResult> AuthorizeAsync(
        string organizationId,
        string agentId,
        string agentApiKey,
        string action,
        string resourceType,
        string resourceId,
        Dictionary<string, JsonElement>? attributes,
        CancellationToken cancellationToken)
    {
        var principal = await ResolvePrincipalAsync(organizationId, agentId, agentApiKey, cancellationToken);
        var normalizedAction = Required(action, "Action is required.");
        if (ActionAdminHandler.IsAdministration(normalizedAction))
        {
            throw new RequestValidationException("Playground cannot authorize administration actions.");
        }

        var normalizedType = Required(resourceType, "Resource type is required.");
        var normalizedId = Required(resourceId, "Resource id is required.");
        var idempotencyKey = $"playground-{Guid.NewGuid():N}";
        var decision = await _authorize(new AuthorizeCommand(
            principal,
            principal.AgentExternalId,
            principal.EnvironmentName,
            principal.AgentExternalId,
            "service",
            null,
            normalizedAction,
            normalizedType,
            normalizedId,
            attributes,
            null,
            null,
            idempotencyKey,
            AuditPayload.PlaygroundSource), cancellationToken);

        return new PlaygroundAuthorizeResult(
            decision,
            principal.AgentExternalId,
            principal.EnvironmentName,
            MessageFor(decision, evidenceReported: false),
            EvidenceReported: false);
    }

    public async Task<PlaygroundAuthorizeResult> ReportExecutionAsync(
        string organizationId,
        string agentId,
        string agentApiKey,
        string decisionId,
        Dictionary<string, JsonElement>? attributes,
        CancellationToken cancellationToken)
    {
        var principal = await ResolvePrincipalAsync(organizationId, agentId, agentApiKey, cancellationToken);
        var id = Required(decisionId, "Decision id is required.");
        var decision = await _getDecision(principal, id, cancellationToken);
        if (decision.Decision != DecisionNames.Allow || string.IsNullOrWhiteSpace(decision.Permit))
        {
            var wait = decision.Decision == DecisionNames.RequireApproval
                ? "Decision is still REQUIRE_APPROVAL. Approve first."
                : decision.Reason ?? "No active permit.";
            return new PlaygroundAuthorizeResult(decision, principal.AgentExternalId, principal.EnvironmentName, wait);
        }

        var snapshot = CanonicalJson.HashHex(JsonSerializer.Serialize(attributes ?? new Dictionary<string, JsonElement>()));
        await _reportExecution(new ReportExecutionCommand(
            principal,
            decision.DecisionId,
            null,
            "SUCCESS",
            null,
            null,
            snapshot,
            new Dictionary<string, JsonElement>
            {
                ["source"] = JsonSerializer.SerializeToElement(AuditPayload.PlaygroundSource)
            },
            AuditPayload.PlaygroundSource), cancellationToken);

        var current = await _getDecision(principal, decision.DecisionId, cancellationToken);
        return new PlaygroundAuthorizeResult(
            current,
            principal.AgentExternalId,
            principal.EnvironmentName,
            $"Execution reported for decision {current.DecisionId}.",
            EvidenceReported: true);
    }

    private async Task<AgentPrincipal> ResolvePrincipalAsync(
        string organizationId,
        string agentId,
        string agentApiKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(agentApiKey))
        {
            return await AuthenticateTenantAgentAsync(organizationId, agentApiKey, cancellationToken);
        }

        var selected = Required(agentId, "Select an agent.");
        var principal = await _agents.GetPrincipalAsync(organizationId, selected, cancellationToken)
                        ?? throw new NotFoundException("Agent not found or disabled.");
        if (string.Equals(principal.AgentExternalId, AccessControlIds.AdminMcpExternalId, StringComparison.Ordinal))
        {
            throw new NotFoundException("Agent not found or disabled.");
        }

        return principal;
    }

    private async Task<AgentPrincipal> AuthenticateTenantAgentAsync(
        string organizationId,
        string agentApiKey,
        CancellationToken cancellationToken)
    {
        var principal = await _agents.AuthenticateAsync(agentApiKey, cancellationToken)
                        ?? throw new AgentAuthenticationException("Invalid agent API key.");
        if (!string.Equals(principal.OrganizationId, organizationId, StringComparison.Ordinal)
            || string.Equals(principal.AgentExternalId, AccessControlIds.AdminMcpExternalId, StringComparison.Ordinal))
        {
            throw new AgentAuthenticationException("Invalid agent API key.");
        }

        return principal;
    }

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RequestValidationException(message);
        }

        return value.Trim();
    }

    private static string MessageFor(DecisionDto decision, bool evidenceReported) =>
        decision.Decision switch
        {
            DecisionNames.Allow when evidenceReported => $"Execution reported for decision {decision.DecisionId}.",
            DecisionNames.Allow when string.IsNullOrWhiteSpace(decision.Permit) =>
                "ALLOW without an active permit. Report execution is unavailable.",
            DecisionNames.Allow => "Permit issued. Report execution when the agent would run the action.",
            DecisionNames.RequireApproval => "Human approval required. Approve, then report execution.",
            _ => decision.Reason ?? "Denied."
        };
}
