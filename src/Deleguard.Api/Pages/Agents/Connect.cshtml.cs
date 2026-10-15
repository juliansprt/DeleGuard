using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Application.Authorization;
using Deleguard.Application.Policies;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Agents;

public sealed class ConnectModel(AgentAdminHandler agents, IControlQueries queries, IAdminActionGateway admin,
    ActionAdminHandler actions, PolicyAdminHandler policies, IAgentConnectionStore connections) : PageModel
{
    public IReadOnlyList<AgentListItem> Agents { get; private set; } = [];
    public IReadOnlyList<EnvironmentInfo> Environments { get; private set; } = [];
    public IReadOnlyList<CoverageRow> Coverage { get; private set; } = [];
    public ActivityPage Activity { get; private set; } = new([], 1, false);
    public bool HasAuthorization { get; private set; }
    public bool HasExecution { get; private set; }
    public bool IsError { get; private set; }
    public string? Message { get; private set; }
    public string? Secret { get; private set; }
    public string? PendingApprovalId { get; private set; }
    public IReadOnlyList<PolicySummary> AvailablePolicies { get; private set; } = [];
    [BindProperty] public string? PolicyId { get; set; }
    [BindProperty(SupportsGet = true)] public string? AgentId { get; set; }
    [BindProperty(SupportsGet = true)] public string? ExampleAction { get; set; }
    [BindProperty] public string ExternalId { get; set; } = "";
    [BindProperty] public string DisplayName { get; set; } = "";
    [BindProperty] public string EnvironmentName { get; set; } = "";
    public bool CanRead(string permission) => HttpContext.GetConsoleUser()?.HasPermission(permission) == true;
    public async Task OnGetAsync(CancellationToken ct)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        await LoadAsync(ct);
    }
    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        try
        {
            DisplayName??="";
            if(string.IsNullOrWhiteSpace(ExternalId))
            {
                var stem=System.Text.RegularExpressions.Regex.Replace(DisplayName.ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-');
                if(stem.Length>40) stem=stem[..40].TrimEnd('-');
                if(stem.Length==0 || !char.IsAsciiLetter(stem[0])) stem="agent-"+stem;
                ExternalId=stem+"-"+Guid.NewGuid().ToString("N")[..8];
                ModelState.Remove(nameof(ExternalId));
            }
            var result = await admin.RunAsync(caller, "admin.agent.create", "agent", ExternalId, null, PermissionCatalog.AgentsWrite,
                c => agents.CreateAsync(new CreateAgentCommand(HttpContext.RequireOrganizationId(), HttpContext.RequireProductApplicationId(), ExternalId, DisplayName, EnvironmentName), c), ct);
            if (result.Allowed && result.Value is { } created)
            {
                AgentId = created.AgentId; Secret = created.ApiKey;
                Response.Headers.CacheControl = "no-store";
                Message = "Agente creado. Guarda la clave y elige la política que utilizará.";
            }
            else { PendingApprovalId = result.ApprovalId; Message = result.Reason; IsError = !result.Pending; }
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or ForbiddenException)
        { IsError = true; Message = ex.Message; }
        await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostAssignAsync(CancellationToken ct)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.ActionsWrite);
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        var org = HttpContext.RequireOrganizationId(); var app = HttpContext.RequireProductApplicationId();
        if (!(await agents.ListAsync(org, app, ct)).Any(a => a.Id == AgentId)) return NotFound();
        var policy = (await policies.ListAsync(org, app, ct)).FirstOrDefault(p => p.Id == PolicyId && p.PublishedVersionNumber is not null);
        if (policy is null) return NotFound();
        var action = (await actions.ListAsync(org, app, ct)).FirstOrDefault(a => a.Name == policy.Action && a.ResourceType == policy.ResourceType);
        if (action is null) { IsError = true; Message = "Register the policy's action before assigning this agent."; await LoadAsync(ct); return Page(); }
        try
        {
            var attributes = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["agent_id"] = System.Text.Json.JsonSerializer.SerializeToElement(AgentId),
                ["policy_id"] = System.Text.Json.JsonSerializer.SerializeToElement(policy.Id),
                ["application_id"] = System.Text.Json.JsonSerializer.SerializeToElement(app)
            };
            var outcome = await admin.RunAsync(caller, "admin.action.update", "action", action.Id, attributes, PermissionCatalog.ActionsWrite,
                async c =>
                {
                    await connections.ConnectAsync(org, app, AgentId!, action.Id, policy.Id, c);
                    return true;
                }, ct);
            Message = outcome.Allowed ? "Action and published policy assigned. You can now test this agent." : outcome.Reason;
            IsError = !outcome.Allowed && !outcome.Pending; PendingApprovalId = outcome.ApprovalId;
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or ForbiddenException)
        { IsError = true; Message = ex.Message; }
        await LoadAsync(ct); return Page();
    }
    private async Task LoadAsync(CancellationToken ct)
    {
        var org = HttpContext.RequireOrganizationId(); var app = HttpContext.RequireProductApplicationId();
        Agents = await agents.ListAsync(org, app, ct);
        Environments = await agents.ListEnvironmentsAsync(org, app, ct);
        if(string.IsNullOrWhiteSpace(EnvironmentName)) EnvironmentName=Environments.FirstOrDefault(e=>e.Name=="development")?.Name ?? Environments.FirstOrDefault()?.Name ?? "";
        if (CanRead(PermissionCatalog.PoliciesRead)) AvailablePolicies = (await policies.ListAsync(org, app, ct)).Where(p => p.PublishedVersionNumber is not null).ToList();
        if (!Agents.Any(a => a.Id == AgentId)) AgentId = null;
        if (AgentId is null) return;
        if (CanRead(PermissionCatalog.ActionsRead) && CanRead(PermissionCatalog.PoliciesRead))
            Coverage = await queries.CoverageAsync(org, app, AgentId, ct);
        if (CanRead(PermissionCatalog.AuditRead))
        {
            Activity = await queries.ActivityAsync(org, app, AgentId, null, 1, ct);
            HasAuthorization = await queries.AgentHasAuthorizationAsync(org,app,AgentId,ct);
            // Persisted progress is queried independently of the recent activity window.
            HasExecution = await queries.AgentHasEvidenceAsync(org, app, AgentId, ct);
        }
    }
}
