using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Policies;
using Deleguard.Domain.Policies;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Deleguard.IntegrationTests;

public sealed class PolicyPublicationConcurrencyTests(AdminAccessPostgresFixture postgres) : IClassFixture<AdminAccessPostgresFixture>
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Waiting_profile_publication_does_not_overwrite_concurrent_v2(bool publishV2)
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        await using var originalScope = factory.Services.CreateAsyncScope();
        await using var competingScope = factory.Services.CreateAsyncScope();
        var original = originalScope.ServiceProvider.GetRequiredService<PolicyAdminHandler>();
        var competing = competingScope.ServiceProvider.GetRequiredService<PolicyAdminHandler>();
        var competingRepository = competingScope.ServiceProvider.GetRequiredService<IPolicyRepository>();
        var (draft, expected) = await DraftAsync(original);
        var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishChange = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? v2Id = null;
        var concurrentWrite = competingRepository.ExecuteLockedAsync(DemoSeeder.OrganizationId, draft.Policy.Id, async () =>
        {
            lockAcquired.SetResult();
            await finishChange.Task;
            var v2 = await competing.AddVersionAsync(new(DemoSeeder.OrganizationId, draft.Policy.Id,
                Yaml(draft.Policy.Slug, 2, "DENY")), default);
            v2Id = v2.Id;
            if (publishV2) await competing.PublishAsync(new(DemoSeeder.OrganizationId, draft.Policy.Id, v2.Id), default);
            return true;
        }, default);
        await lockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var stalePublication = original.PublishAsync(new(DemoSeeder.OrganizationId, draft.Policy.Id, draft.Version.Id, expected), default);
        try
        {
            var winner = await Task.WhenAny(stalePublication, Task.Delay(150));
            Assert.NotSame(stalePublication, winner); // Actual PostgreSQL lock, not only mocked ordering.
        }
        finally { finishChange.TrySetResult(); }
        await concurrentWrite.WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<ConflictException>(() => stalePublication);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var policy = await db.Policies.AsNoTracking().SingleAsync(p => p.Id == draft.Policy.Id);
        var versions = await db.PolicyVersions.AsNoTracking().Where(v => v.PolicyId == policy.Id).ToListAsync();
        Assert.Equal(publishV2 ? v2Id : null, policy.PublishedVersionId);
        Assert.False(versions.Single(v => v.Id == draft.Version.Id).Published);
        Assert.Equal(publishV2, versions.Single(v => v.Id == v2Id).Published);
        Assert.Equal(publishV2 ? 1 : 0, versions.Count(v => v.Published));
    }

    [Fact]
    public async Task Unchanged_verified_snapshot_can_publish_inside_the_shared_lock()
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<PolicyAdminHandler>();
        var (draft, expected) = await DraftAsync(handler);
        var result = await handler.PublishAsync(new(DemoSeeder.OrganizationId, draft.Policy.Id, draft.Version.Id, expected), default);
        Assert.True(result.Published);
        var (policy, versions) = await handler.GetAsync(DemoSeeder.OrganizationId, draft.Policy.Id, default);
        Assert.Equal(result.Id, policy.PublishedVersionId);
        Assert.True(Assert.Single(versions).Published);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_publication_cannot_leak_into_a_later_unrelated_save(bool saveBeforeFailure)
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<PolicyAdminHandler>();
        var repository = scope.ServiceProvider.GetRequiredService<IPolicyRepository>();
        var db = scope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        var (draft, _) = await DraftAsync(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ExecuteLockedAsync<bool>(DemoSeeder.OrganizationId, draft.Policy.Id, async () =>
        {
            var policy = await db.Policies.SingleAsync(p => p.Id == draft.Policy.Id);
            var version = await db.PolicyVersions.SingleAsync(v => v.Id == draft.Version.Id);
            policy.PublishedVersionId = version.Id;
            version.Published = true;
            db.PolicyVersions.Add(new PolicyVersion { Id = "failed-version-" + Guid.NewGuid().ToString("N"),
                OrganizationId = DemoSeeder.OrganizationId, PolicyId = policy.Id, Version = 2,
                Action = "invoice.cancel", ResourceType = "invoice", Yaml = Yaml(policy.Slug, 2, "ALLOW") });
            db.Set<PolicySimulationCase>().Remove(await db.Set<PolicySimulationCase>().SingleAsync(c => c.PolicyId == policy.Id));
            db.AgentPolicyGrants.Remove(await db.AgentPolicyGrants.SingleAsync(g => g.PolicyId == policy.Id));
            if (saveBeforeFailure) await db.SaveChangesAsync();
            throw new InvalidOperationException("Simulated failure after mutation but before commit");
        }, default));
        // The real admin gateway reports failure evidence using this same request DbContext.
        // A harmless unrelated write exercises that SaveChanges path after catching the error.
        var sentinel = new PolicyDefinition { Id = "sentinel-" + Guid.NewGuid().ToString("N"),
            OrganizationId = DemoSeeder.OrganizationId, ApplicationId = DemoSeeder.ApplicationId,
            Slug = "sentinel-" + Guid.NewGuid().ToString("N") };
        db.Policies.Add(sentinel);
        await db.SaveChangesAsync();
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<DeleguardDbContext>();
        Assert.True(await verify.Policies.AnyAsync(p => p.Id == sentinel.Id));
        Assert.Null((await verify.Policies.AsNoTracking().SingleAsync(p => p.Id == draft.Policy.Id)).PublishedVersionId);
        Assert.False((await verify.PolicyVersions.AsNoTracking().SingleAsync(v => v.Id == draft.Version.Id)).Published);
        Assert.Single(await verify.PolicyVersions.Where(v => v.PolicyId == draft.Policy.Id).ToListAsync());
        Assert.Single(await verify.Set<PolicySimulationCase>().Where(c => c.PolicyId == draft.Policy.Id).ToListAsync());
        Assert.Single(await verify.AgentPolicyGrants.Where(g => g.PolicyId == draft.Policy.Id).ToListAsync());
    }

    private static async Task<(CreatedPolicyDraft, PolicyPublicationExpectation)> DraftAsync(PolicyAdminHandler handler)
    {
        var slug = "publication-race-" + Guid.NewGuid().ToString("N");
        var draft = await handler.CreateAsync(new(DemoSeeder.OrganizationId, DemoSeeder.ApplicationId, Yaml(slug, 1, "ALLOW"), [DemoSeeder.AgentId]), default);
        // Both versions deny an unknown action. The race test is about publication state, not fixture coverage.
        await handler.SaveCaseAsync("Unknown actions remain denied", new(DemoSeeder.OrganizationId, draft.Policy.Id, null,
            "DENY", DemoSeeder.AgentId, "production", "unknown.action", "invoice", "fixture", [], []), default);
        var cases = await handler.ListCasesAsync(DemoSeeder.OrganizationId, draft.Policy.Id, default);
        return (draft, new(DemoSeeder.ApplicationId, null, [draft.Version.Id], [DemoSeeder.AgentId], PolicyAdminHandler.CasesFingerprint(cases)));
    }

    private static string Yaml(string slug, int version, string effect) => $"""
        id: {slug}
        version: {version}
        match:
          action: invoice.cancel
          resource_type: invoice
        rules:
          - effect: {effect}
        fallback: DENY
        """;
}
