using System.Reflection;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Ide;
using Deleguard.Application.Policies;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Ide;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Policies;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class IdeTemplateHandlerTests
{
    [Fact]
    public async Task Denied_publication_keeps_generated_profile_in_draft()
    {
        var fixture = new ProfileHarness();
        var attempted = new List<PublishPolicyCommand>();
        var result = await fixture.Handler.ApplyBalancedProfileAsync(fixture.Command, default, (command, _) =>
        {
            attempted.Add(command);
            return Task.FromResult(false);
        });
        Assert.Equal(22, attempted.Count);
        Assert.All(attempted, command => { Assert.NotNull(command.Expected); Assert.Equal("org", command.OrganizationId); });
        Assert.All(fixture.Definitions, p => Assert.Null(p.PublishedVersionId));
        Assert.All(result.Items, item => Assert.Equal("needs_publication_authorization", item.Status));
        Assert.Equal(0, result.PublishedCount);
        Assert.False(result.IsReady);
    }

    [Theory]
    [InlineData("customer-project")]
    [InlineData("different")]
    [InlineData("project: with quoted \"value\"")]
    public async Task Binds_project_and_keeps_existing_unassigned_actions_unchanged(string project)
    {
        var policyRepository = Stub<IPolicyRepository>((name, _) => name == "ListAsync"
            ? Task.FromResult<IReadOnlyList<PolicySummary>>([]) : throw new Exception("Unexpected write: " + name));
        var policies = new PolicyAdminHandler(policyRepository, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var registry = Stub<IActionRegistry>((name, args) => name == "GetByNameAsync"
            ? Task.FromResult<ActionDefinition?>(new() { Id = "existing", OrganizationId = "org", ApplicationId = "app", Name = (string)args[2]!, ResourceType =
                ((string)args[2]!).StartsWith("filesystem.") ? "filesystem" : (string)args[2]! == "process.execute" ? "process" : "mcp" })
            : throw new Exception("Unexpected write: " + name));
        var grants = Stub<IActionGrantStore>((name, _) => name == "HasAsync" ? Task.FromResult(false) : throw new Exception("Grant mutation"));
        var handler = new IdeTemplateHandler(null!, policies, registry, grants, Control(project));
        var result = await handler.PrepareAsync(new("org", "app", project, "agent", Templates()), default);
        Assert.Equal(22, result.Items.Count);
        Assert.All(result.Items, i => { Assert.Equal("needs_action_configuration", i.Status); Assert.Null(i.PolicyId); });
    }

    [Fact]
    public async Task Rejects_foreign_project_before_any_write()
    {
        var handler = new IdeTemplateHandler(null!, null!, null!, null!, Control("registered"));
        await Assert.ThrowsAsync<NotFoundException>(() => handler.PrepareAsync(new("org", "app", "foreign", "agent", Templates()), default));
    }

    [Fact]
    public async Task Failing_fixture_rejects_entire_library_before_any_write()
    {
        var templates = Templates().ToArray();
        var index = Array.FindIndex(templates, template => template.FixturesJson.Contains("\"expected\": \"ALLOW\"", StringComparison.Ordinal));
        Assert.True(index >= 0);
        templates[index] = templates[index] with { FixturesJson = templates[index].FixturesJson.Replace("\"expected\": \"ALLOW\"", "\"expected\": \"DENY\"", StringComparison.Ordinal) };
        var handler = new IdeTemplateHandler(null!, null!, null!, null!, Control("registered"));
        await Assert.ThrowsAsync<RequestValidationException>(() => handler.PrepareAsync(new("org", "app", "registered", "agent", templates), default));
    }

    [Fact]
    public async Task Coverage_reports_unpublished_generated_drafts_until_profile_publish()
    {
        var fixture = new ProfileHarness();
        var empty = await fixture.Handler.CoverageAsync("org", "app", "financial-staff", "agent", default);
        Assert.Equal(22, empty.Count);
        Assert.All(empty, item => { Assert.Null(item.PolicyId); Assert.False(item.IsReady); });
        await fixture.Handler.PrepareAsync(fixture.Command, default);
        var drafts = await fixture.Handler.CoverageAsync("org", "app", "financial-staff", "agent", default);
        Assert.All(drafts, item => { Assert.NotNull(item.PolicyId); Assert.True(item.Assigned); Assert.False(item.Published); });
        Assert.Contains(drafts, item => item.Action == "filesystem.write");
        Assert.Contains(drafts, item => item.Action == "filesystem.create");
        Assert.Contains(drafts, item => item.Action == "filesystem.search");
        await fixture.Handler.ApplyBalancedProfileAsync(fixture.Command, default);
        Assert.All(await fixture.Handler.CoverageAsync("org", "app", "financial-staff", "agent", default), item => Assert.True(item.IsReady));
    }

    [Fact]
    public async Task Published_policy_is_not_ready_after_action_assignment_is_removed()
    {
        var fixture = new ProfileHarness();
        await fixture.Handler.ApplyBalancedProfileAsync(fixture.Command, default);
        fixture.UnassignedActions.Add("filesystem.read");
        var coverage = await fixture.Handler.CoverageAsync("org", "app", "financial-staff", "agent", default);
        var removed = Assert.Single(coverage, item => item.Action == "filesystem.read");
        Assert.True(removed.Published);
        Assert.False(removed.Assigned);
        Assert.False(removed.IsReady);
        Assert.Equal(21, coverage.Count(item => item.IsReady));
    }

    [Fact]
    public async Task Apply_resumes_existing_generated_drafts_and_reports_actual_publication()
    {
        var fixture = new ProfileHarness();
        await fixture.Handler.PrepareAsync(fixture.Command, default);
        Assert.All(fixture.Definitions, p => Assert.Null(p.PublishedVersionId));
        var caseCount = fixture.Cases.Count;
        var result = await fixture.Handler.ApplyBalancedProfileAsync(fixture.Command, default);
        Assert.True(result.IsReady);
        Assert.Equal(22, result.PublishedCount);
        Assert.Equal(22, result.ReadyCount);
        Assert.Equal(0, result.NeedsAttentionCount);
        Assert.All(result.Items, item => Assert.Equal("published", item.Status));
        Assert.Equal(caseCount, fixture.Cases.Count);
        Assert.All(fixture.Definitions, p => Assert.NotNull(p.PublishedVersionId));
    }

    [Fact]
    public async Task Applying_published_profile_again_is_verified_and_does_not_write()
    {
        var fixture = new ProfileHarness();
        await fixture.Handler.ApplyBalancedProfileAsync(fixture.Command, default);
        var writes = fixture.Writes;
        var result = await fixture.Handler.ApplyBalancedProfileAsync(fixture.Command, default);
        Assert.True(result.IsReady);
        Assert.Equal(0, result.PublishedCount);
        Assert.All(result.Items, item => Assert.Equal("already_published", item.Status));
        Assert.Equal(writes, fixture.Writes);
    }

    [Fact]
    public async Task Missing_generated_cases_are_resumed_without_duplicates()
    {
        var fixture = new ProfileHarness();
        await fixture.Handler.PrepareAsync(fixture.Command, default);
        var count = fixture.Cases.Count;
        var missing = fixture.Cases[0];
        fixture.Cases.RemoveAt(0);
        var result = await fixture.Handler.ApplyBalancedProfileAsync(fixture.Command, default);
        Assert.True(result.IsReady);
        Assert.Equal(count, fixture.Cases.Count);
        Assert.Single(fixture.Cases, c => c.PolicyId == missing.PolicyId && c.Name == missing.Name);
        Assert.Equal(1, result.Items.Sum(i => i.CasesSaved));
    }

    [Theory]
    [InlineData("custom-yaml", "needs_policy_review")]
    [InlineData("extra-version", "needs_policy_review")]
    [InlineData("missing-policy-assignment", "needs_policy_assignment")]
    [InlineData("shared-policy-assignment", "needs_policy_assignment")]
    [InlineData("custom-case", "needs_policy_review")]
    [InlineData("duplicate-case", "needs_policy_review")]
    [InlineData("missing-action-assignment", "needs_action_configuration")]
    public async Task Existing_customized_or_unassigned_policy_is_not_replaced_or_reported_ready(string change, string expectedStatus)
    {
        var fixture = new ProfileHarness();
        await fixture.Handler.PrepareAsync(fixture.Command, default);
        var version = fixture.Versions.First(v => v.Action == "filesystem.read");
        var policy = fixture.Definitions.Single(p => p.Id == version.PolicyId);
        var saved = fixture.Cases.First(c => c.PolicyId == policy.Id);
        switch (change)
        {
            case "custom-yaml":
                fixture.Versions.Remove(version);
                version = new() { Id = version.Id, OrganizationId = "org", PolicyId = policy.Id, Version = 1,
                    Action = version.Action, ResourceType = version.ResourceType, Yaml = version.Yaml + "\n# Customized by an administrator\n" };
                fixture.Versions.Add(version);
                break;
            case "extra-version": fixture.Versions.Add(new() { Id = "custom-v2", OrganizationId = "org", PolicyId = policy.Id, Version = 2,
                Action = version.Action, ResourceType = version.ResourceType, Yaml = version.Yaml }); break;
            case "missing-policy-assignment": fixture.PolicyAgents[policy.Id] = []; break;
            case "shared-policy-assignment": fixture.PolicyAgents[policy.Id] = ["agent", "another-agent"]; break;
            case "custom-case": saved.ExpectedEffect = "CUSTOM"; break;
            case "duplicate-case": fixture.Cases.Add(saved); break;
            case "missing-action-assignment": fixture.UnassignedActions.Add("filesystem.read"); break;
        }
        var yaml = version.Yaml;
        var cases = fixture.Cases.ToArray();
        var assignments = fixture.PolicyAgents[policy.Id].ToArray();
        var result = await fixture.Handler.ApplyBalancedProfileAsync(fixture.Command, default);
        Assert.False(result.IsReady);
        Assert.Equal(21, result.ReadyCount);
        Assert.Equal(1, result.NeedsAttentionCount);
        Assert.Equal(expectedStatus, result.Items.Single(i => i.Action == "filesystem.read").Status);
        Assert.Null(policy.PublishedVersionId);
        Assert.False(version.Published);
        Assert.Equal(yaml, version.Yaml);
        Assert.Equal(assignments, fixture.PolicyAgents[policy.Id]);
        Assert.Equal(cases, fixture.Cases);
    }

    [Fact]
    public async Task Action_without_assignment_is_never_silently_granted_and_empty_profile_is_not_ready()
    {
        var fixture = new ProfileHarness();
        fixture.UnassignedActions.UnionWith(Templates().Select(t => PolicyYamlParser.Parse("fixture", t.Yaml).Action));
        var result = await fixture.Handler.ApplyBalancedProfileAsync(fixture.Command, default);
        Assert.False(result.IsReady);
        Assert.Equal(22, result.NeedsAttentionCount);
        Assert.Equal(0, result.ReadyCount);
        Assert.Equal(0, result.PublishedCount);
        Assert.Empty(fixture.Definitions);
        Assert.Equal(0, fixture.Writes);
        Assert.False(new IdeProfileResult("balanced-development", [], 0).IsReady);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("published")]
    [InlineData("assignment")]
    [InlineData("fixture")]
    public async Task Publication_rejects_concurrent_changes_instead_of_reverting_them(string change)
    {
        var fixture = new ProfileHarness();
        await fixture.Handler.PrepareAsync(fixture.Command, default);
        var version = fixture.Versions.First(v => v.Action == "filesystem.read");
        fixture.BeforeVersionUpdate = policyId =>
        {
            if (policyId != version.PolicyId) return;
            if (change is "draft" or "published")
            {
                fixture.Versions.Add(new() { Id = "concurrent-v2", OrganizationId = "org", PolicyId = policyId, Version = 2,
                    Published = change == "published", Action = version.Action, ResourceType = version.ResourceType,
                    Yaml = version.Yaml.Replace("version: 1", "version: 2") });
                if (change == "published") fixture.Definitions.Single(p => p.Id == policyId).PublishedVersionId = "concurrent-v2";
            }
            if (change == "assignment") fixture.PolicyAgents[policyId] = ["another-agent"];
            if (change == "fixture") fixture.Cases.First(c => c.PolicyId == policyId).ExpectedEffect = "DENY";
        };
        var result = await fixture.Handler.ApplyBalancedProfileAsync(fixture.Command, default);
        Assert.False(result.IsReady);
        Assert.Equal("needs_policy_review", result.Items.Single(i => i.Action == "filesystem.read").Status);
        Assert.Equal(change == "published" ? "concurrent-v2" : null,
            fixture.Definitions.Single(p => p.Id == version.PolicyId).PublishedVersionId);
        Assert.False(version.Published);
        if (change == "published") Assert.True(fixture.Versions.Single(v => v.Id == "concurrent-v2").Published);
        if (change == "assignment") Assert.Equal(["another-agent"], fixture.PolicyAgents[version.PolicyId]);
        if (change == "fixture") Assert.Equal("DENY", fixture.Cases.First(c => c.PolicyId == version.PolicyId).ExpectedEffect);
    }

    private sealed class ProfileHarness
    {
        public List<PolicyDefinition> Definitions { get; } = [];
        public List<PolicyVersion> Versions { get; } = [];
        public List<PolicySimulationCase> Cases { get; } = [];
        public Dictionary<string, IReadOnlyList<string>> PolicyAgents { get; } = [];
        public HashSet<string> UnassignedActions { get; } = [];
        public Action<string>? BeforeVersionUpdate { get; set; }
        public int Writes { get; private set; }
        public IdeTemplateCommand Command { get; } = new("org", "app", "financial-staff", "agent", Templates());
        public IdeTemplateHandler Handler { get; }

        public ProfileHarness()
        {
            var resourceTypes = Templates().Select(t => PolicyYamlParser.Parse("fixture", t.Yaml)).ToDictionary(p => p.Action, p => p.ResourceType);
            var registry = Stub<IActionRegistry>((name, args) => name switch {
                "GetByNameAsync" => Task.FromResult<ActionDefinition?>(new() { Id = (string)args[2]!, OrganizationId = "org", ApplicationId = "app",
                    Name = (string)args[2]!, ResourceType = resourceTypes[(string)args[2]!] }),
                "ListAsync" => Task.FromResult<IReadOnlyList<ActionDefinition>>(resourceTypes.Select(pair => new ActionDefinition {
                    Id=pair.Key, OrganizationId="org", ApplicationId="app", Name=pair.Key, ResourceType=pair.Value }).ToArray()),
                _ => throw new Exception("Unexpected action write: " + name)
            });
            var grants = Stub<IActionGrantStore>((name, args) => name switch
            {
                "HasAsync" => Task.FromResult(!UnassignedActions.Contains((string)args[2]!)),
                "ListAgentIdsForActionAsync" => Task.FromResult<IReadOnlyList<string>>(["agent"]),
                "ListAgentIdsByActionIdsAsync" => Task.FromResult<IReadOnlyDictionary<string,IReadOnlyList<string>>>(resourceTypes.Keys.ToDictionary(
                    action => action, action => (IReadOnlyList<string>)(UnassignedActions.Contains(action) ? [] : new[]{"agent"}))),
                _ => throw new Exception("Unexpected grant mutation: " + name)
            });
            var repository = Stub<IPolicyRepository>((name, args) =>
            {
                if (name == "ExecuteLockedAsync") return ((Delegate)args[2]!).DynamicInvoke();
                if (name == "ListAsync") return Task.FromResult<IReadOnlyList<PolicySummary>>(Definitions.Select(p =>
                    new PolicySummary(p.Id, p.Slug, p.ApplicationId, p.PublishedVersionId, Versions.Count(v => v.PolicyId == p.Id),
                        p.PublishedVersionId is null ? null : 1, Versions.FirstOrDefault(v => v.PolicyId == p.Id)?.Action,
                        Versions.FirstOrDefault(v => v.PolicyId == p.Id)?.ResourceType)).ToArray());
                if (name == "GetAsync") return Task.FromResult(Definitions.SingleOrDefault(p => p.OrganizationId == (string)args[0]! && p.Id == (string)args[1]!));
                if (name == "GetVersionsForUpdateAsync") BeforeVersionUpdate?.Invoke((string)args[1]!);
                if (name is "ListVersionsAsync" or "GetVersionsForUpdateAsync")
                    return Task.FromResult<IReadOnlyList<PolicyVersion>>(Versions.Where(v => v.PolicyId == (string)args[1]!).ToArray());
                if (name == "AddDefinitionAsync") { Definitions.Add((PolicyDefinition)args[0]!); return Task.CompletedTask; }
                if (name == "AddVersionAsync") { Versions.Add((PolicyVersion)args[0]!); return Task.CompletedTask; }
                throw new Exception("Unexpected policy operation: " + name);
            });
            var policyGrants = Stub<IPolicyGrantStore>((name, args) =>
            {
                if (name == "ReplaceAsync") { PolicyAgents[(string)args[2]!] = ((IReadOnlyList<string>)args[3]!).ToArray(); return Task.CompletedTask; }
                if (name == "ListAgentIdsByPolicyIdsAsync") return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(PolicyAgents
                    .Where(p => ((IReadOnlyList<string>)args[1]!).Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
                if (name == "ListAgentIdsForPolicyAsync") return Task.FromResult(PolicyAgents[(string)args[1]!]);
                throw new Exception("Unexpected policy grant operation: " + name);
            });
            var agents = Stub<IAgentDirectory>((name, _) => name == "ListAsync"
                ? Task.FromResult<IReadOnlyList<AgentListItem>>([new("agent", "develop-policy", "Agent", "app", "env", "develop", true, "dgk_example")])
                : throw new Exception(name));
            var applications = Stub<IApplicationDirectory>((name, _) => name == "GetAsync"
                ? Task.FromResult<ApplicationListItem?>(new("app", "app", "Application", ApplicationKinds.Product, DateTimeOffset.UtcNow, 1))
                : throw new Exception(name));
            var simulations = Stub<IPolicySimulationStore>((name, args) =>
            {
                if (name == "ListAsync") return Task.FromResult<IReadOnlyList<PolicySimulationCase>>(Cases.Where(c => c.PolicyId == (string)args[2]!).ToArray());
                if (name == "AddAsync") { Cases.Add((PolicySimulationCase)args[0]!); return Task.CompletedTask; }
                throw new Exception(name);
            });
            var uow = Stub<IUnitOfWork>((name, _) => name == "SaveChangesAsync" ? Task.FromResult(++Writes) : throw new Exception(name));
            var clock = Stub<IClock>((_, _) => DateTimeOffset.UtcNow);
            var policies = new PolicyAdminHandler(repository, policyGrants, registry, grants, agents, applications, new NativePolicyEngine(), clock, uow, simulations);
            Handler = new(null!, policies, registry, grants, Control("financial-staff"));
        }
    }

    private static IReadOnlyList<IdePolicyTemplate> Templates() => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "policies"), "ide-*.yaml")
        .OrderBy(p => p, StringComparer.Ordinal).Select(p => new IdePolicyTemplate(File.ReadAllText(p),
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "policies", "fixtures", Path.GetFileNameWithoutExtension(p) + ".json")))).ToArray();

    private static IIdeControl Control(string project) => Stub<IIdeControl>((name, _) => name == "ProjectsAsync"
        ? Task.FromResult<IReadOnlyList<IdeProject>>([new() { Id = project, AgentId = "agent", OrganizationId = "org", ApplicationId = "app" }])
        : throw new Exception("Unexpected control write"));
    public class Proxy : DispatchProxy
    {
        public Func<string, object?[], object?> Handler = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!.Name, args ?? []);
    }
    private static T Stub<T>(Func<string, object?[], object?> handler) where T : class
    {
        var result = DispatchProxy.Create<T, Proxy>();
        ((Proxy)(object)result).Handler = handler;
        return result;
    }
}
