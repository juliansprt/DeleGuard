using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Ide;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Deleguard.Api.Pages.Ide;
public sealed class IndexModel(IIdeControl control, IAgentDirectory agents, IAdminActionGateway admin, Deleguard.Application.Ide.IdeTemplateHandler templates, Deleguard.Application.Identity.AgentAdminHandler agentAdmin, Deleguard.Application.Policies.PolicyAdminHandler policies) : PageModel
{
    public IReadOnlyList<IdeProject> Projects {get;private set;}=[];
    public IReadOnlyList<IdeSession> Sessions {get;private set;}=[];
    public IdeProgress? Progress {get;private set;}
    public IReadOnlyList<IdeGrant> Grants {get;private set;}=[];
    public IReadOnlyList<AgentListItem> Agents {get;private set;}=[];
    public Deleguard.Application.Ide.IdeTemplateResult? Prepared {get;private set;}
    public Deleguard.Application.Ide.IdeProfileResult? AppliedProfile {get;private set;}
    public IReadOnlyDictionary<string, IReadOnlyList<Deleguard.Application.Ide.IdeActionCoverage>> Coverage {get;private set;}=new Dictionary<string, IReadOnlyList<Deleguard.Application.Ide.IdeActionCoverage>>();
    public string? Message {get;private set;}
    public bool IsError {get;private set;}
    public string? Secret {get;private set;}
    public string? PendingApprovalId {get;private set;}
    public IReadOnlyList<EnvironmentInfo> Environments {get;private set;}=[];
    public IdeProject? SelectedProject => Projects.FirstOrDefault(p => p.Id == ProjectId);
    public AgentListItem? ProjectAgent => Agents.FirstOrDefault(a => a.Id == SelectedProject?.AgentId);
    public bool CanGenerateKey => HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.AgentsWrite) == true && ProjectAgent?.Enabled == true;
    public bool ProfileReady => SelectedProject is not null && Coverage.TryGetValue(ProjectId, out var items) && items.Count > 0 && items.All(x => x.IsReady);
    public bool CanPublish => CanWrite && HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.PoliciesPublish)==true;
    public bool CanQuickStart => CanPublish && HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.AgentsWrite)==true;
    private string? publicationMessage;
    private bool publicationBlocked;
    public bool CanWrite => HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.PoliciesWrite)==true && HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.ActionsWrite)==true;
    public bool CanGrant => CanWrite && HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.ApprovalsResolve)==true;
    [BindProperty] public string ProjectId {get;set;}="";
    [BindProperty] public string Name {get;set;}="";
    [BindProperty(SupportsGet=true)] public string AgentId {get;set;}="";
    [BindProperty] public bool CaseSensitive {get;set;}
    [BindProperty] public bool ShareDiff {get;set;}
    [BindProperty] public string ProtectedPaths {get;set;}="infra/production";
    [BindProperty] public string DeniedPaths {get;set;}=".deleguard";
    [BindProperty] public string Directory {get;set;}="infra/production";
    [BindProperty] public string[] Actions {get;set;}=["filesystem.read"];
    [BindProperty] public int Minutes {get;set;}=15;
    [BindProperty] public string Profile {get;set;}="balanced-development";
    [BindProperty(SupportsGet=true)] public string Adapter {get;set;}="cursor";
    [BindProperty] public string EnvironmentName {get;set;}="";
    public async Task OnGetAsync(string? project,bool newProject,CancellationToken ct)
    {
        await Load(ct);
        var selected=newProject ? null : project is not null ? Projects.FirstOrDefault(p=>p.Id==project) : Projects.OrderByDescending(p=>p.CreatedAt).FirstOrDefault();
        if(selected is null) { ProjectId="ide-"+Guid.NewGuid().ToString("N"); return; }
        ProjectId=selected.Id;Name=selected.Name;AgentId=selected.AgentId;
        CaseSensitive=selected.CaseSensitive;ShareDiff=selected.ShareDiff;
        ProtectedPaths=string.Join(Environment.NewLine,System.Text.Json.JsonSerializer.Deserialize<string[]>(selected.ProtectedPathsJson) ?? []);
        DeniedPaths=string.Join(Environment.NewLine,System.Text.Json.JsonSerializer.Deserialize<string[]>(selected.DeniedPathsJson) ?? []);
        if(HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.AuditRead)==true)Progress=await control.ProgressAsync(HttpContext.RequireOrganizationId(),HttpContext.RequireProductApplicationId(),selected.Id,ct);
    }
    public async Task<IActionResult> OnPostQuickStartAsync(CancellationToken ct)
    {
        var caller=HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        HttpContext.EnsurePermission(PermissionCatalog.AgentsRead);
        HttpContext.EnsurePermission(PermissionCatalog.ActionsWrite);
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesPublish);
        Response.Headers.CacheControl="no-store";
        var org=HttpContext.RequireOrganizationId(); var app=HttpContext.RequireProductApplicationId();
        try
        {
            if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length>120)
                throw new RequestValidationException("Escribe un nombre de proyecto de hasta 120 caracteres.");
            ProjectId??="";
            if (!System.Text.RegularExpressions.Regex.IsMatch(ProjectId, "^ide-[a-f0-9]{32}$") ||
                Adapter is not ("cursor" or "vscode" or "claude") ||
                ModelState.TryGetValue(nameof(CaseSensitive), out var caseEntry) && caseEntry.Errors.Count>0)
                throw new RequestValidationException("Revisa el IDE y la configuración del proyecto y vuelve a intentarlo.");
            if ((await control.ProjectsAsync(org,app,ct)).Any(p=>p.Id==ProjectId))
                throw new ConflictException("Este proyecto ya está guardado. Continúa con su conexión o retoma el perfil pendiente.");
            Name=Name.Trim();
            Profile="balanced-development"; ShareDiff=false; ProtectedPaths="infra/production"; DeniedPaths=".deleguard";
            foreach(var field in new[]{nameof(Profile),nameof(ShareDiff),nameof(ProtectedPaths),nameof(DeniedPaths)}) ModelState.Remove(field);
            // Validate and load everything needed before creating the first resource.
            var profileTemplates=await ReadTemplates(ct);
            var available=await agents.ListAsync(org,app,ct);
            if (string.IsNullOrWhiteSpace(AgentId))
            {
                var environments=await agents.ListEnvironmentsAsync(org,app,ct);
                if (!environments.Any(e=>e.Name==EnvironmentName))
                    throw new RequestValidationException("Selecciona un entorno disponible para esta aplicación.");
                var attributes=new Dictionary<string,System.Text.Json.JsonElement> {
                    ["application_id"]=System.Text.Json.JsonSerializer.SerializeToElement(app),
                    ["environment"]=System.Text.Json.JsonSerializer.SerializeToElement(EnvironmentName)
                };
                var created=await admin.RunAsync(caller,"admin.agent.create","agent",ProjectId,attributes,PermissionCatalog.AgentsWrite,
                    c=>agentAdmin.CreateAsync(new(org,app,ProjectId,Name+" · IDE",EnvironmentName),c),ct);
                if (!created.Allowed || created.Value is null)
                {
                    PendingApprovalId=created.ApprovalId; IsError=!created.Pending;
                    Message=created.Pending ? "La creación del agente requiere revisión administrativa. No se ha creado el proyecto; vuelve a intentar la configuración cuando esté autorizada." : created.Reason;
                    await Load(ct); return Page();
                }
                AgentId=created.Value.AgentId; Secret=created.Value.ApiKey;
                ModelState.Remove(nameof(AgentId));
            }
            else if (!available.Any(a=>a.Id==AgentId && a.Enabled))
                throw new RequestValidationException("Elige un agente activo de esta aplicación.");
            // Each administrative operation keeps its own policy gate. Partial setup remains resumable.
            var saved=await Write(actor=>control.SaveProjectAsync(org,app,new(ProjectId,Name,AgentId,CaseSensitive,false,["infra/production"],[".deleguard"]),actor,ct),false,ct,"ide.project.configure");
            if (saved)
            {
                await Write(async actor=> {
                    AppliedProfile=await templates.ApplyBalancedProfileAsync(new(org,app,ProjectId,AgentId,profileTemplates),ct,PublishProfilePolicy);
                    return AppliedProfile;
                },false,ct,"ide.profile.apply");
                if (AppliedProfile is not null) SetProfileMessage();
            }
        }
        catch(Exception ex) when(ex is RequestValidationException or ConflictException or NotFoundException)
        { Message=ex.Message; IsError=true; }
        await Load(ct); return Page();
    }
    private static async Task<IReadOnlyList<Deleguard.Application.Ide.IdePolicyTemplate>> ReadTemplates(CancellationToken ct)
    {
        var folder=Path.Combine(AppContext.BaseDirectory,"policies");
        var items=new List<Deleguard.Application.Ide.IdePolicyTemplate>();
        foreach(var file in System.IO.Directory.GetFiles(folder,"ide-*.yaml").Order())
            items.Add(new(await System.IO.File.ReadAllTextAsync(file,ct),await System.IO.File.ReadAllTextAsync(Path.Combine(folder,"fixtures",Path.GetFileNameWithoutExtension(file)+".json"),ct)));
        if(items.Count==0) throw new RequestValidationException("El perfil inicial no está disponible en esta instalación. Contacta con tu administrador.");
        return items;
    }
    private void SetProfileMessage()
    {
        IsError=AppliedProfile?.IsReady!=true;
        Message=publicationMessage ?? (!IsError ? "Proyecto preparado. Guarda la clave y conecta tu IDE en el paso 2." : "Proyecto guardado. Algunas reglas requieren revisión; puedes retomar la preparación sin reemplazar tus reglas personalizadas.");
    }
    private async Task<bool> PublishProfilePolicy(Deleguard.Application.Policies.PublishPolicyCommand command,CancellationToken ct)
    {
        // Authorize each concrete policy/version; the action-configuration gate is not publication permission.
        if(publicationBlocked) return false;
        var caller=HttpContext.EnsurePermission(PermissionCatalog.PoliciesPublish);
        var attributes=new Dictionary<string,System.Text.Json.JsonElement> {
            ["application_id"]=System.Text.Json.JsonSerializer.SerializeToElement(HttpContext.RequireProductApplicationId()),
            ["project_id"]=System.Text.Json.JsonSerializer.SerializeToElement(ProjectId),
            ["version_id"]=System.Text.Json.JsonSerializer.SerializeToElement(command.VersionId)
        };
        var result=await admin.RunAsync(caller,"admin.policy.publish","policy",command.PolicyId,attributes,PermissionCatalog.PoliciesPublish,
            c=>policies.PublishAsync(command,c),ct);
        if(result.Allowed) return true;
        publicationBlocked=true; PendingApprovalId=result.ApprovalId;
        publicationMessage=result.Pending ? "Proyecto guardado; la publicación requiere revisión administrativa. Los borradores pendientes no autorizan. Revisa la solicitud; este flujo no reanuda cambios automáticamente." : "Proyecto guardado; la publicación no fue autorizada. "+result.Reason;
        return false;
    }
    public async Task<IActionResult> OnPostGenerateKeyAsync(string? currentKeyPrefix, CancellationToken ct)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AgentsRead);
        var caller = HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        Response.Headers.CacheControl = "no-store";
        var org = HttpContext.RequireOrganizationId();
        var app = HttpContext.RequireProductApplicationId();
        try
        {
            // Resolve the agent from the scoped project, never from a posted AgentId.
            var project = (await control.ProjectsAsync(org, app, ct)).SingleOrDefault(p => p.Id == ProjectId);
            if (project is null) return NotFound();
            var agent = await KeyAgent(project, org, app, currentKeyPrefix, ct);
            var attributes = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["application_id"] = System.Text.Json.JsonSerializer.SerializeToElement(app),
                ["project_id"] = System.Text.Json.JsonSerializer.SerializeToElement(project.Id)
            };
            var outcome = await admin.RunAsync(caller, "admin.agent.rotate_key", "agent", agent.Id,
                attributes, PermissionCatalog.AgentsWrite,
                cancellationToken => agentAdmin.RotateKeyAsync(org, app, project.AgentId, currentKeyPrefix, cancellationToken), ct);
            if (outcome.Allowed && outcome.Value is { } created)
            {
                Secret = created.ApiKey;
                Message = "API key generated. Copy it now and paste it when the installer asks for your agent key.";
            }
            else
            {
                PendingApprovalId = outcome.ApprovalId;
                IsError = !outcome.Pending;
                Message = outcome.Pending
                    ? "Key generation requires administrative review. No key was changed. Review the request, then try again; it does not resume automatically."
                    : outcome.Reason ?? "Denied by policy.";
            }
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or NotFoundException)
        { Message = ex.Message; IsError = true; }
        await Load(ct);
        return Page();
    }

    private async Task<AgentListItem> KeyAgent(IdeProject project, string org, string app,
        string? currentKeyPrefix, CancellationToken ct)
    {
        var agent = (await agents.ListAsync(org, app, ct)).SingleOrDefault(a => a.Id == project.AgentId)
            ?? throw new NotFoundException("Agent not found.");
        if (!agent.Enabled) throw new RequestValidationException("This project's agent is disabled. Enable it before generating an API key.");
        if (!string.Equals(agent.ActiveKeyPrefix ?? "", currentKeyPrefix ?? "", StringComparison.Ordinal))
            throw new ConflictException("The API key has changed since this page was opened. Review the current key before generating another.");
        return agent;
    }

    public async Task<IActionResult> OnPostPrepareAsync(CancellationToken ct)
    {
        await Write(async actor=>{
            var project=(await control.ProjectsAsync(HttpContext.RequireOrganizationId(),HttpContext.RequireProductApplicationId(),ct)).SingleOrDefault(p=>p.Id==ProjectId) ?? throw new NotFoundException("Project not found.");
            var folder=Path.Combine(AppContext.BaseDirectory,"policies");
            var items=new List<Deleguard.Application.Ide.IdePolicyTemplate>();
            foreach(var file in System.IO.Directory.GetFiles(folder,"ide-*.yaml"))
                items.Add(new(await System.IO.File.ReadAllTextAsync(file,ct),await System.IO.File.ReadAllTextAsync(Path.Combine(folder,"fixtures",Path.GetFileNameWithoutExtension(file)+".json"),ct)));
            Prepared=await templates.PrepareAsync(new(HttpContext.RequireOrganizationId(),HttpContext.RequireProductApplicationId(),ProjectId,project.AgentId,items),ct);return Prepared;
        },false,ct);
        await Load(ct);return Page();
    }
    public async Task<IActionResult> OnPostApplyProfileAsync(CancellationToken ct)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesPublish);
        if (!string.Equals(Profile, "balanced-development", StringComparison.Ordinal))
        {
            Message = "Este perfil todavía no está disponible. Elige Desarrollo equilibrado."; IsError = true;
            await Load(ct); return Page();
        }
        await Write(async actor =>
        {
            var project=(await control.ProjectsAsync(HttpContext.RequireOrganizationId(),HttpContext.RequireProductApplicationId(),ct)).SingleOrDefault(p=>p.Id==ProjectId) ?? throw new NotFoundException("Project not found.");
            var folder=Path.Combine(AppContext.BaseDirectory,"policies");
            var items=System.IO.Directory.GetFiles(folder,"ide-*.yaml").Select(file => new Deleguard.Application.Ide.IdePolicyTemplate(
                System.IO.File.ReadAllText(file), System.IO.File.ReadAllText(Path.Combine(folder,"fixtures",Path.GetFileNameWithoutExtension(file)+".json")))).ToList();
            AppliedProfile=await templates.ApplyBalancedProfileAsync(new(HttpContext.RequireOrganizationId(),HttpContext.RequireProductApplicationId(),ProjectId,project.AgentId,items),ct,PublishProfilePolicy);return AppliedProfile;
        },false,ct);
        if (AppliedProfile is not null) SetProfileMessage();
        await Load(ct);return Page();
    }
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        await Write(async actor=>await control.SaveProjectAsync(HttpContext.RequireOrganizationId(),HttpContext.RequireProductApplicationId(),new(ProjectId,Name,AgentId,CaseSensitive,ShareDiff,Lines(ProtectedPaths),Lines(DeniedPaths)),actor,ct),false,ct);
        await Load(ct);return Page();
    }
    public async Task<IActionResult> OnPostGrantAsync(CancellationToken ct)
    {
        await Write(async actor=>await control.GrantAsync(HttpContext.RequireOrganizationId(),HttpContext.RequireProductApplicationId(),new(ProjectId,Directory,Actions,Minutes),actor,ct),true,ct);
        await Load(ct);return Page();
    }
    public async Task<IActionResult> OnPostRevokeAsync(string id,CancellationToken ct)
    {
        var selectedProjectId=ProjectId;
        ProjectId=id;
        await Write(async actor=>{await control.RevokeAsync(HttpContext.RequireOrganizationId(),HttpContext.RequireProductApplicationId(),id,actor,ct);return true;},true,ct);
        ProjectId=selectedProjectId;
        await Load(ct);return Page();
    }
    private async Task<bool> Write<T>(Func<string,Task<T>> work,bool grant,CancellationToken ct,string? operationName=null)
    {
        var caller=HttpContext.EnsurePermission(PermissionCatalog.ActionsWrite);
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        if(grant)HttpContext.EnsurePermission(PermissionCatalog.ApprovalsResolve);
        try {
            var handler=HttpContext.Request.Query["handler"].ToString();
            var operation=operationName ?? (grant ? handler=="Revoke" ? "ide.grant.revoke" : "ide.grant.create" : handler=="Prepare" ? "ide.templates.prepare" : handler=="ApplyProfile" ? "ide.profile.apply" : "ide.project.configure");
            var attributes=new Dictionary<string,System.Text.Json.JsonElement>{["project_id"]=System.Text.Json.JsonSerializer.SerializeToElement(ProjectId),["application_id"]=System.Text.Json.JsonSerializer.SerializeToElement(HttpContext.RequireProductApplicationId()),["operation"]=System.Text.Json.JsonSerializer.SerializeToElement(operation),["request"]=System.Text.Json.JsonSerializer.SerializeToElement(new {ProjectId,Profile,Name,AgentId,CaseSensitive,ShareDiff,ProtectedPaths,DeniedPaths,Directory,Actions,Minutes})};
            var outcome=await admin.RunAsync(caller,"admin.action.update","action", "ide-control",attributes,PermissionCatalog.ActionsWrite,_=>work(caller.Email),ct);
            Message=outcome.Allowed ? "Configuración guardada. Continúa con el siguiente paso de la guía; guardar no conecta el IDE automáticamente." : outcome.Pending ? "Se solicitó revisión administrativa en Approvals. El cambio aún no se aplicó y este flujo administrativo no lo reanuda automáticamente." : outcome.Reason;
            IsError=!outcome.Allowed && !outcome.Pending;
            PendingApprovalId??=outcome.ApprovalId;
            return outcome.Allowed;
        } catch(Exception ex) when(ex is RequestValidationException or ConflictException or NotFoundException) {Message=ex.Message;IsError=true;return false;}
    }
    private async Task Load(CancellationToken ct)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AgentsRead);
        var org=HttpContext.RequireOrganizationId();var app=HttpContext.RequireProductApplicationId();
        Projects=await control.ProjectsAsync(org,app,ct);Agents=await agents.ListAsync(org,app,ct);
        Environments=await agents.ListEnvironmentsAsync(org,app,ct);
        if(string.IsNullOrEmpty(EnvironmentName)) EnvironmentName=Environments.FirstOrDefault(e=>e.Name=="development")?.Name ?? Environments.FirstOrDefault()?.Name ?? "";
        if(Adapter is not ("cursor" or "vscode" or "claude")) Adapter="cursor";
        if(SelectedProject is { } selected && (!IsError || HttpContext.Request.Query["handler"]!="Save"))
        {
            Name=selected.Name; AgentId=selected.AgentId; CaseSensitive=selected.CaseSensitive; ShareDiff=selected.ShareDiff;
            ProtectedPaths=string.Join(Environment.NewLine,System.Text.Json.JsonSerializer.Deserialize<string[]>(selected.ProtectedPathsJson) ?? []);
            DeniedPaths=string.Join(Environment.NewLine,System.Text.Json.JsonSerializer.Deserialize<string[]>(selected.DeniedPathsJson) ?? []);
            foreach(var field in new[]{nameof(Name),nameof(AgentId),nameof(CaseSensitive),nameof(ShareDiff),nameof(ProtectedPaths),nameof(DeniedPaths)}) ModelState.Remove(field);
        }
        if(HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.AuditRead)==true)Sessions=await control.SessionsAsync(org,app,ct);
        if(HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.ApprovalsRead)==true)Grants=await control.GrantsAsync(org,app,ct);
        if(HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.PoliciesRead)==true &&
           HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.ActionsRead)==true)
        {
            var coverage=new Dictionary<string,IReadOnlyList<Deleguard.Application.Ide.IdeActionCoverage>>();
            foreach(var project in Projects)
                coverage[project.Id]=await templates.CoverageAsync(org,app,project.Id,project.AgentId,ct);
            Coverage=coverage;
        }
    }
    private static string[] Lines(string? s)=>(s ?? "").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
}
