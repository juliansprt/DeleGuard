using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Agents;

public sealed class DetailModel(IAgentDirectory agents, IControlQueries queries, IClock clock) : PageModel
{
    public AgentListItem Agent { get; private set; } = null!;
    public IReadOnlyList<CoverageRow> Coverage { get; private set; } = [];
    public ActivityPage Activity { get; private set; } = new([], 1, false);
    public decimal Spend { get; private set; }
    public bool CanSeeCoverage => HttpContext.GetConsoleUser() is { } u && u.HasPermission(PermissionCatalog.ActionsRead) && u.HasPermission(PermissionCatalog.PoliciesRead);
    public bool CanSeeActivity => HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.AuditRead) == true;
    public bool CanSeeSpend => HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.GatewayRead) == true;
    public async Task<IActionResult> OnGetAsync(string id, CancellationToken ct)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AgentsRead);
        var org = HttpContext.RequireOrganizationId(); var app = HttpContext.RequireProductApplicationId();
        var agent = (await agents.ListAsync(org, app, ct)).FirstOrDefault(a => a.Id == id);
        if (agent is null) return NotFound();
        Agent = agent;
        if (CanSeeCoverage) Coverage = await queries.CoverageAsync(org, app, id, ct);
        if (CanSeeActivity) Activity = await queries.ActivityAsync(org, app, id, null, 1, ct);
        if (CanSeeSpend) Spend = await queries.AgentSpendAsync(org, app, id, clock.UtcNow, ct);
        return Page();
    }
}
