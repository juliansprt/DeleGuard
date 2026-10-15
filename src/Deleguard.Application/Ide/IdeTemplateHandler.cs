using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Application.Policies;
using Deleguard.Domain.Common;
using Deleguard.Policy;

namespace Deleguard.Application.Ide;

public sealed record IdePolicyTemplate(string Yaml, string FixturesJson);
public sealed record IdeTemplateCommand(string OrganizationId, string ApplicationId, string ProjectId, string AgentId,
    IReadOnlyList<IdePolicyTemplate> Templates);
public sealed record IdeTemplateItem(string Action, string ActionId, string? PolicyId, string Status, int CasesSaved, string Message)
{
    public bool IsReady => Status is "published" or "already_published";
}
public sealed record IdeTemplateResult(IReadOnlyList<IdeTemplateItem> Items);
public sealed record IdeProfileResult(string Profile, IReadOnlyList<IdeTemplateItem> Items, int PublishedCount)
{
    public int ReadyCount => Items.Count(x => x.IsReady);
    public int NeedsAttentionCount => Items.Count - ReadyCount;
    public bool IsReady => Items.Count > 0 && NeedsAttentionCount == 0;
}
public sealed record IdeActionCoverage(string Action, string? PolicyId, bool Published, bool Assigned)
{
    public bool IsReady => PolicyId is not null && Published && Assigned;
}

/// <summary>Called only behind administrative authorization. Never replaces existing policy or action grants.</summary>
public sealed class IdeTemplateHandler(
    ActionAdminHandler actions, PolicyAdminHandler policies, IActionRegistry registry,
    IActionGrantStore grants, IIdeControl ide)
{
    /// <summary>Applies or resumes unchanged starter drafts; customized policies require explicit review.</summary>
    public async Task<IdeProfileResult> ApplyBalancedProfileAsync(IdeTemplateCommand command, CancellationToken ct,
        Func<PublishPolicyCommand, CancellationToken, Task<bool>>? publish = null)
    {
        var prepared = await PrepareAsync(command, ct);
        var templates = command.Templates.Select(t => Prepare(t, command.ProjectId)).ToDictionary(t => t.Policy.Action);
        var items = new List<IdeTemplateItem>();
        var published = 0;
        foreach (var item in prepared.Items)
        {
            if (item.PolicyId is null || item.Status is not ("draft_created" or "existing"))
            {
                items.Add(item);
                continue;
            }
            var template = templates[item.Action];
            var (policy, versions) = await policies.GetAsync(command.OrganizationId, item.PolicyId, ct);
            var expectedPublishedVersionId = policy.PublishedVersionId;
            // A slug alone is not proof that a policy still belongs to the generated profile. Do not
            // auto-publish a customized version, an extra draft, or a policy shared with another agent.
            if (policy.ApplicationId != command.ApplicationId || versions.Count != 1 ||
                versions[0].Version != 1 || versions[0].Yaml != template.Yaml ||
                (policy.PublishedVersionId is not null && policy.PublishedVersionId != versions[0].Id))
            {
                items.Add(item with { Status = "needs_policy_review", Message =
                    "Existing policy preserved: it differs from this profile or contains other versions. Review and publish it explicitly in Policies." });
                continue;
            }
            var bindings = await policies.ListGrantsAsync(command.OrganizationId, [policy.Id], ct);
            if (!bindings.TryGetValue(policy.Id, out var assigned) || assigned.Count != 1 || assigned[0] != command.AgentId)
            {
                items.Add(item with { Status = "needs_policy_assignment", Message =
                    "Policy assignments preserved. This profile requires the policy to be assigned only to the project's agent; review its assignments before retrying." });
                continue;
            }
            var cases = await policies.ListCasesAsync(command.OrganizationId, policy.Id, ct);
            if (cases.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != cases.Count ||
                cases.Any(c => !MatchesGeneratedCase(c, template, command)))
            {
                items.Add(item with { Status = "needs_policy_review", Message =
                    "Saved simulation cases have been customized. No cases or policies were replaced; review and publish explicitly in Policies." });
                continue;
            }
            // Resume an interrupted installation without duplicating cases or erasing custom ones.
            var names = cases.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var added = 0;
            foreach (var fixture in template.Cases.Where(c => !names.Contains(c.Name)))
            {
                await SaveFixtureAsync(command, policy.Id, template, fixture, ct);
                added++;
            }
            var verifiedCases = await policies.ListCasesAsync(command.OrganizationId, policy.Id, ct);
            if (verifiedCases.Count != template.Cases.Count || verifiedCases.Any(c => !MatchesGeneratedCase(c, template, command)))
            {
                items.Add(item with { Status = "needs_policy_review", CasesSaved = item.CasesSaved + added, Message =
                    "Saved cases changed during profile setup. No policy was published; review its current saved cases." });
                continue;
            }
            var expectation = new PolicyPublicationExpectation(command.ApplicationId, expectedPublishedVersionId,
                versions.Select(v => v.Id).ToArray(), assigned.ToArray(), PolicyAdminHandler.CasesFingerprint(verifiedCases));
            var comparisons = await policies.CompareCasesAsync(command.OrganizationId, policy.Id, versions[0].Yaml, ct);
            if (comparisons.Count != template.Cases.Count || comparisons.Any(c => !c.Candidate.Passed))
            {
                items.Add(item with { Status = "needs_policy_review", CasesSaved = item.CasesSaved + added, Message =
                    "Simulation verification did not pass. The policy was not published; review its saved cases." });
                continue;
            }
            if (policy.PublishedVersionId == versions[0].Id && versions[0].Published)
                items.Add(item with { Status = "already_published", CasesSaved = item.CasesSaved + added, Message =
                    "The unchanged profile policy is already published, assigned to this agent and verified against all saved cases." });
            else
            {
                // Recheck the entire verified snapshot under the publication lock. Pinning only
                // VersionId could otherwise republish v1 over a concurrently published restrictive v2.
                try
                {
                    var publication = new PublishPolicyCommand(command.OrganizationId, item.PolicyId, versions[0].Id, expectation);
                    if (publish is not null)
                    {
                        if (!await publish(publication, ct))
                        {
                            items.Add(item with { Status = "needs_publication_authorization", CasesSaved = item.CasesSaved + added,
                                Message = "Draft preserved. An administrator must authorize publication before this rule can become active." });
                            continue;
                        }
                    }
                    else await policies.PublishAsync(publication, ct);
                }
                catch (ConflictException)
                {
                    items.Add(item with { Status = "needs_policy_review", CasesSaved = item.CasesSaved + added, Message =
                        "Policy, assignments or saved cases changed during profile setup. Nothing was published for this action; review the current policy before retrying." });
                    continue;
                }
                published++;
                items.Add(item with { Status = "published", CasesSaved = item.CasesSaved + added, Message =
                    "Profile policy published and assigned to this agent after all saved cases passed." });
            }
        }
        return new("balanced-development", items, published);
    }
    public async Task<IdeTemplateResult> PrepareAsync(IdeTemplateCommand command, CancellationToken ct)
    {
        var project = (await ide.ProjectsAsync(command.OrganizationId, command.ApplicationId, ct))
            .SingleOrDefault(x => x.Id == command.ProjectId && x.AgentId == command.AgentId)
            ?? throw new NotFoundException("IDE project and agent not found in this application.");
        // Parse and execute all fixtures before the first write. IDs are encoded by the YAML writer, never interpolated.
        var prepared = command.Templates.Select(t => Prepare(t, project.Id)).ToArray();
        var expectedActions = ExpectedResourceTypes.Keys;
        if (prepared.Length != ExpectedResourceTypes.Count || !prepared.Select(p => p.Policy.Action).ToHashSet(StringComparer.Ordinal).SetEquals(expectedActions))
            throw new RequestValidationException("Provide exactly the complete IDE policy template library.");
        var existingPolicies = await policies.ListAsync(command.OrganizationId, command.ApplicationId, ct);
        var result = new List<IdeTemplateItem>();
        foreach (var template in prepared)
        {
            var action = await registry.GetByNameAsync(command.OrganizationId, command.ApplicationId, template.Policy.Action, ct);
            if (action is null)
                action = await actions.CreateAsync(new(command.OrganizationId, command.ApplicationId, template.Policy.Action,
                    template.Policy.ResourceType, "HIGH", [command.AgentId]), ct);
            else if (action.ResourceType != template.Policy.ResourceType ||
                     !await grants.HasAsync(command.OrganizationId, command.AgentId, action.Id, ct))
            {
                result.Add(new(action.Name, action.Id, null, "needs_action_configuration", 0,
                    "Existing action preserved. Check its resource type and assign this agent in Actions, then retry."));
                continue;
            }
            var existing = existingPolicies.SingleOrDefault(p => p.Slug == template.Policy.PolicyId);
            if (existing is not null)
            {
                result.Add(new(action.Name, action.Id, existing.Id, "existing", 0,
                    "Existing policy preserved. Review its versions, agent assignments and saved cases in the editor."));
                continue;
            }
            var draft = await policies.CreateAsync(new(command.OrganizationId, command.ApplicationId, template.Yaml, [command.AgentId]), ct);
            foreach (var fixture in template.Cases)
                await SaveFixtureAsync(command, draft.Policy.Id, template, fixture, ct);
            result.Add(new(action.Name, action.Id, draft.Policy.Id, "draft_created", template.Cases.Count,
                "Draft and simulation cases saved. Review project directory rules, compare cases and publish explicitly."));
        }
        return new(result);
    }

    /// <summary>Read-only coverage for the generated per-project slugs. Drafts do not authorize.</summary>
    public async Task<IReadOnlyList<IdeActionCoverage>> CoverageAsync(string organizationId, string applicationId, string projectId, string agentId, CancellationToken ct)
    {
        var project = (await ide.ProjectsAsync(organizationId, applicationId, ct))
            .SingleOrDefault(x => x.Id == projectId && x.AgentId == agentId)
            ?? throw new NotFoundException("IDE project and agent not found in this application.");
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.Id))).ToLowerInvariant();
        var existing = await policies.ListAsync(organizationId, applicationId, ct);
        var policyGrants = existing.Count == 0
            ? new Dictionary<string, IReadOnlyList<string>>()
            : await policies.ListGrantsAsync(organizationId, existing.Select(p => p.Id).ToArray(), ct);
        var registeredActions = await registry.ListAsync(organizationId, applicationId, ct);
        var actionGrants = await grants.ListAgentIdsByActionIdsAsync(organizationId, registeredActions.Select(a => a.Id).ToArray(), ct);
        var byAction = registeredActions.ToDictionary(a => a.Name, StringComparer.Ordinal);
        var bySlug = existing.ToDictionary(p => p.Slug, StringComparer.Ordinal);
        return ExpectedResourceTypes.Keys.Select(action =>
        {
            bySlug.TryGetValue("ide-" + action.Replace('.', '-') + "-" + suffix, out var policy);
            var assigned = policy is not null && policyGrants.TryGetValue(policy.Id, out var agents) && agents.Contains(agentId) &&
                byAction.TryGetValue(action, out var definition) && definition.ResourceType == ExpectedResourceTypes[action] &&
                actionGrants.TryGetValue(definition.Id, out var actionAgents) && actionAgents.Contains(agentId);
            return new IdeActionCoverage(action, policy?.Id, policy?.PublishedVersionId is not null, assigned);
        }).ToArray();
    }

    private Task SaveFixtureAsync(IdeTemplateCommand command, string policyId, Prepared template, Fixture fixture, CancellationToken ct) =>
        policies.SaveCaseAsync(fixture.Name, new(command.OrganizationId, policyId, template.Yaml,
            fixture.Expected, command.AgentId, "development", fixture.Action, fixture.ResourceType,
            fixture.ResourceId, fixture.Attributes, []), ct);

    private static bool MatchesGeneratedCase(Deleguard.Domain.Policies.PolicySimulationCase saved, Prepared template, IdeTemplateCommand command)
    {
        var fixture = template.Cases.SingleOrDefault(c => c.Name == saved.Name);
        if (fixture is null || saved.ExpectedEffect != fixture.Expected) return false;
        try
        {
            var request = JsonSerializer.Deserialize<PolicyTestCommand>(saved.RequestJson);
            return request is not null && request.OrganizationId == command.OrganizationId && request.PolicyId == saved.PolicyId &&
                request.Yaml is null && request.ExpectedEffect == fixture.Expected && request.AgentId == command.AgentId &&
                request.Environment == "development" && request.Action == fixture.Action && request.ResourceType == fixture.ResourceType &&
                request.ResourceId == fixture.ResourceId && request.Context is { Count: 0 } &&
                request.Attributes is not null && request.Attributes.Count == fixture.Attributes.Count &&
                fixture.Attributes.All(a => request.Attributes.TryGetValue(a.Key, out var value) && JsonElement.DeepEquals(a.Value, value));
        }
        catch (JsonException) { return false; }
    }

    private sealed record Fixture(string Name, string Expected, string Action, string ResourceType, string ResourceId,
        Dictionary<string, JsonElement> Attributes);
    private sealed record Prepared(CompiledPolicy Policy, string Yaml, IReadOnlyList<Fixture> Cases);

    private static Prepared Prepare(IdePolicyTemplate input, string projectId)
    {
        try
        {
            var source = PolicyYamlParser.Parse("ide-template", input.Yaml);
            var expectedType = ExpectedResourceTypes.GetValueOrDefault(source.Action);
            if (expectedType is null || source.ResourceType != expectedType || source.Fallback != DecisionEffect.Deny)
                throw new RequestValidationException("IDE templates require the expected resource type and DENY fallback.");
            var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectId))).ToLowerInvariant();
            var policy = source with { PolicyId = "ide-" + source.Action.Replace('.', '-') + "-" + suffix,
                Version = 1, Rules = source.Rules.Select(r => r with { When = Bind(r.When, projectId) }).ToArray() };
            var yaml = PolicyYamlWriter.Write(policy);
            using var json = JsonDocument.Parse(input.FixturesJson);
            var cases = new List<Fixture>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                var request = item.GetProperty("request");
                var attrs = request.GetProperty("attributes").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone());
                if (attrs.TryGetValue("project_id", out var id) && id.ValueKind == JsonValueKind.String)
                    attrs["project_id"] = JsonSerializer.SerializeToElement(id.GetString() == "ide-demo" ? projectId : "fixture-foreign-" + projectId);
                var fixture = new Fixture(item.GetProperty("name").GetString()!, item.GetProperty("expected").GetString()!,
                    request.GetProperty("action").GetString()!, request.GetProperty("resource_type").GetString()!,
                    request.GetProperty("resource_id").GetString()!, attrs);
                if (string.IsNullOrWhiteSpace(fixture.Name) || fixture.Name.Length > 160)
                    throw new RequestValidationException("Invalid IDE fixture name.");
                var envelope = new AuthorizationEnvelope("fixture", "development", null, null, fixture.Action,
                    fixture.ResourceType, fixture.ResourceId, attrs, new Dictionary<string, JsonElement>());
                if (new NativePolicyEngine().Evaluate(envelope, [policy]).Effect != DecisionNames.Parse(fixture.Expected))
                    throw new RequestValidationException($"IDE fixture '{fixture.Name}' failed after project binding.");
                cases.Add(fixture);
            }
            if (cases.Count == 0 || cases.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != cases.Count)
                throw new RequestValidationException("IDE templates require unique saved fixtures.");
            return new(policy, yaml, cases);
        }
        catch (RequestValidationException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or YamlDotNet.Core.YamlException)
        { throw new RequestValidationException("Invalid IDE template or fixture document."); }
    }

    private static ConditionNode Bind(ConditionNode node, string projectId) => node switch
    {
        ConditionNode.Field f when f.Path == "resource.attributes.project_id" && Equals(f.Value, "ide-demo") => f with { Value = projectId },
        ConditionNode.All a => new ConditionNode.All(a.Items.Select(x => Bind(x, projectId)).ToArray()),
        ConditionNode.Any a => new ConditionNode.Any(a.Items.Select(x => Bind(x, projectId)).ToArray()),
        _ => node
    };

    private static readonly IReadOnlyDictionary<string, string> ExpectedResourceTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["filesystem.read"] = "filesystem", ["filesystem.list"] = "filesystem", ["filesystem.search"] = "filesystem",
        ["filesystem.create"] = "filesystem", ["filesystem.write"] = "filesystem", ["filesystem.delete"] = "filesystem", ["filesystem.move"] = "filesystem",
        ["process.execute"] = "process", ["network.connect"] = "network", ["git.clone"] = "git", ["git.fetch"] = "git", ["git.commit"] = "git", ["git.push"] = "git",
        ["package.install"] = "package", ["service.manage"] = "service", ["credential.use"] = "credential", ["mcp.discover"] = "mcp", ["mcp.call"] = "mcp",
        ["ide.settings.change"] = "ide", ["extension.install"] = "extension", ["debug.attach"] = "debug", ["browser.navigate"] = "browser"
    };
}
