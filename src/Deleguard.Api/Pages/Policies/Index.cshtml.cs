using System.Text.RegularExpressions;
using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Application.Identity;
using Deleguard.Application.Policies;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Identity;
using Deleguard.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages.Policies;

public sealed class IndexModel : PageModel
{
    private static readonly Regex SlugPattern = new("^[a-z][a-z0-9-]{1,127}$", RegexOptions.Compiled);

    private readonly PolicyAdminHandler _handler;
    private readonly ActionAdminHandler _actions;
    private readonly AgentAdminHandler _agents;
    private readonly ApplicationAdminHandler _applications;
    private readonly IAdminActionGateway _gateway;

    public IndexModel(
        PolicyAdminHandler handler,
        ActionAdminHandler actions,
        AgentAdminHandler agents,
        ApplicationAdminHandler applications,
        IAdminActionGateway gateway)
    {
        _handler = handler;
        _actions = actions;
        _agents = agents;
        _applications = applications;
        _gateway = gateway;
    }

    public const string ScopeProtected = "protected";
    public const string ScopeAdmin = "admin";

    public IReadOnlyList<PolicyRow> Policies { get; private set; } = [];
    public IReadOnlyList<ActionChoice> Actions { get; private set; } = [];
    public IReadOnlyList<AgentListItem> AssignableAgents { get; private set; } = [];
    public string ActionCatalogJson { get; private set; } = "{}";
    public string ActionAgentsJson { get; private set; } = "{}";
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool CanWrite { get; private set; }
    public string? PrefillAction { get; private set; }
    public bool PasteOpen { get; private set; }
    public string? PendingApprovalId { get; private set; }
    public int TotalCount { get; private set; }
    public int ProtectedCount { get; private set; }
    public int AdministrationCount { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string Scope { get; set; } = ScopeProtected;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public string Slug { get; set; } = "example-policy";

    [BindProperty]
    public string ActionName { get; set; } = "invoice.cancel";

    [BindProperty]
    public string ResourceType { get; set; } = "invoice";

    [BindProperty]
    public string Yaml { get; set; } = DraftYaml("example-policy", "invoice.cancel", "invoice");

    [BindProperty]
    public List<string> SelectedAgentIds { get; set; } = [];

    public bool IsAdminScope => string.Equals(Scope, ScopeAdmin, StringComparison.OrdinalIgnoreCase);

    public sealed record PolicyRow(
        PolicySummary Summary,
        string Agents,
        bool IsCatalog);

    public async Task<IActionResult> OnGetAsync(string? action, string? resource_type, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesRead);
        if (TryPrefill(action, resource_type, out var slug, out var resource, out var yaml))
        {
            PrefillAction = action;
            Slug = slug;
            ActionName = action!;
            ResourceType = resource;
            Yaml = yaml;
            PasteOpen = false;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        try
        {
            var yaml = BuildStructuredYaml();
            var created = await _handler.CreateAsync(new CreatePolicyCommand(
                HttpContext.RequireOrganizationId(),
                HttpContext.RequireProductApplicationId(),
                yaml,
                SelectedAgentIds), cancellationToken);
            TempData[DetailModel.CreatedFlashKey] = created.Policy.Slug;
            return Redirect($"/policies/{created.Policy.Id}");
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException)
        {
            IsError = true;
            Message = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPasteAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        PasteOpen = true;
        try
        {
            var created = await _handler.CreateAsync(new CreatePolicyCommand(
                HttpContext.RequireOrganizationId(),
                HttpContext.RequireProductApplicationId(),
                Yaml,
                SelectedAgentIds), cancellationToken);
            TempData[DetailModel.CreatedFlashKey] = created.Policy.Slug;
            return Redirect($"/policies/{created.Policy.Id}");
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException)
        {
            IsError = true;
            Message = ex.Message;
            TryHydrateFromYaml();
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(string policyId, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        try
        {
            await _handler.EnsureCanDeleteAsync(HttpContext.RequireOrganizationId(), policyId, cancellationToken);
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.policy.delete",
                "policy",
                policyId,
                null,
                PermissionCatalog.PoliciesWrite,
                async ct =>
                {
                    await _handler.DeleteAsync(HttpContext.RequireOrganizationId(), policyId, ct);
                    return true;
                },
                cancellationToken);
            if (outcome.Allowed)
            {
                Message = "Policy deleted. Authorize will DENY that action if no other published policy remains.";
            }
            else
            {
                PendingApprovalId = outcome.Pending ? outcome.ApprovalId : null;
                IsError = !outcome.Pending;
                Message = outcome.Pending ? "Waiting for approval." : outcome.Reason ?? "Denied by policy.";
            }
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or NotFoundException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    internal static string DraftYaml(string slug, string action, string resourceType) =>
        PolicyYamlWriter.Write(new CompiledPolicy(
            slug.Trim(),
            1,
            10,
            action.Trim(),
            resourceType.Trim(),
            [],
            DecisionEffect.Deny));

    internal static string SlugFromAction(string action) =>
        action.Replace('.', '-').Replace('_', '-').Replace(':', '-');

    private string BuildStructuredYaml()
    {
        var slug = Slug.Trim();
        var action = ActionName.Trim();
        var resource = ResourceType.Trim();
        if (!SlugPattern.IsMatch(slug))
        {
            throw new RequestValidationException("Id must be a lowercase slug (letters, digits, hyphens), e.g. invoice-cancel.");
        }

        if (!ActionAdminHandler.IsValidName(action))
        {
            throw new RequestValidationException($"Action must be {ActionAdminHandler.NameRules}.");
        }

        if (!ActionAdminHandler.IsValidResourceType(resource))
        {
            throw new RequestValidationException("Resource type must be lowercase (e.g. invoice) and match the action catalog.");
        }

        return DraftYaml(slug, action, resource);
    }

    private static bool TryPrefill(string? action, string? resourceType, out string slug, out string resource, out string yaml)
    {
        slug = "example-policy";
        resource = "invoice";
        yaml = DraftYaml(slug, "invoice.cancel", resource);
        if (string.IsNullOrWhiteSpace(action) || !ActionAdminHandler.IsValidName(action))
        {
            return false;
        }

        resource = string.IsNullOrWhiteSpace(resourceType) ? "resource" : resourceType.Trim();
        if (!ActionAdminHandler.IsValidResourceType(resource))
        {
            return false;
        }

        slug = SlugFromAction(action);
        yaml = DraftYaml(slug, action, resource);
        return true;
    }

    private void TryHydrateFromYaml()
    {
        if (string.IsNullOrWhiteSpace(Yaml))
        {
            return;
        }

        try
        {
            var compiled = PolicyYamlParser.Parse("draft", Yaml);
            Slug = compiled.PolicyId;
            ActionName = compiled.Action;
            ResourceType = compiled.ResourceType;
        }
        catch (Exception)
        {
            // Keep posted YAML; structured fields stay as bound.
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.PoliciesWrite) == true;
        Scope = IsAdminScope ? ScopeAdmin : ScopeProtected;
        if (string.IsNullOrWhiteSpace(Yaml))
        {
            Yaml = DraftYaml(
                string.IsNullOrWhiteSpace(Slug) ? "example-policy" : Slug,
                string.IsNullOrWhiteSpace(ActionName) ? "invoice.cancel" : ActionName,
                string.IsNullOrWhiteSpace(ResourceType) ? "invoice" : ResourceType);
        }

        var org = HttpContext.RequireOrganizationId();
        var productAppId = HttpContext.RequireProductApplicationId();
        var adminApp = await _applications.GetAdministrationAsync(org, cancellationToken);
        AssignableAgents = (await _agents.ListAsync(org, productAppId, cancellationToken))
            .Where(agent => !string.Equals(agent.ExternalId, AccessControlIds.AdminMcpExternalId, StringComparison.Ordinal))
            .ToList();
        var product = await _handler.ListAsync(org, productAppId, cancellationToken);
        var administration = await _handler.ListAsync(org, adminApp.Id, cancellationToken);
        ProtectedCount = product.Count;
        AdministrationCount = administration.Count;
        var all = IsAdminScope ? administration : product;
        TotalCount = all.Count;
        var catalogAppId = IsAdminScope ? adminApp.Id : productAppId;
        var productActions = await _actions.ListAsync(org, productAppId, cancellationToken);
        var actionGrants = await _actions.ListGrantsAsync(org, productActions.Select(item => item.Id).ToList(), cancellationToken);
        if (SelectedAgentIds.Count == 0 && !string.IsNullOrWhiteSpace(PrefillAction))
        {
            var prefill = productActions.FirstOrDefault(item => string.Equals(item.Name, PrefillAction, StringComparison.Ordinal));
            if (prefill is not null)
            {
                SelectedAgentIds = actionGrants.GetValueOrDefault(prefill.Id, []).ToList();
            }
        }

        var grants = await _handler.ListGrantsAsync(org, all.Select(item => item.Id).ToList(), cancellationToken);
        var agentNames = AssignableAgents.Concat(await _agents.ListAsync(org, adminApp.Id, cancellationToken))
            .GroupBy(agent => agent.Id)
            .ToDictionary(g => g.Key, g => g.First().ExternalId, StringComparer.Ordinal);
        var query = Q?.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? all
            : all.Where(item =>
                    item.Slug.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (item.Action?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (item.ResourceType?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        Policies = filtered.Select(item =>
        {
            var label = string.Join(", ",
                grants.GetValueOrDefault(item.Id, []).Select(id => agentNames.GetValueOrDefault(id, id)));
            return new PolicyRow(
                item,
                string.IsNullOrEmpty(label) ? "—" : label,
                PolicyAdminHandler.IsAdministrationCatalog(item.Slug, item.Action));
        }).ToList();
        Actions = (await _actions.ListAsync(org, catalogAppId, cancellationToken))
            .Select(item => new ActionChoice(item.Name, item.ResourceType))
            .OrderBy(item => ActionAdminHandler.IsAdministration(item.Name))
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        ActionCatalogJson = System.Text.Json.JsonSerializer.Serialize(
            Actions.ToDictionary(item => item.Name, item => item.ResourceType));
        ActionAgentsJson = System.Text.Json.JsonSerializer.Serialize(
            productActions.ToDictionary(
                item => item.Name,
                item => actionGrants.GetValueOrDefault(item.Id, []).ToArray()));
    }

    public sealed record ActionChoice(string Name, string ResourceType);
}
