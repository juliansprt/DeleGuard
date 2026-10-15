using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Policies;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Deleguard.IntegrationTests;
public sealed class IdeControlTests(AdminAccessPostgresFixture postgres) : IClassFixture<AdminAccessPostgresFixture>
{
    [Fact]
    public async Task Protected_access_requires_review_consumes_once_and_grants_are_scoped_and_revocable()
    {
        await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
            ["ConnectionStrings:Postgres"]=postgres.ConnectionString,["DemoAuth:ConsoleToken"]=DemoSeeder.DemoConsoleToken,["DemoAuth:DemoOrganizationId"]=DemoSeeder.OrganizationId,["DemoAuth:ApiKeyPepper"]="deleguard-dev-pepper",["Permits:SigningKey"]="deleguard-dev-permit-hmac-key-change-in-prod-32"})));
        using var agent=factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());agent.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",DemoSeeder.DemoAgentKey);
        using var console=factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());console.DefaultRequestHeaders.Add("Cookie", "dg_ui_language=es");console.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",DemoSeeder.DemoConsoleToken);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();var control=scope.ServiceProvider.GetRequiredService<IIdeControl>();
        Assert.Equal(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString).Port, new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Port);
        var org=DemoSeeder.OrganizationId;var app=DemoSeeder.ApplicationId;var aid=DemoSeeder.AgentId;
        var key=Guid.NewGuid().ToString("N");var policy="ide-read-"+key;var version="pv_"+key;var action="act_"+key;
        db.ActionDefinitions.Add(new ActionDefinition {Id=action,OrganizationId=org,ApplicationId=app,Name="filesystem.read",ResourceType="filesystem",Risk=RiskLevel.Medium,CreatedAt=DateTimeOffset.UtcNow});
        db.AgentActionGrants.Add(new AgentActionGrant {OrganizationId=org,ApplicationId=app,AgentId=aid,ActionDefinitionId=action});
        db.Policies.Add(new PolicyDefinition {Id=policy,OrganizationId=org,ApplicationId=app,Slug=policy,PublishedVersionId=version});
        db.PolicyVersions.Add(new PolicyVersion {Id=version,OrganizationId=org,PolicyId=policy,Version=1,Published=true,Action="filesystem.read",ResourceType="filesystem",Yaml=$"id: {policy}\nversion: 1\npriority: 1000\nmatch:\n  action: filesystem.read\n  resource_type: filesystem\nrules:\n  - name: allowed\n    when: true\n    effect: ALLOW\nfallback: DENY\n"});
        db.AgentPolicyGrants.Add(new AgentPolicyGrant {OrganizationId=org,ApplicationId=app,AgentId=aid,PolicyId=policy});await db.SaveChangesAsync();
        await control.SaveProjectAsync(org,app,new("ide-test","IDE test",aid,false,false,["infra/production"],["secret"]),"admin",default);
        async Task<JsonElement> Authorize(string path,string operation)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,"/v1/authorize"){Content=JsonContent.Create(new {agent=new{id="invoice-agent",environment="production"},action="filesystem.read",resource=new{type="filesystem",id="ide-test:"+path,snapshot_hash=new string('a',64),attributes=new{project_id="ide-test",path,operation_id=operation,operation_hash=new string('b',64)}},context=new{source="ide-hook"}})};
            request.Headers.Add("Idempotency-Key","ide_"+operation);var res=await agent.SendAsync(request);Assert.True(res.IsSuccessStatusCode,await res.Content.ReadAsStringAsync());return await res.Content.ReadFromJsonAsync<JsonElement>();
        }
        // Reproduce the setup failure: assigned action and policy, but the policy is still a draft.
        var initialPolicy = await db.Policies.SingleAsync(p => p.Id == policy);
        var initialVersion = await db.PolicyVersions.SingleAsync(p => p.Id == version);
        initialPolicy.PublishedVersionId = null; initialVersion.Published = false;
        await db.SaveChangesAsync();
        var missingOperation = Guid.NewGuid().ToString("N");
        var missing = await Authorize("infra/production/prueba.txt", missingOperation);
        Assert.Equal("DENY", missing.GetProperty("decision").GetString());
        Assert.Equal("No applicable policy.", missing.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("approval_id").ValueKind);
        var missingPage = WebUtility.HtmlDecode(await console.GetStringAsync("/audit/decisions/" + missing.GetProperty("decision_id").GetString()));
        Assert.Contains("Falta una política publicada aplicable", missingPage);
        Assert.Contains("Los borradores no autorizan operaciones", missingPage);
        Assert.Contains("no hay una aprobación pendiente", missingPage);
        initialPolicy.PublishedVersionId = version; initialVersion.Published = true;
        await db.SaveChangesAsync();
        // Publishing must not rewrite the historical decision or bypass idempotency.
        var sameRequest = await Authorize("infra/production/prueba.txt", missingOperation);
        Assert.Equal(missing.GetProperty("decision_id").GetString(), sameRequest.GetProperty("decision_id").GetString());
        Assert.Equal("DENY", sameRequest.GetProperty("decision").GetString());
        var newRequest = await Authorize("infra/production/prueba.txt", Guid.NewGuid().ToString("N"));
        Assert.Equal("REQUIRE_APPROVAL", newRequest.GetProperty("decision").GetString());
        Assert.NotNull(newRequest.GetProperty("approval_id").GetString());
        var op=Guid.NewGuid().ToString("N");var pending=await Authorize("infra/production/config.yaml",op);Assert.Equal("REQUIRE_APPROVAL",pending.GetProperty("decision").GetString());
        var decision=pending.GetProperty("decision_id").GetString()!;
        async Task<HttpResponseMessage> Consume(string operation)=>await agent.PostAsJsonAsync("/v1/ide/permits/consume",new {decision_id=decision,operation_id=operation,snapshot_hash=new string('a',64)});
        Assert.Equal(HttpStatusCode.Conflict,(await Consume(op)).StatusCode);
        var approved=await console.PostAsJsonAsync("/v1/approvals/"+pending.GetProperty("approval_id").GetString()+"/approve",new {note="test"});Assert.True(approved.IsSuccessStatusCode,await approved.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict,(await Consume("wrong")).StatusCode);
        var first=await Consume(op);Assert.True(first.IsSuccessStatusCode,await first.Content.ReadAsStringAsync());var firstResult = await first.Content.ReadFromJsonAsync<JsonElement>();Assert.True(firstResult.GetProperty("acquired").GetBoolean());
        var second=await Consume(op);Assert.False((await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("acquired").GetBoolean());
        var consumedAt=DateTimeOffset.UtcNow.AddMinutes(-2);var expiresAt=DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ide_permit_uses SET \"ConsumedAt\"={consumedAt} WHERE \"DecisionId\"={decision}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE permits SET \"ExpiresAt\"={expiresAt} WHERE \"DecisionId\"={decision}");
        var lateEvidence=await agent.PostAsJsonAsync("/v1/executions",new {decision_id=decision,execution_id=firstResult.GetProperty("execution_id").GetString(),status="SUCCESS",resource_snapshot_hash=new string('a',64),metadata=new {source="ide-hook"}});
        Assert.Equal(HttpStatusCode.Accepted,lateEvidence.StatusCode);
        var wrongEvidence=await agent.PostAsJsonAsync("/v1/executions",new {decision_id=decision,status="SUCCESS",resource_snapshot_hash="wrong"});
        Assert.Equal(HttpStatusCode.Conflict,wrongEvidence.StatusCode);
        var denied=await Authorize("secret/config",Guid.NewGuid().ToString("N"));Assert.Equal("DENY",denied.GetProperty("decision").GetString());
        var grant=await control.GrantAsync(org,app,new("ide-test","infra/production",["filesystem.read"],15),"admin",default);
        var grantOperation=Guid.NewGuid().ToString("N");var allowed=await Authorize("INFRA/production/config",grantOperation);Assert.Equal("ALLOW",allowed.GetProperty("decision").GetString());
        await control.RevokeAsync(org,app,grant.Id,"admin",default);
        var expiredConsume=await agent.PostAsJsonAsync("/v1/ide/permits/consume",new {decision_id=allowed.GetProperty("decision_id").GetString(),operation_id=grantOperation,snapshot_hash=new string('a',64)});
        Assert.Equal(HttpStatusCode.Conflict,expiredConsume.StatusCode);
        var after=await Authorize("infra/production/config",Guid.NewGuid().ToString("N"));Assert.Equal("REQUIRE_APPROVAL",after.GetProperty("decision").GetString());
        await Assert.ThrowsAsync<RequestValidationException>(()=>control.GrantAsync(org,app,new("ide-test",".",["filesystem.delete"],15),"admin",default));
        Assert.Empty(await control.ProjectsAsync("other-org",app,default));Assert.Empty(await control.ProjectsAsync(org,"other-app",default));
        using var anon=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});Assert.NotEqual(HttpStatusCode.OK,(await anon.GetAsync("/v1/ide/config/ide-test")).StatusCode);
        var guide=await console.GetStringAsync("/ide");
        Assert.Contains("id=\"install\"",guide);
        Assert.Contains("id=\"portable-editor\"",guide);
        Assert.Contains("/ide/connection-profile",guide);
        Assert.Contains("Cobertura parcial",guide);
        Assert.DoesNotContain(DemoSeeder.DemoAgentKey,guide);
        var profile = await console.GetFromJsonAsync<JsonElement>("/ide/connection-profile?project=ide-test&adapter=cursor");
        Assert.Equal("ide-test", profile.GetProperty("project_id").GetString());
        Assert.Equal("invoice-agent", profile.GetProperty("agent_external_id").GetString());
        Assert.Equal("production", profile.GetProperty("environment").GetString());
        Assert.False(profile.TryGetProperty("api_key", out _));
        // Explicit YAML review cannot be replaced by a directory grant.
        await control.GrantAsync(org,app,new("ide-test","src",["filesystem.read"],15),"admin",default);
        var stored=await db.PolicyVersions.SingleAsync(v=>v.Id==version);
        stored= new PolicyVersion {Id=version+"2",OrganizationId=org,PolicyId=policy,Version=2,Published=true,Action="filesystem.read",ResourceType="filesystem",Yaml=stored.Yaml.Replace("version: 1","version: 2").Replace("effect: ALLOW","effect: REQUIRE_APPROVAL")};
        db.PolicyVersions.Add(stored);(await db.Policies.SingleAsync(p=>p.Id==policy)).PublishedVersionId=stored.Id;await db.SaveChangesAsync();
        var explicitReview=await Authorize("src/file",Guid.NewGuid().ToString("N"));Assert.Equal("REQUIRE_APPROVAL",explicitReview.GetProperty("decision").GetString());
    }
}
