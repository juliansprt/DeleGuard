using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Ide;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Deleguard.IntegrationTests;

public sealed class QuickStartGuardTests
{
    [Fact]
    public async Task Quick_start_and_resume_require_publication_permission_before_mutation()
    {
        var context=new DefaultHttpContext();
        context.Items[AuthExtensions.ConsoleItem]=true;
        context.Items[AuthExtensions.ConsoleUserItem]=new ConsoleUserPrincipal("org","user","user@example.test","Editor",[],
            ["agents.read","agents.write","actions.write","policies.write"],false);
        var page=new Deleguard.Api.Pages.Ide.IndexModel(null!,null!,null!,null!,null!,null!) {
            PageContext=new PageContext { HttpContext=context }
        };
        Assert.False(page.CanQuickStart);
        Assert.False(page.CanPublish);
        await Assert.ThrowsAsync<ForbiddenException>(()=>page.OnPostQuickStartAsync(default));
        await Assert.ThrowsAsync<ForbiddenException>(()=>page.OnPostApplyProfileAsync(default));
    }
}

public sealed class QuickStartExperienceTests(AdminAccessPostgresFixture postgres) : IClassFixture<AdminAccessPostgresFixture>
{
    private WebApplicationFactory<Program> Factory(GateTrace trace) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder=> {
        builder.UseSetting("ConnectionStrings:Postgres",postgres.ConnectionString);
        builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?> {
            ["ConnectionStrings:Postgres"]=postgres.ConnectionString,
            ["Oidc:Enabled"]="false", ["DemoAuth:AllowEmergencyConsoleToken"]="true",
            ["DemoAuth:ConsoleToken"]=DemoSeeder.DemoConsoleToken,
            ["DemoAuth:DemoOrganizationId"]=DemoSeeder.OrganizationId,
            ["DemoAuth:ApiKeyPepper"]="deleguard-dev-pepper",
            ["Permits:SigningKey"]="deleguard-dev-permit-hmac-key-change-in-prod-32"
        }));
        builder.ConfigureServices(services=> {
            services.RemoveAll<IAdminActionGateway>();
            services.AddScoped<IAdminActionGateway>(sp=>new TracedGateway(ActivatorUtilities.CreateInstance<AdminActionGateway>(sp),trace));
        });
    });

    [Fact]
    public async Task One_form_creates_scoped_project_agent_and_profile_and_preserves_safe_defaults()
    {
        var trace=new GateTrace(); await using var factory=Factory(trace);
        using var client=await ClientForNewApplication(factory);
        var (id,form)=await Form(client);
        form["ShareDiff"]="true"; form["ProtectedPaths"]="."; form["DeniedPaths"]="src"; form["Profile"]="tampered";
        using var response=await client.PostAsync("/ide?handler=QuickStart",new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("22/22 rules ready",html);
        Assert.Contains("ide-agent-key",html);
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var project=await db.Set<IdeProject>().SingleAsync(p=>p.Id==id);
        Assert.False(project.ShareDiff); Assert.Equal("[\"infra/production\"]",project.ProtectedPathsJson);
        Assert.DoesNotContain("\"src\"",project.DeniedPathsJson);
        Assert.Equal(1,await db.Agents.CountAsync(a=>a.ExternalId==id));
        Assert.Equal(22,trace.Calls.Count(c=>c.Action=="admin.policy.publish"));
        foreach(var call in trace.Calls.Where(c=>c.Action=="admin.policy.publish")) {
            Assert.Equal("policies.publish",call.Permission); Assert.StartsWith("pol_",call.ResourceId);
            Assert.Equal(project.ApplicationId,call.Attributes!["application_id"].GetString());
        }
        var saved=trace.Calls.Single(c=>c.Attributes is not null && c.Attributes.TryGetValue("operation",out var operation) && operation.GetString()=="ide.project.configure");
        var request=saved.Attributes!["request"];
        Assert.False(request.GetProperty("ShareDiff").GetBoolean());
        Assert.Equal("infra/production",request.GetProperty("ProtectedPaths").GetString());
        Assert.Equal("balanced-development",request.GetProperty("Profile").GetString());
        Assert.Equal(".deleguard",request.GetProperty("DeniedPaths").GetString());
        // A replay cannot create another agent, rotate a secret or duplicate policies.
        using var replay=await client.PostAsync("/ide?handler=QuickStart",new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        Assert.Equal(1,await db.Agents.CountAsync(a=>a.ExternalId==id));
        Assert.DoesNotContain("id=\"ide-agent-key\"",await replay.Content.ReadAsStringAsync());
        var reloaded=await client.GetStringAsync("/ide?project="+id);
        Assert.DoesNotContain("id=\"ide-agent-key\"",reloaded);
        Assert.Contains("value=\""+id+"\" selected=\"selected\"",reloaded);
    }

    [Theory]
    [InlineData("DENY")]
    [InlineData("REQUIRE_APPROVAL")]
    public async Task Publication_gate_cannot_be_bypassed_by_quick_start_or_resume(string decision)
    {
        var trace=new GateTrace { BlockPublication=decision }; await using var factory=Factory(trace);
        using var client=await ClientForNewApplication(factory); var (id,form)=await Form(client);
        var response=await client.PostAsync("/ide?handler=QuickStart",new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var project=await db.Set<IdeProject>().SingleAsync(p=>p.Id==id);
        var policies=await db.Policies.Where(p=>p.ApplicationId==project.ApplicationId).ToListAsync();
        Assert.Equal(22,policies.Count); Assert.All(policies,p=>Assert.Null(p.PublishedVersionId));
        Assert.Single(trace.Calls,c=>c.Action=="admin.policy.publish");
        var html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Project saved",html);
        if(decision=="REQUIRE_APPROVAL") Assert.Contains("/approvals/test-pending",html);
        var resume=new Dictionary<string,string> { ["ProjectId"]=id,["Profile"]="balanced-development",["dg_operation_id"]=Guid.NewGuid().ToString("N"),["__RequestVerificationToken"]=Token(html) };
        var resumed=await client.PostAsync("/ide?handler=ApplyProfile",new FormUrlEncodedContent(resume));
        Assert.Equal(HttpStatusCode.OK,resumed.StatusCode);
        Assert.False(await db.Policies.AnyAsync(p=>p.ApplicationId==project.ApplicationId && p.PublishedVersionId!=null));
    }

    [Fact]
    public async Task Invalid_input_and_csrf_fail_before_creating_resources_and_guide_requires_login()
    {
        var trace=new GateTrace(); await using var factory=Factory(trace);
        using var anonymous=factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect=false });
        Assert.Equal(HttpStatusCode.Redirect,(await anonymous.GetAsync("/start")).StatusCode);
        using var client=await ClientForNewApplication(factory); var (_,form)=await Form(client);
        var noToken=new Dictionary<string,string>(form); noToken.Remove("__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/ide?handler=QuickStart",new FormUrlEncodedContent(noToken))).StatusCode);
        form["Name"]="";
        var invalid=await client.PostAsync("/ide?handler=QuickStart",new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK,invalid.StatusCode); Assert.Empty(trace.Calls);
        var guide=await client.GetStringAsync("/start"); Assert.Contains("data-module-search",guide);
        Assert.DoesNotContain(DemoSeeder.DemoAgentKey,guide);
    }

    private static async Task<HttpClient> ClientForNewApplication(WebApplicationFactory<Program> factory)
    {
        var client=factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler()); client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",DemoSeeder.DemoConsoleToken);
        await using var scope=factory.Services.CreateAsyncScope();
        var app=await scope.ServiceProvider.GetRequiredService<ApplicationAdminHandler>().CreateAsync(DemoSeeder.OrganizationId,"UX test","ux-"+Guid.NewGuid().ToString("N"),default);
        client.DefaultRequestHeaders.Add("Cookie",ApplicationContextExtensions.CookieName+"="+app.Id);
        return client;
    }
    private static async Task<(string Id,Dictionary<string,string> Form)> Form(HttpClient client)
    {
        var html=await client.GetStringAsync("/ide?newProject=true");
        var id=Regex.Match(html,"name=\"ProjectId\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.StartsWith("ide-",id);
        return (id,new() { ["ProjectId"]=id,["Name"]="UX project",["Adapter"]="cursor",["EnvironmentName"]="production",["CaseSensitive"]="false",["dg_operation_id"]=Guid.NewGuid().ToString("N"),["__RequestVerificationToken"]=Token(html) });
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private sealed record GateCall(string Action,string ResourceId,string Permission,Dictionary<string,JsonElement>? Attributes);
    private sealed class GateTrace { public string? BlockPublication; public List<GateCall> Calls {get;}=[]; }
    private sealed class TracedGateway(IAdminActionGateway inner,GateTrace trace) : IAdminActionGateway
    {
        public Task<AdminActionOutcome<T>> RunAsync<T>(ConsoleUserPrincipal caller,string action,string resourceType,string resourceId,Dictionary<string,JsonElement>? attributes,string permission,Func<CancellationToken,Task<T>> execute,CancellationToken ct)
        {
            trace.Calls.Add(new(action,resourceId,permission,attributes));
            if(action=="admin.policy.publish" && trace.BlockPublication is { } decision)
                return Task.FromResult(new AdminActionOutcome<T>(decision,"test-decision","Blocked in regression test",decision=="REQUIRE_APPROVAL" ? "test-pending" : null,null,default));
            return inner.RunAsync(caller,action,resourceType,resourceId,attributes,permission,execute,ct);
        }
    }
}
