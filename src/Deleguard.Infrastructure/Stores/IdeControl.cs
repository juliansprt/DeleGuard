using System.Text.Json;
using System.Text.RegularExpressions;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Ide;
using Deleguard.Infrastructure.Persistence;
using Deleguard.Policy;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Stores;

public sealed class IdeControl(DeleguardDbContext db, IClock clock, IAuditLedger audit,
    IPolicyRepository policies, IPolicyEngine engine, IActionGrantStore actions, IActorDirectory actors) : IIdeControl
{
    private static readonly string[] SafeGrantActions = ["filesystem.read", "filesystem.list", "filesystem.search", "filesystem.create", "filesystem.write", "filesystem.move"];
    // This is intentionally closed. New IDE capabilities must be registered here, with a policy and fixtures,
    // before an adapter can obtain a permit for them.
    private static readonly HashSet<string> ControlledActions = new(StringComparer.Ordinal)
    {
        "filesystem.read", "filesystem.list", "filesystem.search", "filesystem.create", "filesystem.write", "filesystem.delete", "filesystem.move",
        "process.execute", "network.connect", "git.clone", "git.fetch", "git.commit", "git.push", "package.install", "service.manage",
        "credential.use", "mcp.discover", "mcp.call", "ide.settings.change", "extension.install", "debug.attach", "browser.navigate"
    };
    private static string[] Paths(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
    private static string? Text(IReadOnlyDictionary<string, JsonElement>? a, string key) => a is not null && a.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public static bool ValidPath(string? path) => path is not null && path.Length <= 2048 && (path == "." ||
        path.Length > 0 && !path.Contains('\\') && !path.Contains(':') && !path.Any(char.IsControl) &&
        path.Split('/').All(s => s.Length > 0 && s is not "." and not ".." && !s.EndsWith('.') && !s.EndsWith(' ')));
    private static bool Under(string path, string directory, bool sensitive) => directory == "." || path.Equals(directory, sensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) || path.StartsWith(directory + "/", sensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    private static bool Overlaps(string path, string directory, bool sensitive) => Under(path, directory, sensitive) || Under(directory, path, sensitive);
    private IQueryable<IdeProject> Projects(string org,string app) => db.Set<IdeProject>().Where(p=>p.OrganizationId==org && p.ApplicationId==app);
    public async Task<IReadOnlyList<IdeProject>> ProjectsAsync(string org,string app,CancellationToken ct) => await Projects(org,app).AsNoTracking().OrderBy(p=>p.Name).ToListAsync(ct);
    public async Task<IdeProject> ConfigAsync(AgentPrincipal a,string project,CancellationToken ct) => await Projects(a.OrganizationId,a.ApplicationId).AsNoTracking().SingleOrDefaultAsync(p=>p.Id==project && p.AgentId==a.AgentId,ct) ?? throw new NotFoundException("IDE project not found for this agent.");
    public async Task<IdeProject> SaveProjectAsync(string org,string app,IdeProjectInput i,string actor,CancellationToken ct)
    {
        if (!Regex.IsMatch(i.Id ?? "", "^[a-zA-Z0-9_-]{1,80}$") || string.IsNullOrWhiteSpace(i.Name) || i.Name.Length>120 || i.ProtectedPaths.Length>100 || i.DeniedPaths.Length>100 || i.ProtectedPaths.Concat(i.DeniedPaths).Any(p=>!ValidPath(p))) throw new RequestValidationException("Use a project id, name and canonical relative directories (or '.' for the project root).");
        if (!await db.Agents.AnyAsync(a=>a.OrganizationId==org && a.ApplicationId==app && a.Id==i.AgentId && a.Enabled,ct)) throw new NotFoundException("Agent not found.");
        var p=await Projects(org,app).SingleOrDefaultAsync(p=>p.Id==i.Id,ct);
        if(p is null) { p=new IdeProject { Id=i.Id!,OrganizationId=org,ApplicationId=app,CreatedAt=clock.UtcNow }; db.Add(p); }
        // Rebinding an existing project would make pending decisions and grants ambiguous.
        if(p.AgentId.Length>0 && (p.AgentId!=i.AgentId || p.CaseSensitive!=i.CaseSensitive)) throw new ConflictException("Create a new project to change its agent or filesystem case semantics.");
        p.Name=i.Name;p.AgentId=i.AgentId;p.CaseSensitive=i.CaseSensitive;p.ShareDiff=i.ShareDiff;
        p.ProtectedPathsJson=JsonSerializer.Serialize(i.ProtectedPaths.Distinct());
        p.DeniedPathsJson=JsonSerializer.Serialize(i.DeniedPaths.Concat(new[]{".deleguard",".cursor/hooks.json",".claude/settings.json",".github/hooks",".vscode/settings.json",".claude/settings.local.json"}).Distinct());
        await Log(org,"ide.project.updated",new {project_id=p.Id,application_id=app,actor},ct);
        await db.SaveChangesAsync(ct); return p;
    }
    public async Task RegisterSessionAsync(AgentPrincipal a,IdeSessionInput i,CancellationToken ct)
    {
        await ConfigAsync(a,i.ProjectId,ct);
        if(i.Id.Length is <1 or >120 || i.Adapter is not ("cursor" or "vscode" or "claude") || i.Version.Length>80 || i.Platform.Length>40 || i.Capabilities.Count>30 || i.Capabilities.Any(x=>x.Key.Length>80 || x.Value is not ("controlled" or "observed" or "uncovered"))) throw new RequestValidationException("Invalid adapter session.");
        var s=await db.Set<IdeSession>().SingleOrDefaultAsync(s=>s.OrganizationId==a.OrganizationId && s.ApplicationId==a.ApplicationId && s.AgentId==a.AgentId && s.Id==i.Id,ct);
        if(s is null) { s=new IdeSession {Id=i.Id,OrganizationId=a.OrganizationId,ApplicationId=a.ApplicationId,AgentId=a.AgentId,ProjectId=i.ProjectId};db.Add(s); }
        if(s.ProjectId!=i.ProjectId) throw new ConflictException("Session belongs to another project.");
        s.Adapter=i.Adapter;s.Version=i.Version;s.Platform=i.Platform;s.CapabilitiesJson=JsonSerializer.Serialize(i.Capabilities);s.LastSeenAt=clock.UtcNow;await db.SaveChangesAsync(ct);
    }
    public async Task<IReadOnlyList<IdeSession>> SessionsAsync(string org,string app,CancellationToken ct) => await db.Set<IdeSession>().AsNoTracking().Where(s=>s.OrganizationId==org && s.ApplicationId==app).OrderByDescending(s=>s.LastSeenAt).Take(100).ToListAsync(ct);
    public async Task<IdeProgress> ProgressAsync(string org,string app,string project,CancellationToken ct)
    {
        var p=await Projects(org,app).AsNoTracking().SingleOrDefaultAsync(p=>p.Id==project,ct) ?? throw new NotFoundException("Project not found.");
        var connection=await db.Set<IdeSession>().Where(s=>s.OrganizationId==org&&s.ApplicationId==app&&s.ProjectId==project&&s.AgentId==p.AgentId)
            .Select(s=>(DateTimeOffset?)s.LastSeenAt).MaxAsync(ct);
        var scope=JsonSerializer.Serialize(new{project_id=project});
        var simulations=db.AuditEvents.Where(e=>e.OrganizationId==org&&e.ApplicationId==app&&e.EventType=="authorization.decided"
            &&EF.Functions.JsonContains(e.PayloadJson,"{\"source\":\"playground\"}")).Select(e=>e.DecisionId);
        var decisions=db.AuthorizationDecisions.AsNoTracking().Where(d=>d.OrganizationId==org&&d.ApplicationId==app&&d.AgentId==p.AgentId
            &&!simulations.Contains(d.Id)&&EF.Functions.JsonContains(d.ResourceAttributesJson,scope)&&EF.Functions.JsonContains(d.ContextJson,"{\"source\":\"ide-hook\"}"));
        var last=await decisions.OrderByDescending(d=>d.CreatedAt).Select(d=>new{d.Id,d.CreatedAt}).FirstOrDefaultAsync(ct);
        var result=await db.ExecutionEvidence.Where(e=>e.OrganizationId==org&&decisions.Select(d=>d.Id).Contains(e.DecisionId)).Select(e=>(DateTimeOffset?)e.CreatedAt).MaxAsync(ct);
        return new(project,connection,last?.CreatedAt,last?.Id,result);
    }
    public async Task<IReadOnlyList<IdeGrant>> GrantsAsync(string org,string app,CancellationToken ct) => await db.Set<IdeGrant>().AsNoTracking().Where(g=>g.OrganizationId==org && g.ApplicationId==app).OrderByDescending(g=>g.ExpiresAt).Take(100).ToListAsync(ct);
    public async Task<IdeGrant> GrantAsync(string org,string app,IdeGrantInput i,string actor,CancellationToken ct)
    {
        var p=await Projects(org,app).SingleOrDefaultAsync(p=>p.Id==i.ProjectId,ct) ?? throw new NotFoundException("Project not found.");
        if(!ValidPath(i.Directory) || i.Minutes is <1 or >60 || i.Actions.Length==0 || i.Actions.Any(a=>!SafeGrantActions.Contains(a))) throw new RequestValidationException("Select filesystem actions and a duration between 1 and 60 minutes. Delete, process and MCP grants are not supported.");
        if(Paths(p.DeniedPathsJson).Any(d=>Under(i.Directory,d,p.CaseSensitive))) throw new ConflictException("An explicit deny cannot be bypassed.");
        var grant=new IdeGrant {Id=IdGenerator.New("igr"),OrganizationId=org,ApplicationId=app,ProjectId=p.Id,AgentId=p.AgentId,Directory=i.Directory,ActionsJson=JsonSerializer.Serialize(i.Actions.Distinct()),CreatedBy=actor,ExpiresAt=clock.UtcNow.AddMinutes(i.Minutes)};
        db.Add(grant);await Log(org,"ide.grant.created",new {grant_id=grant.Id,project_id=p.Id,application_id=app,actor,directory=i.Directory,actions=i.Actions,expires_at=grant.ExpiresAt},ct);await db.SaveChangesAsync(ct);return grant;
    }
    public async Task RevokeAsync(string org,string app,string id,string actor,CancellationToken ct)
    {
        var g=await db.Set<IdeGrant>().SingleOrDefaultAsync(g=>g.OrganizationId==org && g.ApplicationId==app && g.Id==id,ct) ?? throw new NotFoundException("Grant not found.");
        g.RevokedAt=clock.UtcNow;await Log(org,"ide.grant.revoked",new {grant_id=id,application_id=app,actor},ct);await db.SaveChangesAsync(ct);
    }
    public async Task<PolicyEvaluation> EvaluateAsync(AuthorizeCommand c,PolicyEvaluation e,CancellationToken ct)
    {
        if(!ControlledActions.Contains(c.Action)) return e;
        if(e.Effect==DecisionEffect.Deny) return e;
        PolicyEvaluation Deny(string reason) => e with {Effect=DecisionEffect.Deny,Reason=reason};
        var id=Text(c.ResourceAttributes,"project_id");
        var p=await Projects(c.Principal.OrganizationId,c.Principal.ApplicationId).AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id && p.AgentId==c.Principal.AgentId,ct);
        if(p is null) return Deny("IDE project is not registered for this agent.");
        var op=Text(c.ResourceAttributes,"operation_id");var hash=Text(c.ResourceAttributes,"operation_hash");
        if(string.IsNullOrWhiteSpace(op) || op.Length>120 || hash is null || !Regex.IsMatch(hash,"^[a-f0-9]{64}$")) return Deny("IDE operation identity and hash are required.");
        var isFilesystem=c.Action.StartsWith("filesystem.",StringComparison.Ordinal);
        var path=Text(c.ResourceAttributes,"path");var destination=Text(c.ResourceAttributes,"destination_path");
        if(isFilesystem && (!ValidPath(path) || c.Action=="filesystem.move" && !ValidPath(destination))) return Deny("IDE path is not canonical.");
        // Non-filesystem operations are never granted by a directory grant. The broker must bind its complete
        // typed operation into operation_hash; these stable identifiers prevent an unscoped policy from allowing it.
        if(!isFilesystem && string.IsNullOrWhiteSpace(Text(c.ResourceAttributes,"operation_kind"))) return Deny("IDE operation kind is required.");
        var touched=isFilesystem ? (destination is null ? new[]{path!} : new[]{path!,destination}) : Array.Empty<string>();
        var recursive=c.Action is "filesystem.search" or "filesystem.list" or "filesystem.move" or "filesystem.delete";
        bool Touches(string t,string d) => recursive ? Overlaps(t,d,p.CaseSensitive) : Under(t,d,p.CaseSensitive);
        if(touched.Any(t=>Paths(p.DeniedPathsJson).Any(d=>Touches(t,d)))) return Deny("IDE path is explicitly prohibited.");
        // A directory request may expose descendants. It cannot authorize a narrower child as a side effect.
        var protectedPath=touched.Any(t=>Paths(p.ProtectedPathsJson).Any(d=>Touches(t,d)));
        var needsReview=e.Effect==DecisionEffect.RequireApproval || protectedPath || !isFilesystem;
        if(!needsReview) return e;
        var grants=await db.Set<IdeGrant>().AsNoTracking().Where(g=>g.OrganizationId==p.OrganizationId && g.ApplicationId==p.ApplicationId && g.ProjectId==p.Id && g.AgentId==p.AgentId && g.RevokedAt==null && g.ExpiresAt>clock.UtcNow).ToListAsync(ct);
        var grant=e.Effect==DecisionEffect.Allow && SafeGrantActions.Contains(c.Action) ? grants.FirstOrDefault(g=>Paths(g.ActionsJson).Contains(c.Action) && touched.All(t=>Under(t,g.Directory,p.CaseSensitive))) : null;
        if(grant is not null) return e with {Effect=DecisionEffect.Allow,Reason="IDE temporary grant: "+grant.Id};
        return e with {Effect=DecisionEffect.RequireApproval,Reason=protectedPath ? "Access to a protected project directory requires review." : e.Reason,ApprovalExpiresIn=TimeSpan.FromMinutes(15),ApproverRoles=e.ApproverRoles.Count>0 ? e.ApproverRoles : new[]{"org-admin"}};
    }
    public Task<bool> CanReportAsync(AgentPrincipal a,string decisionId,string snapshot,DateTimeOffset expires,CancellationToken ct) =>
        db.Set<IdePermitUse>().AnyAsync(u=>u.OrganizationId==a.OrganizationId && u.ApplicationId==a.ApplicationId && u.AgentId==a.AgentId && u.DecisionId==decisionId && u.SnapshotHash==snapshot && u.ConsumedAt<expires,ct);
    public async Task<IdeConsumeResult> ConsumeAsync(AgentPrincipal a,IdeConsumeInput i,CancellationToken ct)
    {
        try { return await ConsumeCoreAsync(a,i,ct); }
        catch(Npgsql.PostgresException ex) when(ex.SqlState is "40001" or "40P01") {throw new ConflictException("Concurrent authorization change. Refresh the decision; do not execute on this response.");}
    }
    private async Task<IdeConsumeResult> ConsumeCoreAsync(AgentPrincipal a,IdeConsumeInput i,CancellationToken ct)
    {
        if(i.OperationId.Length is <1 or >120) throw new RequestValidationException("Invalid operation id.");
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var d=await db.AuthorizationDecisions.AsNoTracking().SingleOrDefaultAsync(d=>d.OrganizationId==a.OrganizationId && d.ApplicationId==a.ApplicationId && d.AgentId==a.AgentId && d.Id==i.DecisionId,ct) ?? throw new NotFoundException("Decision not found.");
        var attrs=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(d.ResourceAttributesJson)!;
        if(Text(attrs,"operation_id")!=i.OperationId || d.ResourceSnapshotHash!=i.SnapshotHash) throw new ConflictException("Operation or snapshot does not match authorization.");
        if(d.Decision!=DecisionEffect.Allow || d.PermitId is null) throw new ConflictException("A live ALLOW permit is required.");
        var permit=await db.Permits.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==d.PermitId && p.OrganizationId==a.OrganizationId && p.DecisionId==d.Id,ct);
        if(permit is null || !permit.IsActive(clock.UtcNow)) throw new ConflictException("Permit expired or revoked.");
        var action=await db.ActionDefinitions.AsNoTracking().SingleOrDefaultAsync(x=>x.OrganizationId==a.OrganizationId && x.ApplicationId==a.ApplicationId && x.Name==d.Action && x.ResourceType==d.ResourceType,ct);
        if(action is null || !await actions.HasAsync(a.OrganizationId,a.AgentId,action.Id,ct)) throw new ConflictException("Action assignment changed.");
        var published=await policies.GetPublishedAsync(a.OrganizationId,a.ApplicationId,d.Action,d.ResourceType,a.AgentId,ct);
        var context=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(d.ContextJson)!;
        var evaluation=engine.Evaluate(new AuthorizationEnvelope(a.AgentExternalId,a.EnvironmentName,d.ActorId,d.ActorType,d.Action,d.ResourceType,d.ResourceId,attrs,context,await actors.GetRolesAsync(a.OrganizationId,d.ActorId,ct)),published);
        if(evaluation.PolicyId!=d.PolicyId || evaluation.PolicyVersion!=d.PolicyVersion) throw new ConflictException("Policy changed; request a new authorization.");
        var fresh=await EvaluateAsync(new AuthorizeCommand(a,a.AgentExternalId,a.EnvironmentName,d.ActorId,d.ActorType,null,d.Action,d.ResourceType,d.ResourceId,attrs,d.ResourceSnapshotHash,context,d.IdempotencyKey),evaluation,ct);
        var human=await db.ApprovalRequests.AnyAsync(r=>r.OrganizationId==a.OrganizationId && r.DecisionId==d.Id && r.Status==Deleguard.Domain.Common.ApprovalStatus.Approved,ct);
        if(fresh.Effect==DecisionEffect.Deny || fresh.Effect==DecisionEffect.RequireApproval && !human) throw new ConflictException("Access changed or temporary grant expired; request authorization again.");
        var now=clock.UtcNow;
        var deadline=permit.ExpiresAt;
        const string grantPrefix="IDE temporary grant: ";
        if(fresh.Reason.StartsWith(grantPrefix,StringComparison.Ordinal)) {
            var grantId=fresh.Reason[grantPrefix.Length..];
            var validGrant=await db.Set<IdeGrant>().AsNoTracking().SingleOrDefaultAsync(g=>g.Id==grantId && g.OrganizationId==a.OrganizationId && g.ApplicationId==a.ApplicationId && g.AgentId==a.AgentId && g.RevokedAt==null,ct);
            if(validGrant is null)throw new ConflictException("Temporary grant was revoked.");
            if(validGrant.ExpiresAt<deadline)deadline=validGrant.ExpiresAt;
        }
        if(deadline<=clock.UtcNow)throw new ConflictException("Execution permission expired before consumption.");
        var inserted=await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ide_permit_uses ("DecisionId","OrganizationId","ApplicationId","AgentId","OperationId","SnapshotHash","ConsumedAt")
            SELECT {d.Id},{a.OrganizationId},{a.ApplicationId},{a.AgentId},{i.OperationId},{i.SnapshotHash},clock_timestamp() WHERE clock_timestamp() < {deadline} ON CONFLICT DO NOTHING
            """,ct);
        if(inserted==0) {var previous=await db.Set<IdePermitUse>().AsNoTracking().SingleOrDefaultAsync(x=>x.DecisionId==d.Id,ct);if(previous?.OperationId!=i.OperationId || previous.SnapshotHash!=i.SnapshotHash) throw new ConflictException("Operation already used by a different decision.");}
        await tx.CommitAsync(ct);return new(inserted==1,inserted==0);
    }
    private Task Log(string org,string kind,object data,CancellationToken ct) => audit.AppendAsync(org,kind,null,JsonSerializer.SerializeToElement(data),ct);
}
