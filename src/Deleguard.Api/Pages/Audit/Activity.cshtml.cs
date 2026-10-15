using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Deleguard.Api.Pages.Audit;
public sealed class ActivityModel(IControlQueries queries) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? AgentId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Filter { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public ActivityPage Activity { get; private set; } = new([], 1, false);
    public async Task OnGetAsync(CancellationToken ct)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AuditRead);
        Activity = await queries.ActivityAsync(HttpContext.RequireOrganizationId(), HttpContext.RequireProductApplicationId(), AgentId, Filter, PageNumber, ct);
    }
    public string PageUrl(int page) => $"/audit/activity?PageNumber={page}&Filter={Uri.EscapeDataString(Filter ?? "")}&AgentId={Uri.EscapeDataString(AgentId ?? "")}";
}
