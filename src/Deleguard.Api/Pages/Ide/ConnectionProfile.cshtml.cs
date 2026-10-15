using System.Text.Json;
using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Ide;

public sealed class ConnectionProfileModel(IIdeControl control, IAgentDirectory agents) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string project,string adapter="cursor",CancellationToken ct=default)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AgentsRead);
        Response.Headers.CacheControl="no-store";
        if(adapter is not ("cursor" or "vscode" or "claude"))return BadRequest();
        var org=HttpContext.RequireOrganizationId();var app=HttpContext.RequireProductApplicationId();
        var selected=(await control.ProjectsAsync(org,app,ct)).SingleOrDefault(p=>p.Id==project);
        if(selected is null)return NotFound();
        var agent=(await agents.ListAsync(org,app,ct)).SingleOrDefault(a=>a.Id==selected.AgentId && a.Enabled);
        if(agent is null)return NotFound();
        var profile=new{schema_version=1,server_url=$"{Request.Scheme}://{Request.Host}{Request.PathBase}",organization_id=org,application_id=app,
            project_id=selected.Id,project_name=selected.Name,agent_id=agent.Id,agent_external_id=agent.ExternalId,environment=agent.EnvironmentName,
            adapter,language=System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="es"?"es":"en"};
        return File(JsonSerializer.SerializeToUtf8Bytes(profile,new JsonSerializerOptions{WriteIndented=true}),"application/json","connection.deleguard.json");
    }
}
