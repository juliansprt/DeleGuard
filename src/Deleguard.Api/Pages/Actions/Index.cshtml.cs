using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Application.Identity;
using Deleguard.Application.Policies;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages.Actions;

public sealed record ActionListItem(
    string Id,
    string Name,
    string ResourceType,
    RiskLevel Risk,
    bool IsAdministration,
    string PolicyCoverage,
    string? PolicyId,
    string? GuardTool,
    string Agents,
    IReadOnlyList<string> AgentIds);

public sealed class IndexModel : PageModel
{
    public const string CreatedNameKey = "created_action_name";
    public const string CreatedResourceKey = "created_action_resource";
    public const string ScopeProtected = "protected";
    public const string ScopeAdmin = "admin";

    private readonly ActionAdminHandler _handler;
    private readonly ActionMcpBindingHandler _bindings;
    private readonly PolicyAdminHandler _policies;
    private readonly AgentAdminHandler _agents;
    private readonly ApplicationAdminHandler _applications;
    private readonly IAdminActionGateway _gateway;

    public IndexModel(
        ActionAdminHandler handler,
        ActionMcpBindingHandler bindings,
        PolicyAdminHandler policies,
        AgentAdminHandler agents,
        ApplicationAdminHandler applications,
        IAdminActionGateway gateway)
    {
        _handler = handler;
        _bindings = bindings;
        _policies = policies;
        _agents = agents;
        _applications = applications;
        _gateway = gateway;
    }

    public IReadOnlyList<ActionListItem> Actions { get; private set; } = [];
    public IReadOnlyList<AgentListItem> AssignableAgents { get; private set; } = [];
    public IReadOnlyList<GuardMappingRecord> Bindings { get; private set; } = [];
    public int ProtectedCount { get; private set; }
    public int AdministrationCount { get; private set; }
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool CanWrite { get; private set; }
    public string? CreatedName { get; private set; }
    public string? CreatedResourceType { get; private set; }
    public string? PendingApprovalId { get; private set; }
    public string GuardSnippet { get; private set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Scope { get; set; } = ScopeProtected;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public string ResourceType { get; set; } = "invoice";

    [BindProperty]
    public string Risk { get; set; } = "HIGH";

    [BindProperty]
    public List<string> SelectedAgentIds { get; set; } = [];

    [BindProperty]
    public string BindActionId { get; set; } = string.Empty;

    [BindProperty]
    public string BindMcpServer { get; set; } = "invoice-demo";

    [BindProperty]
    public string BindMcpTool { get; set; } = "cancel_invoice";

    [BindProperty]
    public string BindResourceIdPath { get; set; } = "invoice_id";

    [BindProperty]
    public string BindAttributePaths { get; set; } = """{"amount":"amount","status":"status","reportedToTaxAuthority":"reportedToTaxAuthority"}""";

    public bool IsAdminScope => string.Equals(Scope, ScopeAdmin, StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.ActionsRead);
        CreatedName = TempData[CreatedNameKey] as string;
        CreatedResourceType = TempData[CreatedResourceKey] as string;
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.ActionsWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.action.create",
                "action",
                Name,
                null,
                PermissionCatalog.ActionsWrite,
                ct => _handler.CreateAsync(new CreateActionCommand(
                    HttpContext.RequireOrganizationId(),
                    HttpContext.RequireProductApplicationId(),
                    Name,
                    ResourceType,
                    Risk,
                    SelectedAgentIds), ct),
                cancellationToken);
            if (!ApplyOutcome(outcome, out var created) || created is null)
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            TempData[CreatedNameKey] = created.Name;
            TempData[CreatedResourceKey] = created.ResourceType;
            return RedirectToPage(new
            {
                scope = ActionAdminHandler.IsAdministration(created.Name) ? ScopeAdmin : ScopeProtected
            });
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync(string actionId, string updatedResourceType, string updatedRisk, List<string>? updatedAgentIds, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.ActionsWrite);
        try
        {
            await _handler.EnsureCanUpdateAsync(HttpContext.RequireOrganizationId(), actionId, cancellationToken);
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.action.update",
                "action",
                actionId,
                null,
                PermissionCatalog.ActionsWrite,
                ct => _handler.UpdateAsync(new UpdateActionCommand(
                    HttpContext.RequireOrganizationId(),
                    actionId,
                    updatedResourceType,
                    updatedRisk,
                    updatedAgentIds ?? []), ct),
                cancellationToken);
            if (ApplyOutcome(outcome, out _))
            {
                Message = "Action updated. The name is unchanged; authorize clients must still send it exactly.";
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

    public async Task<IActionResult> OnPostDeleteAsync(string actionId, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.ActionsWrite);
        try
        {
            await _handler.EnsureCanDeleteAsync(HttpContext.RequireOrganizationId(), actionId, cancellationToken);
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.action.delete",
                "action",
                actionId,
                null,
                PermissionCatalog.ActionsWrite,
                async ct =>
                {
                    await _handler.DeleteAsync(HttpContext.RequireOrganizationId(), actionId, ct);
                    return true;
                },
                cancellationToken);
            if (ApplyOutcome(outcome, out _))
            {
                Message = "Action deleted. Authorize will DENY that name until it is registered again.";
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

    public async Task<IActionResult> OnPostBindAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.ActionsWrite);
        try
        {
            Dictionary<string, string>? attributes = null;
            if (!string.IsNullOrWhiteSpace(BindAttributePaths))
            {
                attributes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(BindAttributePaths)
                             ?? throw new RequestValidationException("attribute_paths must be a JSON object of string paths.");
            }

            await _bindings.CreateAsync(new CreateMcpBindingCommand(
                HttpContext.RequireOrganizationId(),
                BindActionId,
                BindMcpServer,
                BindMcpTool,
                BindResourceIdPath,
                attributes), cancellationToken);
            Message = "Guard MCP binding saved. Point Cursor at the Guard, not the downstream MCP.";
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or NotFoundException or System.Text.Json.JsonException)
        {
            IsError = true;
            Message = ex is System.Text.Json.JsonException ? "attribute_paths must be a JSON object of string paths." : ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUnbindAsync(string bindingId, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.ActionsWrite);
        try
        {
            await _bindings.DeleteAsync(HttpContext.RequireOrganizationId(), bindingId, cancellationToken);
            Message = "Guard MCP binding removed.";
        }
        catch (Exception ex) when (ex is NotFoundException or RequestValidationException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.ActionsWrite) == true;
        Scope = IsAdminScope ? ScopeAdmin : ScopeProtected;

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
        var definitions = IsAdminScope ? administration : product;
        var catalogAppId = IsAdminScope ? adminApp.Id : productAppId;
        var policies = await _policies.ListAsync(org, catalogAppId, cancellationToken);
        var coverage = BuildCoverage(policies);
        Bindings = await _bindings.ListAsync(org, cancellationToken);
        var toolsByAction = Bindings
            .GroupBy(item => item.ActionId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(item => GuardToolNames.Compose(item.McpServer, item.McpTool))), StringComparer.Ordinal);

        var grants = await _handler.ListGrantsAsync(org, definitions.Select(item => item.Id).ToList(), cancellationToken);
        var agentNames = AssignableAgents.Concat(
                (await _agents.ListAsync(org, adminApp.Id, cancellationToken)))
            .GroupBy(agent => agent.Id)
            .ToDictionary(g => g.Key, g => g.First().ExternalId, StringComparer.Ordinal);

        var adminScope = IsAdminScope;
        var query = Q?.Trim();
        Actions = definitions
            .Where(item => ActionAdminHandler.IsAdministration(item.Name) == adminScope)
            .Where(item => string.IsNullOrEmpty(query)
                           || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || item.ResourceType.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(item =>
            {
                var match = coverage.GetValueOrDefault(item.Name);
                var published = match is not null && match.PublishedVersionNumber is not null;
                var agentLabel = string.Join(", ",
                    grants.GetValueOrDefault(item.Id, []).Select(id => agentNames.GetValueOrDefault(id, id)));
                return new ActionListItem(
                    item.Id,
                    item.Name,
                    item.ResourceType,
                    item.Risk,
                    ActionAdminHandler.IsAdministration(item.Name),
                    published ? "published" : match is null ? "none" : "draft",
                    match?.Id,
                    toolsByAction.GetValueOrDefault(item.Id),
                    string.IsNullOrEmpty(agentLabel) ? "—" : agentLabel,
                    grants.GetValueOrDefault(item.Id, []));
            })
            .ToList();

        if (string.IsNullOrWhiteSpace(BindActionId))
        {
            BindActionId = Actions.FirstOrDefault(item => !item.IsAdministration)?.Id ?? string.Empty;
        }

        GuardSnippet =
            "{\n  \"mcpServers\": {\n    \"deleguard-guard\": {\n      \"url\": \"" +
            $"{Request.Scheme}://{Request.Host}/mcp/guard" +
            "\",\n      \"headers\": { \"Authorization\": \"Bearer dgk_YOUR_AGENT_KEY\" }\n    }\n  }\n}";
    }

    private static Dictionary<string, PolicySummary> BuildCoverage(IReadOnlyList<PolicySummary> policies)
    {
        var map = new Dictionary<string, PolicySummary>(StringComparer.Ordinal);
        foreach (var policy in policies)
        {
            if (string.IsNullOrWhiteSpace(policy.Action))
            {
                continue;
            }

            if (!map.TryGetValue(policy.Action, out var current))
            {
                map[policy.Action] = policy;
                continue;
            }

            if (Rank(policy) > Rank(current))
            {
                map[policy.Action] = policy;
            }
        }

        return map;
    }

    private static int Rank(PolicySummary policy)
    {
        if (policy.PublishedVersionNumber is not null)
        {
            return 2;
        }

        return 1;
    }

    private bool ApplyOutcome<T>(AdminActionOutcome<T> outcome, out T? value)
    {
        value = default;
        if (outcome.Allowed)
        {
            value = outcome.Value;
            return true;
        }

        PendingApprovalId = outcome.Pending ? outcome.ApprovalId : null;
        IsError = !outcome.Pending;
        Message = outcome.Pending ? "Waiting for approval." : outcome.Reason ?? "Denied by policy.";
        return false;
    }
}
