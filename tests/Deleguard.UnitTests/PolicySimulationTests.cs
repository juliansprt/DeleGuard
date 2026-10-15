using System.Reflection;
using System.Text.Json;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Policies;
using Deleguard.Domain.Policies;
using Deleguard.Domain.Identity;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class PolicySimulationTests
{
    private const string Yaml = "id: sample\nversion: 1\nmatch:\n  action: invoice.cancel\n  resource_type: invoice\nrules:\n  - effect: ALLOW\nfallback: DENY\n";

    [Fact]
    public async Task Comparison_preserves_request_match_and_does_not_write()
    {
        var fixture = new Harness();
        await fixture.Handler.SaveCaseAsync("Allowed invoice", fixture.Command("ALLOW"), default);
        var writes = fixture.Writes;
        var results = await fixture.Handler.CompareCasesAsync("org", "policy", Yaml.Replace("invoice.cancel", "email.send"), default);
        var result = Assert.Single(results);
        Assert.Equal("ALLOW", result.Published!.ActualEffect);
        Assert.Equal("DENY", result.Candidate.ActualEffect);
        Assert.True(result.DecisionChanged);
        Assert.False(result.Candidate.Passed);
        Assert.Equal(writes, fixture.Writes);
    }

    [Fact]
    public async Task Publication_requires_cases_and_rechecks_expectations()
    {
        var fixture = new Harness();
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Handler.PublishAsync(new("org", "policy", "v1"), default));
        await fixture.Handler.SaveCaseAsync("Wrong expectation", fixture.Command("DENY"), default);
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Handler.PublishAsync(new("org", "policy", "v1"), default));
        Assert.Equal(1, fixture.Writes);
        fixture.Cases.Clear();
        await fixture.Handler.SaveCaseAsync("Expected allow", fixture.Command("ALLOW"), default);
        var version = await fixture.Handler.PublishAsync(new("org", "policy", "v1"), default);
        Assert.True(version.Published);
    }

    [Fact]
    public async Task Foreign_tenant_cannot_read_or_store_cases()
    {
        var fixture = new Harness();
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Handler.ListCasesAsync("other", "policy", default));
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Handler.SaveCaseAsync("Foreign", fixture.Command("ALLOW") with { OrganizationId = "other" }, default));
        Assert.Empty(fixture.Cases);
    }

    [Fact]
    public async Task Product_policy_cannot_bypass_regressions_with_an_admin_action()
    {
        var fixture = new Harness(action: "admin.policy.publish");
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Handler.PublishAsync(new("org", "policy", "v1"), default));
    }

    [Fact]
    public async Task Selected_application_is_required_for_product_policy_even_with_admin_action()
    {
        var fixture = new Harness(action: "admin.policy.publish");
        await fixture.Handler.EnsureApplicationAccessAsync("org", "policy", "app", default);
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Handler.EnsureApplicationAccessAsync("org", "policy", "another-app", default));
    }

    [Fact]
    public async Task Real_administration_catalog_remains_accessible_and_publishable()
    {
        var fixture = new Harness(action: "admin.policy.publish", administration: true);
        await fixture.Handler.EnsureApplicationAccessAsync("org", "policy", "product-app", default);
        var version = await fixture.Handler.PublishAsync(new("org", "policy", "v1"), default);
        Assert.True(version.Published);
    }

    [Theory]
    [InlineData("draft", 150000, false, "ALLOW")]
    [InlineData("draft", 15000000, false, "REQUIRE_APPROVAL")]
    [InlineData("draft", 150000, true, "DENY")]
    [InlineData("issued", 150000, false, "DENY")]
    public async Task Saved_cases_preserve_all_effects_and_default_deny(string status, int amount, bool reported, string expected)
    {
        var fixture = new Harness();
        var yaml = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "policies", "invoice-cancel-production.yaml"));
        var attributes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(new { status, amount, reportedToTaxAuthority = reported }));
        await fixture.Handler.SaveCaseAsync(expected, fixture.Command(expected) with { Yaml = yaml, Attributes = attributes }, default);
        var results = await fixture.Handler.CompareCasesAsync("org", "policy", yaml, default);
        Assert.True(Assert.Single(results).Candidate.Passed);
        Assert.Equal(expected, results[0].Candidate.ActualEffect);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public async Task Saved_cases_require_an_explicit_supported_expectation(string effect)
    {
        var fixture = new Harness();
        await Assert.ThrowsAsync<RequestValidationException>(() => fixture.Handler.SaveCaseAsync("Invalid", fixture.Command(effect), default));
        Assert.Empty(fixture.Cases);
    }

    private sealed class Harness
    {
        public readonly List<PolicySimulationCase> Cases = [];
        public int Writes;
        public PolicyAdminHandler Handler { get; }
        public PolicyTestCommand Command(string effect) => new("org", "policy", Yaml, effect, "agent", "production", null, null, "invoice", null, null);
        public Harness(string action = "invoice.cancel", bool administration = false)
        {
            var slug = administration ? "admin-policy-publish" : "sample";
            var policy = new PolicyDefinition { Id = "policy", OrganizationId = "org", ApplicationId = "app", Slug = slug, PublishedVersionId = "v1" };
            IReadOnlyList<PolicyVersion> versions = [new() { Id = "v1", OrganizationId = "org", PolicyId = "policy", Version = 1, Yaml = Yaml.Replace("invoice.cancel", action).Replace("id: sample", "id: " + slug), Action = action, ResourceType = "invoice", Published = true }];
            var repository = Stub<IPolicyRepository>((name, args) => name switch
            {
                "ExecuteLockedAsync" => ((Delegate)args[2]!).DynamicInvoke(),
                "GetAsync" => Task.FromResult((string)args[0]! == "org" ? policy : null),
                "ListVersionsAsync" or "GetVersionsForUpdateAsync" => Task.FromResult(versions),
                _ => throw new InvalidOperationException(name)
            });
            var store = Stub<IPolicySimulationStore>((name, args) =>
            {
                if (name == "ListAsync") return Task.FromResult<IReadOnlyList<PolicySimulationCase>>(Cases.Where(x => x.OrganizationId == (string)args[0]! && x.ApplicationId == (string)args[1]! && x.PolicyId == (string)args[2]!).ToList());
                if (name == "AddAsync") { Cases.Add((PolicySimulationCase)args[0]!); return Task.CompletedTask; }
                throw new InvalidOperationException(name);
            });
            var clock = Stub<IClock>((_, _) => DateTimeOffset.UtcNow);
            var applications = Stub<IApplicationDirectory>((_, _) => Task.FromResult<ApplicationListItem?>(new(
                "app", "app", "Application", administration ? ApplicationKinds.Administration : ApplicationKinds.Product, DateTimeOffset.UtcNow, 0)));
            var uow = Stub<IUnitOfWork>((name, _) => name == "SaveChangesAsync" ? Task.FromResult(++Writes) : throw new InvalidOperationException(name));
            Handler = new(repository, null!, null!, null!, null!, applications, new NativePolicyEngine(), clock, uow, store);
        }
    }

    public class PortProxy : DispatchProxy
    {
        public Func<string, object?[], object?> InvokePort { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => InvokePort(method!.Name, args ?? []);
    }

    private static T Stub<T>(Func<string, object?[], object?> invoke) where T : class
    {
        var result = DispatchProxy.Create<T, PortProxy>();
        ((PortProxy)(object)result).InvokePort = invoke;
        return result;
    }
}
