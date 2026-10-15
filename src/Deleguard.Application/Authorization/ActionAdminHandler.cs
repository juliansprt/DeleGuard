using System.Text.RegularExpressions;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;

namespace Deleguard.Application.Authorization;

public sealed record CreateActionCommand(
    string OrganizationId,
    string ApplicationId,
    string Name,
    string ResourceType,
    string Risk,
    IReadOnlyList<string> AgentIds);

public sealed record UpdateActionCommand(
    string OrganizationId,
    string ActionId,
    string ResourceType,
    string Risk,
    IReadOnlyList<string>? AgentIds);

public sealed class ActionAdminHandler
{
    public const string AdministrationPrefix = "admin.";
    public const string NameRules =
        "lowercase, start with a letter, and use only a-z, 0-9, _, ., :, - (e.g. invoice.cancel)";

    private static readonly Regex ActionPattern = new("^[a-z][a-z0-9_.:-]{1,127}$", RegexOptions.Compiled);
    private static readonly Regex ResourcePattern = new("^[a-z][a-z0-9_.-]{0,63}$", RegexOptions.Compiled);
    private readonly IActionRegistry _actions;
    private readonly IActionGrantStore _grants;
    private readonly IAgentDirectory _agents;
    private readonly IApplicationDirectory _applications;
    private readonly IGuardMappingStore _bindings;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public ActionAdminHandler(
        IActionRegistry actions,
        IActionGrantStore grants,
        IAgentDirectory agents,
        IApplicationDirectory applications,
        IGuardMappingStore bindings,
        IClock clock,
        IUnitOfWork uow)
    {
        _actions = actions;
        _grants = grants;
        _agents = agents;
        _applications = applications;
        _bindings = bindings;
        _clock = clock;
        _uow = uow;
    }

    public static bool IsAdministration(string name) =>
        name.StartsWith(AdministrationPrefix, StringComparison.Ordinal);

    public static bool IsValidName(string name) => ActionPattern.IsMatch(name);

    public static bool IsValidResourceType(string resourceType) => ResourcePattern.IsMatch(resourceType);

    public Task<IReadOnlyList<ActionDefinition>> ListAsync(
        string organizationId,
        string? applicationId,
        CancellationToken cancellationToken) =>
        _actions.ListAsync(organizationId, applicationId, cancellationToken);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ListGrantsAsync(
        string organizationId,
        IReadOnlyList<string> actionIds,
        CancellationToken cancellationToken) =>
        _grants.ListAgentIdsByActionIdsAsync(organizationId, actionIds, cancellationToken);

    public async Task<ActionDefinition> GetAsync(string organizationId, string actionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            throw new RequestValidationException("action_id is required.");
        }

        return await _actions.GetAsync(organizationId, actionId, cancellationToken)
               ?? throw new NotFoundException("Action not found.");
    }

    public async Task<ActionDefinition> CreateAsync(CreateActionCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name.Trim();
        if (!ActionPattern.IsMatch(name))
        {
            throw new RequestValidationException($"Action name must be {NameRules}.");
        }

        var resourceType = NormalizeResourceType(command.ResourceType);
        var risk = ParseRisk(command.Risk);
        var application = await RequireApplicationAsync(command.OrganizationId, command.ApplicationId, cancellationToken);
        if (IsAdministration(name) && string.Equals(application.Kind, ApplicationKinds.Product, StringComparison.Ordinal))
        {
            throw new RequestValidationException("Administration actions cannot be created in a product application.");
        }

        var existing = await _actions.GetByNameAsync(command.OrganizationId, application.Id, name, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException("An action with that name already exists.");
        }

        var agentIds = await NormalizeAgentIdsAsync(command.OrganizationId, application.Id, command.AgentIds, cancellationToken);
        var action = new ActionDefinition
        {
            Id = IdGenerator.New("act"),
            OrganizationId = command.OrganizationId,
            ApplicationId = application.Id,
            Name = name,
            ResourceType = resourceType,
            Risk = risk,
            CreatedAt = _clock.UtcNow
        };
        await _actions.AddAsync(action, cancellationToken);
        await _grants.ReplaceAsync(command.OrganizationId, application.Id, action.Id, agentIds, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return action;
    }

    public async Task EnsureCanUpdateAsync(string organizationId, string actionId, CancellationToken cancellationToken)
    {
        var action = await GetAsync(organizationId, actionId, cancellationToken);
        EnsureMutable(action.Name, "updated");
    }

    public async Task EnsureCanDeleteAsync(string organizationId, string actionId, CancellationToken cancellationToken)
    {
        var action = await GetAsync(organizationId, actionId, cancellationToken);
        EnsureMutable(action.Name, "deleted");
    }

    public async Task<ActionDefinition> UpdateAsync(UpdateActionCommand command, CancellationToken cancellationToken)
    {
        var action = await GetAsync(command.OrganizationId, command.ActionId, cancellationToken);
        EnsureMutable(action.Name, "updated");
        var resourceType = NormalizeResourceType(command.ResourceType);
        var risk = ParseRisk(command.Risk);
        await _actions.UpdateAsync(command.OrganizationId, action.Id, resourceType, risk, cancellationToken);
        if (command.AgentIds is not null)
        {
            var agentIds = await NormalizeAgentIdsAsync(
                command.OrganizationId,
                action.ApplicationId,
                command.AgentIds,
                cancellationToken);
            await _grants.ReplaceAsync(command.OrganizationId, action.ApplicationId, action.Id, agentIds, cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);
        return await GetAsync(command.OrganizationId, action.Id, cancellationToken);
    }

    public async Task DeleteAsync(string organizationId, string actionId, CancellationToken cancellationToken)
    {
        var action = await GetAsync(organizationId, actionId, cancellationToken);
        EnsureMutable(action.Name, "deleted");
        await _bindings.DeleteByActionIdAsync(organizationId, action.Id, cancellationToken);
        await _grants.DeleteByActionIdAsync(organizationId, action.Id, cancellationToken);
        await _actions.DeleteAsync(organizationId, action.Id, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    private async Task<ApplicationListItem> RequireApplicationAsync(
        string organizationId,
        string applicationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new RequestValidationException("application_id is required.");
        }

        return await _applications.GetAsync(organizationId, applicationId, cancellationToken)
               ?? throw new NotFoundException("Application not found.");
    }

    private async Task<IReadOnlyList<string>> NormalizeAgentIdsAsync(
        string organizationId,
        string applicationId,
        IReadOnlyList<string>? agentIds,
        CancellationToken cancellationToken)
    {
        var requested = (agentIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (requested.Count == 0)
        {
            throw new RequestValidationException("Assign at least one agent to this action.");
        }

        var agents = await _agents.ListAsync(organizationId, applicationId, cancellationToken);
        var allowed = agents.Select(agent => agent.Id).ToHashSet(StringComparer.Ordinal);
        if (requested.Any(id => !allowed.Contains(id)))
        {
            throw new RequestValidationException("Agents must belong to the same application as the action.");
        }

        return requested;
    }

    private static void EnsureMutable(string name, string verb)
    {
        if (IsAdministration(name))
        {
            throw new RequestValidationException($"Administration actions cannot be {verb}.");
        }
    }

    private static string NormalizeResourceType(string resourceType)
    {
        var trimmed = resourceType.Trim();
        if (!ResourcePattern.IsMatch(trimmed))
        {
            throw new RequestValidationException("resource_type must be lowercase (e.g. invoice) and match policy match.resource_type.");
        }

        return trimmed;
    }

    private static RiskLevel ParseRisk(string risk)
    {
        if (!Enum.TryParse<RiskLevel>(risk, true, out var parsed))
        {
            throw new RequestValidationException("risk must be LOW, MEDIUM, HIGH, or CRITICAL.");
        }

        return parsed;
    }
}
