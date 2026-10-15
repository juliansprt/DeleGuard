using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages;

public sealed class StartModel(IControlQueries queries, IClock clock, IIdeControl? ide=null) : PageModel
{
    public IReadOnlyList<Deleguard.Domain.Ide.IdeProject> RecentProjects {get;private set;}=[];
    public bool? ConfigurationReady { get; private set; }
    public ControlSummary? Summary { get; private set; }
    public bool Can(string permission) => HttpContext.GetConsoleUser()?.HasPermission(permission) == true;
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!HttpContext.IsConsole()) return Redirect("/login");
        var org=HttpContext.RequireOrganizationId(); var app=HttpContext.RequireProductApplicationId();
        if(Can("agents.read")&&ide is not null)RecentProjects=(await ide.ProjectsAsync(org,app,ct)).OrderByDescending(p=>p.CreatedAt).Take(4).ToArray();
        if(Can("agents.read") && Can("actions.read") && Can("policies.read"))
        {
            var coverage=await queries.CoverageAsync(org,app,null,ct);
            ConfigurationReady=coverage.Any(r=>r.AgentEnabled && r.ActionAssigned && r.Policies.Count>0);
        }
        if(Can("audit.read")) Summary=await queries.SummaryAsync(org,app,clock.UtcNow,ct);
        return Page();
    }
}
