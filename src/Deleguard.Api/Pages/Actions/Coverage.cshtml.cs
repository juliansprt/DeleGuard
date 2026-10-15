using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Actions;

public sealed class CoverageModel(IControlQueries queries) : PageModel
{
    public IReadOnlyList<CoverageRow> Rows { get; private set; } = [];
    [BindProperty(SupportsGet = true)] public string? AgentId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Filter { get; set; }
    public async Task OnGetAsync(CancellationToken ct)
    {
        HttpContext.EnsurePermission(PermissionCatalog.ActionsRead);
        HttpContext.EnsurePermission(PermissionCatalog.AgentsRead);
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesRead);
        Rows = await queries.CoverageAsync(HttpContext.RequireOrganizationId(), HttpContext.RequireProductApplicationId(), AgentId, ct);
        if (Filter == "missing") Rows = Rows.Where(r => r.Policies.Count == 0 || !r.ActionAssigned || !r.AgentEnabled).ToList();
    }
}
