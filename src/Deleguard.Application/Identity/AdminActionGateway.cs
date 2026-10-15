using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Application.Evidence;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Common;

namespace Deleguard.Application.Identity;

public sealed class AdminActionGateway : IAdminActionGateway
{
    private readonly AuthorizeHandler _authorize;
    private readonly ReportExecutionHandler _evidence;
    private readonly IAgentDirectory _agents;
    private readonly IExecutionControl? _execution;
    private readonly IAdminContinuationStore? _continuations;
    private readonly GetDecisionHandler? _decisions;
    private readonly IAdminOperationContext? _operationContext;
    private readonly IWebhookEndpointStore? _webhooks;

    public AdminActionGateway(AuthorizeHandler authorize, ReportExecutionHandler evidence, IAgentDirectory agents, IExecutionControl? execution = null, IAdminContinuationStore? continuations = null, GetDecisionHandler? decisions = null, IAdminOperationContext? operationContext = null, IWebhookEndpointStore? webhooks = null)
    {
        _authorize = authorize;
        _evidence = evidence;
        _agents = agents; _execution = execution; _continuations = continuations; _decisions = decisions; _operationContext = operationContext; _webhooks = webhooks;
    }

    public async Task<AdminActionOutcome<T>> RunAsync<T>(
        ConsoleUserPrincipal caller,
        string action,
        string resourceType,
        string resourceId,
        Dictionary<string, JsonElement>? attributes,
        string permission,
        Func<CancellationToken, Task<T>> execute,
        CancellationToken cancellationToken)
    {
        if (!caller.HasPermission(permission))
        {
            throw new ForbiddenException($"Missing permission '{permission}'.");
        }

        var agent = await _agents.GetPrincipalByExternalIdAsync(caller.OrganizationId, AccessControlIds.AdminMcpExternalId, cancellationToken)
                    ?? throw new FailClosedException("Administration MCP agent is not provisioned.");

        if (resourceType == "webhook" && _webhooks is not null &&
            await _webhooks.GetAsync(caller.OrganizationId, resourceId, cancellationToken) is { } endpoint)
        {
            attributes = attributes is null ? new() : new(attributes);
            attributes["reviewed_destination"] = JsonSerializer.SerializeToElement(endpoint.Url);
            attributes["reviewed_endpoint_version"] = JsonSerializer.SerializeToElement(endpoint.UpdatedAt);
        }
        var transport = _operationContext is null ? null : await _operationContext.ReadAsync(cancellationToken);
        var operation = new OperationRef(transport is null ? Guid.NewGuid().ToString("N") : CanonicalJson.HashHex(JsonSerializer.SerializeToNode(new { root = transport.Id, action, resourceType, resourceId, step = attributes is not null && attributes.TryGetValue("operation", out var step) ? step.GetRawText() : null })), CanonicalJson.HashHex(JsonSerializer.SerializeToNode(new
            { action, resourceType, resourceId, attributes, transportHash = transport?.Hash, actor = caller.UserId })));
        var pending = _continuations is null ? null : await _continuations.FindAsync(agent, caller.UserId, operation.Hash, cancellationToken);
        var decision = pending is not null && _decisions is not null
            ? await _decisions.HandleAsync(agent, pending, cancellationToken)
            : await _authorize.HandleAsync(new AuthorizeCommand(
            agent,
            agent.AgentExternalId,
            agent.EnvironmentName,
            caller.UserId,
            "human",
            null,
            action,
            resourceType,
            resourceId,
            attributes,
            null,
            null,
            "adm_" + operation.Id, Operation: operation), cancellationToken);

        if (_decisions is not null) decision = await _decisions.HandleAsync(agent, decision.DecisionId, cancellationToken);
        if (!string.Equals(decision.Decision, "ALLOW", StringComparison.Ordinal))
        {
            return new AdminActionOutcome<T>(decision.Decision, decision.DecisionId, decision.Reason, decision.ApprovalId, decision.Permit, default);
        }

        string? executionId = null;
        if (_execution is not null)
        {
            var claim = await _execution.ConsumeAsync(agent, decision.PermitId!, new(decision.Operation!.Id, operation.Hash, decision.ResourceSnapshotHash!), cancellationToken);
            if (!claim.Acquired) throw new ConflictException("Administrative operation already consumed; inspect its outcome before retrying.");
            executionId = claim.ExecutionId;
        }
        T value;
        var status = "SUCCESS";
        try
        {
            value = await execute(cancellationToken);
        }
        catch
        {
            status = "UNKNOWN";
            await TryReportAsync(agent, decision, executionId, status, cancellationToken);
            throw;
        }

        await TryReportAsync(agent, decision, executionId, status, cancellationToken);
        return new AdminActionOutcome<T>(decision.Decision, decision.DecisionId, decision.Reason, decision.ApprovalId, decision.Permit, value);
    }

    private async Task TryReportAsync(AgentPrincipal agent, DecisionDto decision, string? executionId, string status, CancellationToken cancellationToken)
    {
        await _evidence.HandleAsync(new ReportExecutionCommand(
            agent,
            decision.DecisionId,
            decision.PermitId,
            status,
            "deleguard-admin",
            null,
            decision.ResourceSnapshotHash,
            null, ExecutionId: executionId), cancellationToken);
    }
}
