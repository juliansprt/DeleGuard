using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.IntegrationTests;

public sealed class IdeKeyPermissionTests
{
    [Theory]
    [InlineData("agents.read")]
    [InlineData("agents.write")]
    public async Task Both_read_and_write_permissions_are_required_before_any_work(string permission)
    {
        var context = new DefaultHttpContext();
        context.Items[AuthExtensions.ConsoleItem] = true;
        context.Items[AuthExtensions.ConsoleUserItem] = new ConsoleUserPrincipal("org", "user", "user@example.test", "Reader", [], [permission], false);
        var page = new Deleguard.Api.Pages.Ide.IndexModel(null!, null!, null!, null!, null!, null!)
        { PageContext = new PageContext { HttpContext = context } };
        Assert.False(page.CanGenerateKey);
        await Assert.ThrowsAsync<ForbiddenException>(() => page.OnPostGenerateKeyAsync(null, default));
    }
}

public sealed class IdeKeyGenerationTests(AdminAccessPostgresFixture postgres) : IClassFixture<AdminAccessPostgresFixture>
{
    private WebApplicationFactory<Program> Factory(GateTrace trace) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("ConnectionStrings:Postgres", postgres.ConnectionString);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = postgres.ConnectionString,
            ["Oidc:Enabled"] = "false", ["DemoAuth:AllowEmergencyConsoleToken"] = "true",
            ["DemoAuth:ConsoleToken"] = DemoSeeder.DemoConsoleToken,
            ["DemoAuth:DemoOrganizationId"] = DemoSeeder.OrganizationId,
            ["DemoAuth:ApiKeyPepper"] = "deleguard-dev-pepper",
            ["Permits:SigningKey"] = "deleguard-dev-permit-hmac-key-change-in-prod-32"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAdminActionGateway>();
            services.AddScoped<IAdminActionGateway>(sp => new TracedGateway(ActivatorUtilities.CreateInstance<AdminActionGateway>(sp), trace));
        });
    });

    [Fact]
    public async Task Key_is_for_the_project_agent_and_authenticates_the_downloaded_installer()
    {
        var trace = new GateTrace(); await using var factory = Factory(trace);
        var project = await CreateProject(factory); var other = await CreateProject(factory);
        using var client = Client(factory, project.ApplicationId);
        var html = await client.GetStringAsync("/ide?project=" + project.Id);
        Assert.Contains("Generate new API key", html);
        Assert.Contains("id=\"confirm-dialog\"", html);
        Assert.Contains("value=\"cancel\"", html);
        var form = Form(html, project); form["AgentId"] = other.Agent.AgentId;
        using var response = await client.PostAsync("/ide?handler=GenerateKey", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var result = await response.Content.ReadAsStringAsync();
        var key = Regex.Match(result, "id=\"ide-agent-key\"[^>]*>([^<]+)<").Groups[1].Value;
        Assert.StartsWith("dgk_", key); Assert.NotEqual(project.Agent.ApiKey, key);
        Assert.Single(Regex.Matches(result, Regex.Escape(key)).Cast<Match>());
        Assert.DoesNotContain(key, string.Join("\n", response.Headers));
        var call = Assert.Single(trace.Calls);
        Assert.Equal("admin.agent.rotate_key", call.Action); Assert.Equal(project.Agent.AgentId, call.Resource);
        Assert.Equal(project.ApplicationId, call.Attributes!["application_id"].GetString());
        Assert.Equal(project.Id, call.Attributes["project_id"].GetString());
        Assert.DoesNotContain(key, JsonSerializer.Serialize(call.Attributes));
        using var installer = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        installer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await installer.GetAsync("/v1/ide/config/" + project.Id)).StatusCode);
        installer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", project.Agent.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await installer.GetAsync("/v1/ide/config/" + project.Id)).StatusCode);
        Assert.DoesNotContain(key, await client.GetStringAsync("/ide?project=" + project.Id));
        // An old form cannot replace the key again after a completed request.
        await client.PostAsync("/ide?handler=GenerateKey", new FormUrlEncodedContent(form));
        Assert.Single(trace.Calls);
        await using var scope = factory.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IAgentDirectory>();
        Assert.NotNull(await directory.AuthenticateAsync(key, default));
        Assert.NotNull(await directory.AuthenticateAsync(other.Agent.ApiKey, default));
    }

    [Fact]
    public async Task Concurrent_requests_from_the_same_form_leave_one_usable_key()
    {
        var trace = new GateTrace { Synchronize = true }; await using var factory = Factory(trace);
        var project = await CreateProject(factory); using var client = Client(factory, project.ApplicationId);
        var form = Form(await client.GetStringAsync("/ide?project=" + project.Id), project);
        async Task<string> Generate()
        {
            using var response = await client.PostAsync("/ide?handler=GenerateKey", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }
        var results = await Task.WhenAll(Generate(), Generate());
        var keys = results.Select(r => Regex.Match(r, "id=\"ide-agent-key\"[^>]*>([^<]+)<").Groups[1].Value).Where(k => k.Length > 0).ToArray();
        var key = Assert.Single(keys);
        Assert.Contains(results, html => html.Contains("already consumed") || html.Contains("The API key has changed since this page was opened."));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        Assert.Single(await db.AgentCredentials.Where(c => c.AgentId == project.Agent.AgentId && c.RevokedAt == null).ToListAsync());
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAgentDirectory>().AuthenticateAsync(key, default));
    }

    [Fact]
    public async Task Rolled_back_rotation_cannot_be_saved_by_later_evidence_work()
    {
        await using var factory = Factory(new GateTrace()); var project = await CreateProject(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IAgentDirectory>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        string? rejectedKey = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.ExecuteLockedAsync<bool>(DemoSeeder.OrganizationId, project.Agent.AgentId, async () =>
        {
            rejectedKey = (await directory.RotateKeyAsync(DemoSeeder.OrganizationId, project.Agent.AgentId, default)).ApiKey;
            await uow.SaveChangesAsync(default);
            throw new InvalidOperationException("Simulated failure after saving");
        }, default));
        await uow.SaveChangesAsync(default);
        Assert.NotNull(await directory.AuthenticateAsync(project.Agent.ApiKey, default));
        Assert.Null(await directory.AuthenticateAsync(rejectedKey!, default));
    }

    [Theory]
    [InlineData("DENY")]
    [InlineData("REQUIRE_APPROVAL")]
    public async Task Administrative_gate_never_reveals_or_changes_a_key_until_allowed(string decision)
    {
        var trace = new GateTrace { Block = decision }; await using var factory = Factory(trace);
        var project = await CreateProject(factory); using var client = Client(factory, project.ApplicationId);
        var html = await client.GetStringAsync("/ide?project=" + project.Id);
        var response = await client.PostAsync("/ide?handler=GenerateKey", new FormUrlEncodedContent(Form(html, project)));
        var result = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("id=\"ide-agent-key\"", result);
        if (decision == "REQUIRE_APPROVAL") Assert.Contains("/approvals/key-review", result);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAgentDirectory>().AuthenticateAsync(project.Agent.ApiKey, default));
    }

    [Fact]
    public async Task Foreign_project_missing_csrf_and_disabled_agent_do_not_rotate()
    {
        var trace = new GateTrace(); await using var factory = Factory(trace);
        var project = await CreateProject(factory); var foreign = await CreateProject(factory);
        using var client = Client(factory, project.ApplicationId);
        var html = await client.GetStringAsync("/ide?project=" + project.Id);
        var foreignForm = Form(html, foreign);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/ide?handler=GenerateKey", new FormUrlEncodedContent(foreignForm))).StatusCode);
        var missingToken = Form(html, project); missingToken.Remove("__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/ide?handler=GenerateKey", new FormUrlEncodedContent(missingToken))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentAdminHandler>().SetEnabledAsync(DemoSeeder.OrganizationId, project.Agent.AgentId, false, default);
        var disabled = await client.PostAsync("/ide?handler=GenerateKey", new FormUrlEncodedContent(Form(html, project)));
        Assert.DoesNotContain("id=\"ide-agent-key\"", await disabled.Content.ReadAsStringAsync());
        Assert.Empty(trace.Calls);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAgentDirectory>().AuthenticateAsync(foreign.Agent.ApiKey, default));
    }

    [Fact]
    public async Task Portable_profile_contains_scope_but_no_secret_and_does_not_cross_applications()
    {
        await using var factory=Factory(new GateTrace());var project=await CreateProject(factory);var other=await CreateProject(factory);
        using var client=Client(factory,project.ApplicationId);
        using var response=await client.GetAsync("/ide/connection-profile?project="+project.Id+"&adapter=claude");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);
        var raw=await response.Content.ReadAsStringAsync();using var json=JsonDocument.Parse(raw);var profile=json.RootElement;
        Assert.Equal(project.ApplicationId,profile.GetProperty("application_id").GetString());
        Assert.Equal(project.Agent.AgentId,profile.GetProperty("agent_id").GetString());
        Assert.Equal("claude",profile.GetProperty("adapter").GetString());Assert.Equal("en",profile.GetProperty("language").GetString());
        Assert.DoesNotContain("dgk_",raw);Assert.DoesNotContain("api_key",raw);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/ide/connection-profile?project="+other.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/ide/connection-profile?project="+project.Id+"&adapter=unknown")).StatusCode);
        using var progress=await client.GetAsync("/v1/ide/projects/"+project.Id+"/progress");Assert.Equal(HttpStatusCode.OK,progress.StatusCode);
        using var status=JsonDocument.Parse(await progress.Content.ReadAsStringAsync());Assert.Equal(JsonValueKind.Null,status.RootElement.GetProperty("last_connection").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/v1/ide/projects/"+other.Id+"/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/ide/installer?platform=win-x64")).StatusCode);
    }

    [Fact]
    public async Task Shared_agent_decisions_and_progress_stay_bound_to_the_selected_project()
    {
        await using var factory = Factory(new GateTrace());
        var project = await CreateProject(factory); var foreign = await CreateProject(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var ide = scope.ServiceProvider.GetRequiredService<IIdeControl>();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var sibling = project.Id + "-other";
        await ide.SaveProjectAsync(DemoSeeder.OrganizationId, project.ApplicationId,
            new(sibling, "Same account, other folder", project.Agent.AgentId, false, false, ["infra/production"], []), "test", default);
        var decision = new AuthorizationDecision
        {
            Id = "dec_" + Guid.NewGuid().ToString("N"), OrganizationId = DemoSeeder.OrganizationId,
            ApplicationId = project.ApplicationId, EnvironmentId = await db.Agents.Where(a => a.Id == project.Agent.AgentId).Select(a => a.EnvironmentId).SingleAsync(),
            AgentId = project.Agent.AgentId, AgentExternalId = project.Id, Action = "filesystem.read",
            ResourceType = "file", ResourceId = "src/test.txt",
            ResourceAttributesJson = JsonSerializer.Serialize(new { project_id = project.Id }), ContextJson = "{\"source\":\"ide-hook\"}",
            Decision = DecisionEffect.Allow, RequestDigest = Guid.NewGuid().ToString("N"),
            IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow
        };
        db.AuthorizationDecisions.Add(decision);
        db.ExecutionEvidence.Add(new() { Id = "evd_" + Guid.NewGuid().ToString("N"), OrganizationId = decision.OrganizationId,
            DecisionId = decision.Id, Status = ExecutionStatus.Success, MetadataJson = "{}", CreatedAt = decision.CreatedAt });
        await db.SaveChangesAsync();
        using var installer = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        installer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", project.Agent.ApiKey);
        var path = $"/v1/ide/projects/{project.Id}/decisions/{decision.Id}";
        Assert.Equal(HttpStatusCode.OK, (await installer.GetAsync(path)).StatusCode);
        using var remote=JsonDocument.Parse(await installer.GetStringAsync("/v1/ide/config/"+project.Id));
        Assert.Equal(project.Id,remote.RootElement.GetProperty("id").GetString());
        Assert.Equal(project.Id,remote.RootElement.GetProperty("agent_external_id").GetString());
        Assert.Equal("production",remote.RootElement.GetProperty("environment").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await installer.GetAsync($"/v1/ide/projects/{sibling}/decisions/{decision.Id}")).StatusCode);
        installer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreign.Agent.ApiKey);
        Assert.Equal(HttpStatusCode.NotFound, (await installer.GetAsync(path)).StatusCode);
        installer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "dgk_revoked_test");
        Assert.Equal(HttpStatusCode.Unauthorized, (await installer.GetAsync(path)).StatusCode);
        var ownProgress = await ide.ProgressAsync(DemoSeeder.OrganizationId, project.ApplicationId, project.Id, default);
        Assert.Equal(decision.Id, ownProgress.DecisionId); Assert.NotNull(ownProgress.LastReportedResult); Assert.Null(ownProgress.LastConnection);
        var otherProgress = await ide.ProgressAsync(DemoSeeder.OrganizationId, project.ApplicationId, sibling, default);
        Assert.Null(otherProgress.LastDecision); Assert.Null(otherProgress.LastReportedResult);
        db.AuditEvents.Add(new() { Id = "aud_" + Guid.NewGuid().ToString("N"), OrganizationId = decision.OrganizationId,
            ApplicationId = decision.ApplicationId, DecisionId = decision.Id, EventType = "authorization.decided",
            PayloadJson = "{\"source\":\"playground\"}", PreviousEventHash = "previous", EventHash = Guid.NewGuid().ToString("N"), CreatedAt = decision.CreatedAt });
        await db.SaveChangesAsync();
        var simulated = await ide.ProgressAsync(DemoSeeder.OrganizationId, project.ApplicationId, project.Id, default);
        Assert.Null(simulated.LastDecision); Assert.Null(simulated.LastReportedResult);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string app)
    {
        var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
        client.DefaultRequestHeaders.Add("Cookie", ApplicationContextExtensions.CookieName + "=" + app);
        return client;
    }
    private static async Task<ProjectFixture> CreateProject(WebApplicationFactory<Program> factory)
    {
        using var bootstrap = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var id = "ide-" + Guid.NewGuid().ToString("N");
        var app = await services.GetRequiredService<ApplicationAdminHandler>().CreateAsync(DemoSeeder.OrganizationId, "Key test", id, default);
        var agent = await services.GetRequiredService<AgentAdminHandler>().CreateAsync(new(DemoSeeder.OrganizationId, app.Id, id, "Test IDE", "production"), default);
        await services.GetRequiredService<IIdeControl>().SaveProjectAsync(DemoSeeder.OrganizationId, app.Id, new(id, "Key project", agent.AgentId, false, false, ["infra/production"], [".deleguard"]), "test@example.test", default);
        return new(id, app.Id, agent);
    }
    private static Dictionary<string, string> Form(string html, ProjectFixture project) => new()
    {
        ["ProjectId"] = project.Id, ["Adapter"] = "cursor", ["currentKeyPrefix"] = project.Agent.KeyPrefix,
        ["dg_operation_id"] = Guid.NewGuid().ToString("N"),
        ["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value)
    };
    private sealed record ProjectFixture(string Id, string ApplicationId, CreatedAgentSecret Agent);
    private sealed record GateCall(string Action, string Resource, Dictionary<string, JsonElement>? Attributes);
    private sealed class GateTrace
    {
        public string? Block;
        public bool Synchronize;
        public int Arrived;
        public TaskCompletionSource BothArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<GateCall> Calls { get; } = new();
    }
    private sealed class TracedGateway(IAdminActionGateway inner, GateTrace trace) : IAdminActionGateway
    {
        public async Task<AdminActionOutcome<T>> RunAsync<T>(ConsoleUserPrincipal caller, string action, string resourceType, string resourceId, Dictionary<string, JsonElement>? attributes, string permission, Func<CancellationToken, Task<T>> execute, CancellationToken ct)
        {
            trace.Calls.Enqueue(new(action, resourceId, attributes));
            if (trace.Block is { } decision) return new AdminActionOutcome<T>(decision, "key-decision", "Blocked in test", decision == "REQUIRE_APPROVAL" ? "key-review" : null, null, default);
            if (trace.Synchronize)
            {
                if (Interlocked.Increment(ref trace.Arrived) == 2) trace.BothArrived.TrySetResult();
                await trace.BothArrived.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
            return await inner.RunAsync(caller, action, resourceType, resourceId, attributes, permission, execute, ct);
        }
    }
}
