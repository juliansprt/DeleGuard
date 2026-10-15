using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Deleguard.SecurityTests;

public sealed class UnifiedExecutionSecurityTests(AuthorizationPostgresFixture postgres) : IClassFixture<AuthorizationPostgresFixture>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _agent = null!;
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = postgres.ConnectionString })));
        _agent = _factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        _agent.DefaultRequestHeaders.Authorization = new("Bearer", DemoSeeder.DemoAgentKey);
        return Task.CompletedTask;
    }
    public async Task DisposeAsync() { _agent.Dispose(); await _factory.DisposeAsync(); }

    [Theory]
    [InlineData("invoice.cancel")]
    [InlineData("filesystem.archive")]
    public async Task Public_authorization_rejects_missing_operation_identity(string action)
    {
        using var raw = _factory.CreateClient(); // Deliberately bypass the integration fixture's identity handler.
        raw.DefaultRequestHeaders.Authorization = new("Bearer", DemoSeeder.DemoAgentKey);
        raw.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await raw.PostAsJsonAsync("/v1/authorize", new {
            action, resource = new { type = "invoice", id = "missing-operation" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("operation.id", await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> Authorize(string? operation = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new { agent = new { id = "invoice-agent", environment = "production" },
                operation = new { id = operation ?? Guid.NewGuid().ToString("N"), hash = Hash }, action = "invoice.cancel",
                resource = new { type = "invoice", id = "v1-test", snapshot_hash = Hash,
                    attributes = new { status = "draft", amount = 100, currency = "COP", reported_to_tax_authority = false } } })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _agent.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.StartsWith("authorization;dur=", response.Headers.GetValues("Server-Timing").Single());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ALLOW", result.GetProperty("decision").GetString());
        return result;
    }
    private Task<HttpResponseMessage> Consume(JsonElement d, string? hash = null) => _agent.PostAsJsonAsync(
        "/v1/permits/" + d.GetProperty("permit_id").GetString() + "/consume", new {
            operation_id = d.GetProperty("operation").GetProperty("id").GetString(), operation_hash = hash ?? Hash, resource_snapshot_hash = Hash });

    [Fact]
    public async Task Concurrent_consumers_acquire_exactly_once_and_late_evidence_is_idempotent()
    {
        var d = await Authorize();
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Consume(d)));
        var results = new List<JsonElement>();
        foreach (var response in responses) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); results.Add(await response.Content.ReadFromJsonAsync<JsonElement>()); }
        Assert.Single(results, x => x.GetProperty("acquired").GetBoolean());
        var execution = results[0].GetProperty("execution_id").GetString();
        Assert.Single(results.Select(x => x.GetProperty("execution_id").GetString()).Distinct());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var decisionId = d.GetProperty("decision_id").GetString()!;
        await db.Permits.Where(p => p.DecisionId == decisionId).ExecuteUpdateAsync(s => s.SetProperty(p => p.RevokedAt, DateTimeOffset.UtcNow));
        object Report(string? permit = null) => new { decision_id = decisionId, execution_id = execution,
            permit_id = permit ?? d.GetProperty("permit_id").GetString(), status = "SUCCESS", provider_reference = "receipt-1", resource_snapshot_hash = Hash };
        Assert.Equal(HttpStatusCode.Accepted, (await _agent.PostAsJsonAsync("/v1/executions", Report())).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await _agent.PostAsJsonAsync("/v1/executions", Report())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _agent.PostAsJsonAsync("/v1/executions", Report("wrong"))).StatusCode);
        Assert.Equal(1, await db.ExecutionEvidence.CountAsync(e => e.DecisionId == decisionId));
        Assert.Equal("REPORTED", (await db.OperationExecutions.SingleAsync(e => e.DecisionId == decisionId)).Verification);
        Assert.False((await (await Consume(d)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("acquired").GetBoolean());
    }

    [Fact]
    public async Task New_operation_can_repeat_content_but_consumed_identity_cannot_be_reused()
    {
        var op = Guid.NewGuid().ToString("N"); var first = await Authorize(op);
        Assert.Equal(HttpStatusCode.Conflict, (await Consume(first, new string('b', 64))).StatusCode);
        (await Consume(first)).EnsureSuccessStatusCode();
        var reused = await Authorize(op);
        Assert.Equal(HttpStatusCode.Conflict, (await Consume(reused)).StatusCode);
        var legitimate = await Authorize();
        Assert.True((await (await Consume(legitimate)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("acquired").GetBoolean());
    }

    [Fact]
    public async Task Suspension_blocks_new_consumption()
    {
        var d = await Authorize();
        await using var scope = _factory.Services.CreateAsyncScope();
        var control = scope.ServiceProvider.GetRequiredService<IExecutionControl>();
        await control.SuspendApplicationAsync(DemoSeeder.OrganizationId, DemoSeeder.ApplicationId, true, "test", default);
        try { Assert.Equal(HttpStatusCode.Forbidden, (await Consume(d)).StatusCode); }
        finally { await control.SuspendApplicationAsync(DemoSeeder.OrganizationId, DemoSeeder.ApplicationId, false, "test", default); }
    }

    [Fact]
    public async Task Evidence_requires_a_consumed_execution()
    {
        var d = await Authorize();
        Assert.Equal(HttpStatusCode.Conflict, (await _agent.PostAsJsonAsync("/v1/executions", new {
            decision_id = d.GetProperty("decision_id").GetString(), execution_id = "invented", status = "SUCCESS", resource_snapshot_hash = Hash })).StatusCode);
    }

    [Fact]
    public async Task Simulation_issues_no_decision_or_permit()
    {
        await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var count = await db.AuthorizationDecisions.CountAsync(); var permits = await db.Permits.CountAsync();
        var response = await _agent.PostAsJsonAsync("/v1/evaluations", new { action = "invoice.cancel", resource = new { type = "invoice", id = "sim", attributes = new { status = "draft" } } });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(count, await db.AuthorizationDecisions.CountAsync()); Assert.Equal(permits, await db.Permits.CountAsync());
    }

    [Fact]
    public async Task Parallel_audit_writers_leave_one_verifiable_chain()
    {
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Authorize()));
        await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var events = await db.AuditEvents.AsNoTracking().Where(e => e.OrganizationId == DemoSeeder.OrganizationId).ToListAsync();
        Assert.Equal(events.Count, events.Select(x => x.PreviousEventHash).Distinct().Count());
        var previous = AuditHasher.Genesis; var seen = 0;
        while (events.SingleOrDefault(x => x.PreviousEventHash == previous) is { } e)
        {
            var canonical = CanonicalJson.Serialize(JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<JsonElement>(e.PayloadJson)));
            Assert.Equal(e.EventHash, AuditHasher.Compute(previous, $"{e.EventType}|{canonical}")); previous = e.EventHash; seen++;
        }
        Assert.Equal(events.Count, seen);
        Assert.Equal(previous, (await db.TenantAuditHeads.SingleAsync(x => x.OrganizationId == DemoSeeder.OrganizationId)).LastEventHash);
    }

    [Fact]
    public async Task Deadline_is_enforced_after_waiting_for_audit_lock()
    {
        var d = await Authorize(); var id = d.GetProperty("decision_id").GetString();
        await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        await db.Permits.Where(p => p.DecisionId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(2)));
        await using var tx = await db.Database.BeginTransactionAsync();
        var key = "deleguard-audit:" + DemoSeeder.OrganizationId;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
        var pending = Consume(d);
        await Task.Delay(TimeSpan.FromSeconds(3));
        await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        Assert.False(await db.OperationExecutions.AnyAsync(e => e.DecisionId == id));
    }

    [Fact]
    public async Task Ide_common_consume_keeps_project_grant_and_legacy_replay_defenses()
    {
        await using var scope = _factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var ide = scope.ServiceProvider.GetRequiredService<IIdeControl>();
        var key = Guid.NewGuid().ToString("N"); var org = DemoSeeder.OrganizationId; var app = DemoSeeder.ApplicationId; var agent = DemoSeeder.AgentId;
        db.ActionDefinitions.Add(new Deleguard.Domain.Authorization.ActionDefinition { Id = key, OrganizationId = org, ApplicationId = app, Name = "filesystem.read", ResourceType = "filesystem", Risk = RiskLevel.Medium });
        db.AgentActionGrants.Add(new Deleguard.Domain.Authorization.AgentActionGrant { OrganizationId = org, ApplicationId = app, AgentId = agent, ActionDefinitionId = key });
        db.Policies.Add(new Deleguard.Domain.Policies.PolicyDefinition { Id = key, OrganizationId = org, ApplicationId = app, Slug = key, PublishedVersionId = key });
        db.PolicyVersions.Add(new Deleguard.Domain.Policies.PolicyVersion { Id = key, OrganizationId = org, PolicyId = key, Version = 1, Published = true, Action = "filesystem.read", ResourceType = "filesystem",
            Yaml = $"id: {key}\nversion: 1\npriority: 1000\nmatch:\n  action: filesystem.read\n  resource_type: filesystem\nrules:\n  - name: allow\n    when: true\n    effect: ALLOW\nfallback: DENY\n" });
        db.AgentPolicyGrants.Add(new Deleguard.Domain.Policies.AgentPolicyGrant { OrganizationId = org, ApplicationId = app, AgentId = agent, PolicyId = key });
        await db.SaveChangesAsync();
        await ide.SaveProjectAsync(org, app, new(key, "Security", agent, true, false, ["protected"], ["secret"]), "test", default);
        async Task<HttpResponseMessage> IdeAuthorize(string project, string path, string op, string? overrideId = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize") { Content = JsonContent.Create(new {
                action = "filesystem.read", operation = new { id = overrideId ?? op, hash = Hash },
                resource = new { type = "filesystem", id = project + ":" + path, snapshot_hash = Hash,
                    attributes = new { project_id = project, path, operation_id = op, operation_hash = Hash } } }) };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N")); return await _agent.SendAsync(request);
        }
        async Task<JsonElement> Decision(string project, string path) => await (await IdeAuthorize(project, path, Guid.NewGuid().ToString("N"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DENY", (await Decision("other-project", "src/file")).GetProperty("decision").GetString());
        Assert.Equal("DENY", (await Decision(key, "secret/file")).GetProperty("decision").GetString());
        Assert.Equal("REQUIRE_APPROVAL", (await Decision(key, "protected/file")).GetProperty("decision").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await IdeAuthorize(key, "src/file", Guid.NewGuid().ToString("N"), "contradictory")).StatusCode);
        var grant = await ide.GrantAsync(org, app, new(key, "protected", ["filesystem.read"], 1), "test", default);
        var allowed = await Decision(key, "protected/file"); Assert.Equal("ALLOW", allowed.GetProperty("decision").GetString());
        await ide.RevokeAsync(org, app, grant.Id, "test", default);
        Assert.Equal(HttpStatusCode.Conflict, (await Consume(allowed)).StatusCode);
        var ordinary = await Decision(key, "src/file"); (await Consume(ordinary)).EnsureSuccessStatusCode();
        var legacy = await _agent.PostAsJsonAsync("/v1/ide/permits/consume", new { decision_id = ordinary.GetProperty("decision_id").GetString(),
            operation_id = ordinary.GetProperty("operation").GetProperty("id").GetString(), snapshot_hash = Hash });
        Assert.True(legacy.IsSuccessStatusCode, await legacy.Content.ReadAsStringAsync());
        Assert.False((await legacy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("acquired").GetBoolean());
    }

    [Fact]
    public async Task Concurrent_budget_reservations_cannot_overspend_and_old_pending_cost_is_carried()
    {
        var org = "org_budget_" + Guid.NewGuid().ToString("N");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
            db.Organizations.Add(new() { Id = org, Name = "Budget race" });
            await db.SaveChangesAsync();
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            try { await scope.ServiceProvider.GetRequiredService<ISpendLedger>().ReserveAsync(org, "agent", "decision_" + i, 8m, default); return true; }
            catch (Deleguard.Application.ConflictException) { return false; }
        }));
        Assert.Equal(6, results.Count(x => x)); // Default budget is USD 50: six times USD 8, never seven.
        await using var verify = _factory.Services.CreateAsyncScope(); var verifyDb = verify.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        await verifyDb.GatewaySpendReservations.Where(r => r.OrganizationId == org).ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, DateTimeOffset.UtcNow.AddMonths(-1)));
        Assert.Equal(2m, await verify.ServiceProvider.GetRequiredService<ISpendLedger>().RemainingBudgetUsdAsync(org, default));
    }
}
