using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Policy;
namespace Deleguard.Application.Authorization;

/// <summary>Pure evaluation: no permit, outbox, model call, or execution.</summary>
public sealed class AuthorizationEvaluator(IActionRegistry _actions, IActionGrantStore _actionGrants,
    IPolicyRepository _policies, IPolicyEngine _engine, IActorDirectory _actors, IIdeControl? _ide = null)
{
    public async Task<PolicyEvaluation> EvaluateAsync(AuthorizeCommand command, CancellationToken cancellationToken)
    {
        AuthorizeHandler.Validate(command);
        var attributes = command.ResourceAttributes ?? new Dictionary<string, JsonElement>();
        var context = command.Context ?? new Dictionary<string, JsonElement>();
        PolicyEvaluation evaluation;
        try
        {
            var action = await _actions.GetByNameAsync(
                command.Principal.OrganizationId,
                command.Principal.ApplicationId,
                command.Action,
                cancellationToken);
            if (action is null || action.ResourceType != command.ResourceType)
            {
                evaluation = new PolicyEvaluation(DecisionEffect.Deny, "Unknown action.", null, null, null, null, []);
            }
            else if (command.Action.StartsWith("admin.", StringComparison.Ordinal) &&
                     !string.Equals(command.Principal.AgentExternalId, AccessControlIds.AdminMcpExternalId, StringComparison.Ordinal))
            {
                evaluation = new PolicyEvaluation(DecisionEffect.Deny, "Administration actions are bound to the admin-mcp agent.", null, null, null, null, []);
            }
            else if (!await _actionGrants.HasAsync(
                         command.Principal.OrganizationId,
                         command.Principal.AgentId,
                         action.Id,
                         cancellationToken))
            {
                evaluation = new PolicyEvaluation(DecisionEffect.Deny, "Agent is not allowed to request this action.", null, null, null, null, []);
            }
            else
            {
                var policies = await _policies.GetPublishedAsync(
                    command.Principal.OrganizationId,
                    command.Principal.ApplicationId,
                    command.Action,
                    command.ResourceType,
                    command.Principal.AgentId,
                    cancellationToken);
                var actorRoles = await _actors.GetRolesAsync(command.Principal.OrganizationId, command.ActorId, cancellationToken);
                var envelope = new AuthorizationEnvelope(
                    command.Principal.AgentExternalId,
                    command.Principal.EnvironmentName,
                    command.ActorId,
                    command.ActorType,
                    command.Action,
                    command.ResourceType,
                    command.ResourceId,
                    attributes,
                    context,
                    actorRoles);
                evaluation = _engine.Evaluate(envelope, policies);
            }
        }
        catch (Exception ex) when (ex is not FailClosedException)
        {
            throw new FailClosedException("Policy evaluation failed.", ex);
        }

        if (_ide is not null) evaluation = await _ide.EvaluateAsync(command, evaluation, cancellationToken);
        else if (AuthorizeHandler.IsIdeAction(command.Action))
            throw new FailClosedException("IDE control is unavailable.");

        return evaluation;
    }
}
