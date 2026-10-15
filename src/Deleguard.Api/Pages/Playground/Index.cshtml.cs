using System.Text.Json;
using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Playground;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Playground;

public sealed class IndexModel : PageModel
{
    private static readonly JsonSerializerOptions CatalogJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions AttributesJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly PlaygroundHandler _handler;

    public IndexModel(PlaygroundHandler handler)
    {
        _handler = handler;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public IReadOnlyList<AgentListItem> Agents { get; private set; } = [];
    public IReadOnlyList<Deleguard.Domain.Authorization.ActionDefinition> Actions { get; private set; } = [];
    public IReadOnlyList<PolicySummary> Policies { get; private set; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<PlaygroundScenario>> Scenarios { get; private set; } =
        new Dictionary<string, IReadOnlyList<PlaygroundScenario>>();
    public string CatalogJson { get; private set; } = "{}";
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public PlaygroundAuthorizeResult? Result { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string AgentId { get; set; } = string.Empty;

    [BindProperty]
    public string AgentApiKey { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string ActionName { get; set; } = string.Empty;

    [BindProperty]
    public string ResourceType { get; set; } = string.Empty;

    [BindProperty]
    public string ResourceId { get; set; } = string.Empty;

    [BindProperty]
    public string AttributesJson { get; set; } = "{}";

    [BindProperty]
    public string DecisionId { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        await LoadAsync(cancellationToken, applyDefaults: true);
    }

    public async Task<IActionResult> OnPostAuthorizeAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        await RunAsync(
            () => _handler.AuthorizeAsync(
                HttpContext.RequireOrganizationId(),
                AgentId,
                AgentApiKey,
                ActionName,
                ResourceType,
                ResourceId,
                ParseAttributes(AttributesJson),
                cancellationToken),
            cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostReportExecutionAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        await RunAsync(
            () => _handler.ReportExecutionAsync(
                HttpContext.RequireOrganizationId(),
                AgentId,
                AgentApiKey,
                DecisionId,
                ParseAttributes(AttributesJson),
                cancellationToken),
            cancellationToken);
        return Page();
    }

    private async Task RunAsync(Func<Task<PlaygroundAuthorizeResult>> action, CancellationToken cancellationToken)
    {
        try
        {
            Result = await action();
            DecisionId = Result.Decision.DecisionId;
            Message = $"{Result.Decision.Decision}. {Result.Message}";
            IsError = Result.Decision.Decision == "DENY";
        }
        catch (JsonException)
        {
            IsError = true;
            Message = "Attributes must be a JSON object.";
        }
        catch (Exception ex) when (ex is AgentAuthenticationException or RequestValidationException or ConflictException or NotFoundException or FailClosedException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken, applyDefaults: false);
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool applyDefaults)
    {
        OrganizationId = HttpContext.RequireOrganizationId();
        var catalog = await _handler.LoadCatalogAsync(
            OrganizationId,
            HttpContext.RequireProductApplicationId(),
            cancellationToken);
        Agents = catalog.Agents;
        Actions = catalog.Actions;
        Policies = catalog.Policies;
        Scenarios = catalog.Scenarios;

        if (applyDefaults)
        {
            ApplyDefaults();
        }

        CatalogJson = JsonSerializer.Serialize(new
        {
            orgId = OrganizationId,
            agents = Agents.Select(agent => new
            {
                agent.Id,
                agent.ExternalId,
                agent.DisplayName,
                agent.EnvironmentName,
                keyPrefix = agent.ActiveKeyPrefix
            }),
            actions = Actions.Select(action => new
            {
                action.Name,
                action.ResourceType
            }),
            policies = Policies.Select(policy => new
            {
                policy.Id,
                policy.Slug,
                policy.Action,
                policy.ResourceType,
                version = policy.PublishedVersionNumber
            }),
            scenarios = Scenarios.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(scenario => new
                {
                    scenario.Label,
                    scenario.Effect,
                    scenario.ResourceId,
                    attributes = scenario.Attributes
                }))
        }, CatalogJsonOptions).Replace("</", "<\\/", StringComparison.Ordinal);
    }

    private void ApplyDefaults()
    {
        if (string.IsNullOrWhiteSpace(AgentId))
        {
            AgentId = Agents.FirstOrDefault()?.Id ?? string.Empty;
        }

        var publishedActions = Policies
            .Select(policy => policy.Action)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        var selectedAction = Actions.FirstOrDefault(action => string.Equals(action.Name, ActionName, StringComparison.Ordinal))
                             ?? Actions.FirstOrDefault(action => publishedActions.Contains(action.Name))
                             ?? Actions.FirstOrDefault();
        if (selectedAction is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ActionName))
        {
            ActionName = selectedAction.Name;
        }

        if (string.IsNullOrWhiteSpace(ResourceType))
        {
            ResourceType = selectedAction.ResourceType;
        }

        ApplyScenario(selectedAction.Name, selectedAction.ResourceType);
    }

    private void ApplyScenario(string actionName, string resourceType)
    {
        var scenario = Scenarios.TryGetValue(actionName, out var list)
            ? PlaygroundScenarioBuilder.Default(list)
            : null;
        if (string.IsNullOrWhiteSpace(ResourceId))
        {
            ResourceId = scenario?.ResourceId ?? PlaygroundScenarioBuilder.DefaultResourceId(resourceType);
        }

        if (string.IsNullOrWhiteSpace(AttributesJson) || AttributesJson == "{}")
        {
            AttributesJson = scenario is null
                ? "{}"
                : JsonSerializer.Serialize(scenario.Attributes, AttributesJsonOptions);
        }
    }

    private static Dictionary<string, JsonElement> ParseAttributes(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, JsonElement>();
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new RequestValidationException("Attributes must be a JSON object.");
        }

        var attributes = new Dictionary<string, JsonElement>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            attributes[property.Name] = property.Value.Clone();
        }

        return attributes;
    }
}
