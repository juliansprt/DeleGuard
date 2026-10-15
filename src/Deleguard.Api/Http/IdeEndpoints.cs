using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;
namespace Deleguard.Api.Http;
public static class IdeEndpoints
{
    public static void MapIdeApi(this IEndpointRouteBuilder app)
    {
        var g=app.MapGroup("/v1/ide").WithTags("IDE control");
        g.MapGet("/config/{project}",async(HttpContext h,IIdeControl c,string project,CancellationToken ct)=>
        {
            var agent=Agent(h);var selected=await c.ConfigAsync(agent,project,ct);
            h.Response.Headers.CacheControl="no-store";
            return Results.Ok(new { selected.Id,selected.OrganizationId,selected.ApplicationId,selected.AgentId,selected.Name,
                selected.CaseSensitive,selected.ShareDiff,selected.ProtectedPathsJson,selected.DeniedPathsJson,selected.CreatedAt,
                agent_external_id=agent.AgentExternalId,environment=agent.EnvironmentName,environment_id=agent.EnvironmentId });
        });
        g.MapGet("/projects/{project}/decisions/{id}", async (HttpContext h, IIdeControl c, Deleguard.Application.Authorization.GetDecisionHandler decisions, string project, string id, CancellationToken ct) =>
        {
            var agent=Agent(h); await c.ConfigAsync(agent, project, ct);
            return Results.Ok(await decisions.HandleAsync(agent,id,ct,project));
        });
        g.MapPost("/sessions",async(HttpContext h,IIdeControl c,IdeSessionInput body,CancellationToken ct)=> {await c.RegisterSessionAsync(Agent(h),body,ct);return Results.Accepted();});
        g.MapPost("/permits/consume", async (HttpContext h, IExecutionControl execution, Deleguard.Application.Authorization.GetDecisionHandler decisions, IdeConsumeInput body, CancellationToken ct) =>
        {
            var a = Agent(h); var d = await decisions.HandleAsync(a, body.DecisionId, ct);
            if (d.PermitId is null || d.Operation is null) throw new ConflictException("A bound permit is required.");
            return Results.Ok(await execution.ConsumeAsync(a, d.PermitId, new(body.OperationId, d.Operation.Hash, body.SnapshotHash), ct));
        });
        g.MapGet("/projects",async(HttpContext h,IIdeControl c,CancellationToken ct)=> {h.EnsurePermission(PermissionCatalog.AgentsRead);return Results.Ok(await c.ProjectsAsync(h.RequireOrganizationId(),h.RequireProductApplicationId(),ct));});
        g.MapGet("/projects/{project}/progress",async(HttpContext h,IIdeControl c,string project,CancellationToken ct)=>
        {h.EnsurePermission(PermissionCatalog.AgentsRead);h.EnsurePermission(PermissionCatalog.AuditRead);h.Response.Headers.CacheControl="no-store";return Results.Ok(await c.ProgressAsync(h.RequireOrganizationId(),h.RequireProductApplicationId(),project,ct));});
        g.MapGet("/sessions",async(HttpContext h,IIdeControl c,CancellationToken ct)=> {h.EnsurePermission(PermissionCatalog.AuditRead);return Results.Ok(await c.SessionsAsync(h.RequireOrganizationId(),h.RequireProductApplicationId(),ct));});
        g.MapGet("/grants",async(HttpContext h,IIdeControl c,CancellationToken ct)=> {h.EnsurePermission(PermissionCatalog.ApprovalsRead);return Results.Ok(await c.GrantsAsync(h.RequireOrganizationId(),h.RequireProductApplicationId(),ct));});
        g.MapPost("/projects",async(HttpContext h,IIdeControl c,IAdminActionGateway admin,IdeProjectInput body,CancellationToken ct)=>await Write(h,admin,"ide.project.configure",body,false,()=>c.SaveProjectAsync(h.RequireOrganizationId(),h.RequireProductApplicationId(),body,h.RequireConsoleUser().Email,ct),ct));
        g.MapPost("/grants",async(HttpContext h,IIdeControl c,IAdminActionGateway admin,IdeGrantInput body,CancellationToken ct)=>await Write(h,admin,"ide.grant.create",body,true,()=>c.GrantAsync(h.RequireOrganizationId(),h.RequireProductApplicationId(),body,h.RequireConsoleUser().Email,ct),ct));
        g.MapPost("/grants/{id}/revoke",async(HttpContext h,IIdeControl c,IAdminActionGateway admin,string id,CancellationToken ct)=>await Write(h,admin,"ide.grant.revoke",new {id},true,async()=>{await c.RevokeAsync(h.RequireOrganizationId(),h.RequireProductApplicationId(),id,h.RequireConsoleUser().Email,ct);return true;},ct));
    }
    private static async Task<IResult> Write<T>(HttpContext h,IAdminActionGateway gateway,string operation,object body,bool grant,Func<Task<T>> execute,CancellationToken ct)
    {
        var caller=h.EnsurePermission(PermissionCatalog.ActionsWrite);h.EnsurePermission(PermissionCatalog.PoliciesWrite);
        if(grant)h.EnsurePermission(PermissionCatalog.ApprovalsResolve);
        // JSON APIs require an explicit administrative bearer; browser forms use Razor antiforgery.
        if(!h.Request.Headers.Authorization.ToString().StartsWith("Bearer ",StringComparison.Ordinal))throw new AgentAuthenticationException("Administrative bearer required.");
        var attrs=new Dictionary<string,System.Text.Json.JsonElement>{["operation"]=System.Text.Json.JsonSerializer.SerializeToElement(operation),["application_id"]=System.Text.Json.JsonSerializer.SerializeToElement(h.RequireProductApplicationId()),["request"]=System.Text.Json.JsonSerializer.SerializeToElement(body)};
        var result=await gateway.RunAsync(caller,"admin.action.update","action","ide-control",attrs,PermissionCatalog.ActionsWrite,_=>execute(),ct);
        if(result.Allowed)return Results.Ok(result.Value);
        return Results.Json(new {decision=result.Pending ? "REQUIRE_APPROVAL" : "DENY",approval_id=result.ApprovalId,reason=result.Reason},statusCode:result.Pending ? 202 : 403);
    }
    private static AgentPrincipal Agent(HttpContext h)=>h.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Agent credential required.");
}
