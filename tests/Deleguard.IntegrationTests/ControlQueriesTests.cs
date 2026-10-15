using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Audit;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Deleguard.IntegrationTests;

public sealed class ControlQueriesTests(AdminAccessPostgresFixture postgres) : IClassFixture<AdminAccessPostgresFixture>
{
    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = postgres.ConnectionString,
            ["DemoAuth:ConsoleToken"] = DemoSeeder.DemoConsoleToken,
            ["DemoAuth:DemoOrganizationId"] = DemoSeeder.OrganizationId,
            ["DemoAuth:ApiKeyPepper"] = "deleguard-dev-pepper",
            ["Permits:SigningKey"] = "deleguard-dev-permit-hmac-key-change-in-prod-32"
        })));

    [Fact]
    public async Task Summary_counts_entire_day_and_separates_activation_from_execution()
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var q = scope.ServiceProvider.GetRequiredService<IControlQueries>();
        var app = "app_summary_" + Guid.NewGuid().ToString("N");
        var now = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        var start = now.Date;
        for (var i = 0; i < 150; i++) db.AuditEvents.Add(Event(app, now.AddMinutes(-i), i < 40 ? "DENY" : "ALLOW"));
        db.AuditEvents.Add(Event(app, new DateTimeOffset(start, TimeSpan.Zero).AddTicks(-1), "DENY"));
        db.AuditEvents.Add(Event(app, new DateTimeOffset(start, TimeSpan.Zero).AddDays(1), "DENY"));
        db.AuditEvents.Add(Event(app + "_other", now, "DENY"));
        db.AuditEvents.Add(Event(app, now, "DENY", "other_org"));
        var decision = Decision(app, now);
        db.AuthorizationDecisions.Add(decision);
        await db.SaveChangesAsync();
        var summary = await q.SummaryAsync(DemoSeeder.OrganizationId, app, now, default);
        Assert.Equal(150, summary.ChecksToday); Assert.Equal(40, summary.DenialsToday);
        Assert.True(summary.AuthorizationTested); Assert.False(summary.ExecutionReported); Assert.Equal(1, summary.MissingEvidence);
        db.ExecutionEvidence.Add(Evidence(decision)); await db.SaveChangesAsync();
        summary = await q.SummaryAsync(DemoSeeder.OrganizationId, app, now, default);
        Assert.True(summary.ExecutionReported); Assert.Equal(0, summary.MissingEvidence);
        Assert.True(await q.AgentHasEvidenceAsync(DemoSeeder.OrganizationId, app, decision.AgentId, default));
        Assert.False(await q.AgentHasEvidenceAsync(DemoSeeder.OrganizationId, "wrong_app", decision.AgentId, default));
    }

    [Fact]
    public async Task Record_and_activity_are_scoped_and_never_export_token_or_arbitrary_metadata()
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var q = scope.ServiceProvider.GetRequiredService<IControlQueries>();
        var decision = Decision(DemoSeeder.ApplicationId, DateTimeOffset.UtcNow);
        db.AuthorizationDecisions.Add(decision); db.ExecutionEvidence.Add(Evidence(decision));
        db.Permits.Add(new Permit { Id = "perm_" + Guid.NewGuid().ToString("N"), OrganizationId = decision.OrganizationId, DecisionId = decision.Id,
            Jti = Guid.NewGuid().ToString(), Action = decision.Action, ResourceType = decision.ResourceType, ResourceId = decision.ResourceId,
            Token = "DO_NOT_EXPORT_BEARER", IssuedAt = decision.CreatedAt, ExpiresAt = decision.CreatedAt.AddMinutes(10) });
        await db.SaveChangesAsync();
        Assert.Null(await q.CaseAsync("other_org", DemoSeeder.ApplicationId, decision.Id, default));
        Assert.Null(await q.CaseAsync(decision.OrganizationId, "other_app", decision.Id, default));
        var item = await q.CaseAsync(decision.OrganizationId, decision.ApplicationId, decision.Id, default);
        Assert.NotNull(item); Assert.Equal("actual-model", item.Decision.UsedModel); Assert.Equal(0.012m, item.Decision.CostUsd);
        Assert.Equal(DemoSeeder.PolicyId, item.PolicyDefinitionId);
        var json = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("DO_NOT_EXPORT", json); Assert.DoesNotContain("messages", json); Assert.DoesNotContain("Token", json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
        var export = await client.GetAsync($"/audit/decisions/{decision.Id}?handler=Export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("client_reported", await export.Content.ReadAsStringAsync());
        Assert.DoesNotContain("DO_NOT_EXPORT", await export.Content.ReadAsStringAsync());
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.NotEqual(HttpStatusCode.OK, (await anonymous.GetAsync($"/audit/decisions/{decision.Id}?handler=Export")).StatusCode);
    }

    [Fact]
    public async Task Coverage_uses_both_action_and_policy_grants_and_pages_render()
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        await using var scope = factory.Services.CreateAsyncScope();
        var q = scope.ServiceProvider.GetRequiredService<IControlQueries>();
        var rows = await q.CoverageAsync(DemoSeeder.OrganizationId, DemoSeeder.ApplicationId, DemoSeeder.AgentId, default);
        var invoice = Assert.Single(rows, r => r.Action == "invoice.cancel");
        Assert.True(invoice.ActionAssigned); Assert.NotEmpty(invoice.Policies); Assert.Equal("Ready to evaluate", invoice.Status);
        Assert.Empty(await q.CoverageAsync("other_org", DemoSeeder.ApplicationId, null, default));
        Assert.Empty(await q.CoverageAsync(DemoSeeder.OrganizationId, "other_app", null, default));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
        foreach (var url in new[] { "/", "/agents/connect", "/agents/" + DemoSeeder.AgentId, "/actions/coverage", "/audit/activity", "/access/ai-gateway" })
        {
            var response = await client.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode, $"{url}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }

    [Fact]
    public async Task Connection_is_additive_idempotent_and_rejects_wrong_application()
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IAgentConnectionStore>();
        var action = await db.ActionDefinitions.SingleAsync(a => a.OrganizationId == DemoSeeder.OrganizationId && a.ApplicationId == DemoSeeder.ApplicationId && a.Name == "invoice.cancel");
        var count = await db.AgentActionGrants.CountAsync(g => g.OrganizationId == DemoSeeder.OrganizationId && g.ActionDefinitionId == action.Id);
        var risk = action.Risk; var type = action.ResourceType;
        await store.ConnectAsync(DemoSeeder.OrganizationId, DemoSeeder.ApplicationId, DemoSeeder.AgentId, action.Id, DemoSeeder.PolicyId, default);
        await store.ConnectAsync(DemoSeeder.OrganizationId, DemoSeeder.ApplicationId, DemoSeeder.AgentId, action.Id, DemoSeeder.PolicyId, default);
        Assert.Equal(count, await db.AgentActionGrants.CountAsync(g => g.OrganizationId == DemoSeeder.OrganizationId && g.ActionDefinitionId == action.Id));
        await db.Entry(action).ReloadAsync(); Assert.Equal(risk, action.Risk); Assert.Equal(type, action.ResourceType);
        var newAgent = new Deleguard.Domain.Identity.Agent
        {
            Id = "agt_" + Guid.NewGuid().ToString("N"), OrganizationId = DemoSeeder.OrganizationId,
            ApplicationId = DemoSeeder.ApplicationId, EnvironmentId = DemoSeeder.EnvironmentId,
            ExternalId = Guid.NewGuid().ToString("N"), DisplayName = "Connection test", CreatedAt = DateTimeOffset.UtcNow
        };
        db.Agents.Add(newAgent); await db.SaveChangesAsync();
        await store.ConnectAsync(DemoSeeder.OrganizationId, DemoSeeder.ApplicationId, newAgent.Id, action.Id, DemoSeeder.PolicyId, default);
        await store.ConnectAsync(DemoSeeder.OrganizationId, DemoSeeder.ApplicationId, newAgent.Id, action.Id, DemoSeeder.PolicyId, default);
        Assert.Equal(1, await db.AgentActionGrants.CountAsync(g => g.AgentId == newAgent.Id && g.ActionDefinitionId == action.Id));
        Assert.Equal(1, await db.AgentPolicyGrants.CountAsync(g => g.AgentId == newAgent.Id && g.PolicyId == DemoSeeder.PolicyId));
        Assert.Equal(count + 1, await db.AgentActionGrants.CountAsync(g => g.OrganizationId == DemoSeeder.OrganizationId && g.ActionDefinitionId == action.Id));
        await Assert.ThrowsAsync<Deleguard.Application.ConflictException>(() => store.ConnectAsync(DemoSeeder.OrganizationId, "wrong_app", DemoSeeder.AgentId, action.Id, DemoSeeder.PolicyId, default));
        await Assert.ThrowsAsync<Deleguard.Application.ConflictException>(() => store.ConnectAsync("other_org", DemoSeeder.ApplicationId, DemoSeeder.AgentId, action.Id, DemoSeeder.PolicyId, default));
    }

    [Fact]
    public async Task Console_simulations_do_not_complete_real_integration_progress()
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var queries = scope.ServiceProvider.GetRequiredService<IControlQueries>();
        var app = "app_simulation_" + Guid.NewGuid().ToString("N");
        var decision = Decision(app, DateTimeOffset.UtcNow);
        db.AuthorizationDecisions.Add(decision); db.ExecutionEvidence.Add(Evidence(decision));
        db.AuditEvents.Add(new() { Id = "aud_" + Guid.NewGuid().ToString("N"), OrganizationId = decision.OrganizationId,
            ApplicationId = app, DecisionId = decision.Id, EventType = "authorization.decided",
            PayloadJson = "{\"decision\":\"ALLOW\",\"source\":\"playground\"}", PreviousEventHash = "previous",
            EventHash = Guid.NewGuid().ToString("N"), CreatedAt = decision.CreatedAt });
        await db.SaveChangesAsync();
        var summary = await queries.SummaryAsync(decision.OrganizationId, app, decision.CreatedAt, default);
        Assert.False(summary.AuthorizationTested); Assert.False(summary.ExecutionReported);
        Assert.False(await queries.AgentHasAuthorizationAsync(decision.OrganizationId, app, decision.AgentId, default));
        Assert.False(await queries.AgentHasEvidenceAsync(decision.OrganizationId, app, decision.AgentId, default));
        Assert.Equal("playground", (await queries.CaseAsync(decision.OrganizationId, app, decision.Id, default))!.Decision.Source);
        var real = Decision(app, decision.CreatedAt.AddSeconds(1));
        db.AuthorizationDecisions.Add(real); db.ExecutionEvidence.Add(Evidence(real)); await db.SaveChangesAsync();
        Assert.True(await queries.AgentHasAuthorizationAsync(real.OrganizationId, app, real.AgentId, default));
        Assert.True(await queries.AgentHasEvidenceAsync(real.OrganizationId, app, real.AgentId, default));
        Assert.False(await queries.AgentHasAuthorizationAsync("other_org", app, real.AgentId, default));
    }

    private static AuditEvent Event(string app, DateTimeOffset time, string decision, string org = DemoSeeder.OrganizationId) => new()
    {
        Id = "aud_" + Guid.NewGuid().ToString("N"), OrganizationId = org, ApplicationId = app, EventType = "authorization.decided",
        PayloadJson = JsonSerializer.Serialize(new { decision }), PreviousEventHash = "previous", EventHash = Guid.NewGuid().ToString("N"), CreatedAt = time
    };
    private static AuthorizationDecision Decision(string app, DateTimeOffset time) => new()
    {
        Id = "dec_" + Guid.NewGuid().ToString("N"), OrganizationId = DemoSeeder.OrganizationId, ApplicationId = app,
        EnvironmentId = DemoSeeder.EnvironmentId, AgentId = DemoSeeder.AgentId, AgentExternalId = "invoice-agent", Action = "llm.chat",
        ResourceType = "model", ResourceId = "chat.default", ResourceAttributesJson = "{}", ContextJson = "{}", Decision = DecisionEffect.Allow,
        RequestDigest = Guid.NewGuid().ToString("N"), IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAt = time,
        PolicyId = "invoice-cancel-production", PolicyVersion = 4
    };
    private static ExecutionEvidence Evidence(AuthorizationDecision d) => new()
    {
        Id = "evd_" + Guid.NewGuid().ToString("N"), OrganizationId = d.OrganizationId, DecisionId = d.Id, Status = ExecutionStatus.Success,
        MetadataJson = "{\"model_requested\":\"chat.default\",\"model_used\":\"actual-model\",\"cost_usd\":\"0.012\",\"route_reason\":\"fallback\",\"messages\":\"DO_NOT_EXPORT_PROMPT\",\"api_key\":\"DO_NOT_EXPORT_SECRET\"}", CreatedAt = d.CreatedAt
    };
}
