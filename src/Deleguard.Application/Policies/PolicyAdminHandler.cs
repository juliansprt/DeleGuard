using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Policies;
using Deleguard.Policy;

namespace Deleguard.Application.Policies;

public sealed record CreatePolicyCommand(string OrganizationId, string ApplicationId, string Yaml, IReadOnlyList<string> AgentIds);

public sealed record CreatedPolicyDraft(PolicyDefinition Policy, PolicyVersion Version);

public sealed record AddPolicyVersionCommand(string OrganizationId, string PolicyId, string Yaml);

public sealed record PolicyPublicationExpectation(string ApplicationId, string? PublishedVersionId,
    IReadOnlyList<string> VersionIds, IReadOnlyList<string> AgentIds, string CasesFingerprint);

public sealed record PublishPolicyCommand(string OrganizationId, string PolicyId, string? VersionId,
    PolicyPublicationExpectation? Expected = null);

public sealed record PolicyTestCommand(
    string OrganizationId,
    string PolicyId,
    string? Yaml,
    string? ExpectedEffect,
    string? AgentId,
    string? Environment,
    string? Action,
    string? ResourceType,
    string? ResourceId,
    Dictionary<string, JsonElement>? Attributes,
    Dictionary<string, JsonElement>? Context);

public sealed record PolicyTestResult(
    bool Passed,
    string ActualEffect,
    string? ExpectedEffect,
    string Reason,
    string? PolicyId,
    int? PolicyVersion,
    string? MatchedRule);

public sealed record PolicyCaseComparison(string Id, string Name, PolicyTestResult Candidate, PolicyTestResult? Published)
{
    public bool DecisionChanged => Published is not null && Candidate.ActualEffect != Published.ActualEffect;
}

public sealed class PolicyAdminHandler
{
    private readonly IPolicyRepository _policies;
    private readonly IPolicyGrantStore _grants;
    private readonly IActionRegistry _actions;
    private readonly IActionGrantStore _actionGrants;
    private readonly IAgentDirectory _agents;
    private readonly IApplicationDirectory _applications;
    private readonly IPolicyEngine _engine;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;
    private readonly IPolicySimulationStore _simulations;

    public PolicyAdminHandler(
        IPolicyRepository policies,
        IPolicyGrantStore grants,
        IActionRegistry actions,
        IActionGrantStore actionGrants,
        IAgentDirectory agents,
        IApplicationDirectory applications,
        IPolicyEngine engine,
        IClock clock,
        IUnitOfWork uow,
        IPolicySimulationStore simulations)
    {
        _policies = policies;
        _grants = grants;
        _actions = actions;
        _actionGrants = actionGrants;
        _agents = agents;
        _applications = applications;
        _engine = engine;
        _clock = clock;
        _uow = uow;
        _simulations = simulations;
    }

    public Task<IReadOnlyList<PolicySummary>> ListAsync(
        string organizationId,
        string? applicationId,
        CancellationToken cancellationToken) =>
        _policies.ListAsync(organizationId, applicationId, cancellationToken);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ListGrantsAsync(
        string organizationId,
        IReadOnlyList<string> policyIds,
        CancellationToken cancellationToken) =>
        _grants.ListAgentIdsByPolicyIdsAsync(organizationId, policyIds, cancellationToken);

    public async Task<(PolicyDefinition Policy, IReadOnlyList<PolicyVersion> Versions)> GetAsync(
        string organizationId,
        string policyId,
        CancellationToken cancellationToken)
    {
        var policy = await _policies.GetAsync(organizationId, policyId, cancellationToken)
                     ?? throw new NotFoundException("Policy not found.");
        var versions = await _policies.ListVersionsAsync(organizationId, policyId, cancellationToken);
        return (policy, versions);
    }

    public async Task<CreatedPolicyDraft> CreateAsync(CreatePolicyCommand command, CancellationToken cancellationToken)
    {
        var compiled = ParseYaml("policy", command.Yaml);
        if (IsAdministrationCatalog(compiled.PolicyId, compiled.Action))
        {
            throw new RequestValidationException("Administration catalog policies cannot be created here.");
        }

        var application = await _applications.GetAsync(command.OrganizationId, command.ApplicationId, cancellationToken)
                          ?? throw new NotFoundException("Application not found.");
        if (string.Equals(application.Kind, ApplicationKinds.Administration, StringComparison.Ordinal))
        {
            throw new RequestValidationException("Product policies cannot be created in the administration application.");
        }

        var existing = await _policies.ListAsync(command.OrganizationId, application.Id, cancellationToken);
        if (existing.Any(p => string.Equals(p.Slug, compiled.PolicyId, StringComparison.Ordinal)))
        {
            throw new ConflictException("A policy with that id already exists.");
        }

        var agentIds = await NormalizeAgentIdsAsync(
            command.OrganizationId,
            application.Id,
            compiled.Action,
            command.AgentIds,
            cancellationToken);
        var now = _clock.UtcNow;
        var policy = new PolicyDefinition
        {
            Id = IdGenerator.New("pol"),
            OrganizationId = command.OrganizationId,
            ApplicationId = application.Id,
            Slug = compiled.PolicyId,
            PublishedVersionId = null,
            CreatedAt = now
        };
        var version = NewVersion(command.OrganizationId, policy.Id, compiled, command.Yaml, now);
        await _policies.AddDefinitionAsync(policy, cancellationToken);
        await _policies.AddVersionAsync(version, cancellationToken);
        await _grants.ReplaceAsync(command.OrganizationId, application.Id, policy.Id, agentIds, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return new CreatedPolicyDraft(policy, version);
    }

    public Task<PolicyVersion> AddVersionAsync(AddPolicyVersionCommand command, CancellationToken cancellationToken) =>
        _policies.ExecuteLockedAsync(command.OrganizationId, command.PolicyId, () => AddVersionLockedAsync(command, cancellationToken), cancellationToken);

    private async Task<PolicyVersion> AddVersionLockedAsync(AddPolicyVersionCommand command, CancellationToken cancellationToken)
    {
        var policy = await _policies.GetAsync(command.OrganizationId, command.PolicyId, cancellationToken)
                     ?? throw new NotFoundException("Policy not found.");
        var compiled = ParseYaml(policy.Slug, command.Yaml);
        if (!string.Equals(compiled.PolicyId, policy.Slug, StringComparison.Ordinal))
        {
            throw new RequestValidationException($"YAML id '{compiled.PolicyId}' must match policy slug '{policy.Slug}'.");
        }

        var versions = await _policies.ListVersionsAsync(command.OrganizationId, command.PolicyId, cancellationToken);
        if (versions.Any(v => v.Version == compiled.Version))
        {
            throw new ConflictException($"Version {compiled.Version} already exists for this policy.");
        }

        var version = NewVersion(command.OrganizationId, policy.Id, compiled, command.Yaml, _clock.UtcNow);
        await _policies.AddVersionAsync(version, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return version;
    }

    public Task<PolicyVersion> PublishAsync(PublishPolicyCommand command, CancellationToken cancellationToken) =>
        _policies.ExecuteLockedAsync(command.OrganizationId, command.PolicyId, () => PublishLockedAsync(command, cancellationToken), cancellationToken);

    private async Task<PolicyVersion> PublishLockedAsync(PublishPolicyCommand command, CancellationToken cancellationToken)
    {
        var policy = await _policies.GetAsync(command.OrganizationId, command.PolicyId, cancellationToken)
                     ?? throw new NotFoundException("Policy not found.");
        var versions = await _policies.GetVersionsForUpdateAsync(command.OrganizationId, command.PolicyId, cancellationToken);
        if (versions.Count == 0)
        {
            throw new ConflictException("Policy has no versions to publish.");
        }

        if (command.Expected is { } expected)
        {
            var agents = await _grants.ListAgentIdsForPolicyAsync(command.OrganizationId, command.PolicyId, cancellationToken);
            var cases = await _simulations.ListAsync(command.OrganizationId, policy.ApplicationId, policy.Id, cancellationToken);
            if (policy.ApplicationId != expected.ApplicationId || policy.PublishedVersionId != expected.PublishedVersionId ||
                !versions.Select(v => v.Id).Order(StringComparer.Ordinal).SequenceEqual(expected.VersionIds.Order(StringComparer.Ordinal)) ||
                !agents.Order(StringComparer.Ordinal).SequenceEqual(expected.AgentIds.Order(StringComparer.Ordinal)) ||
                CasesFingerprint(cases) != expected.CasesFingerprint)
                throw new ConflictException("Policy, assignments or saved cases changed during profile setup. Nothing was published; review the current policy before retrying.");
        }

        PolicyVersion target;
        if (string.IsNullOrWhiteSpace(command.VersionId))
        {
            throw new RequestValidationException("version_id is required; the approved publication must identify an immutable policy version.");
        }
        else
        {
            target = versions.FirstOrDefault(v => v.Id == command.VersionId)
                     ?? throw new NotFoundException("Policy version not found.");
        }

        ParseYaml(policy.Slug, target.Yaml);
        if (!await IsAdministrationPolicyAsync(policy, cancellationToken))
        {
            var results = await CompareCasesAsync(command.OrganizationId, command.PolicyId, target.Yaml, cancellationToken);
            if (results.Count == 0 || results.Any(x => !x.Candidate.Passed))
                throw new ConflictException("Publishing requires at least one saved case and all saved cases must pass against this version. Open the simulator to save and compare cases.");
        }
        var now = _clock.UtcNow;
        foreach (var version in versions)
        {
            version.Published = version.Id == target.Id;
            if (version.Published)
            {
                version.PublishedAt = now;
            }
        }

        policy.PublishedVersionId = target.Id;
        await _uow.SaveChangesAsync(cancellationToken);
        return target;
    }

    public async Task<PolicyTestResult> TestAsync(PolicyTestCommand command, CancellationToken cancellationToken)
    {
        var policy = await _policies.GetAsync(command.OrganizationId, command.PolicyId, cancellationToken)
                     ?? throw new NotFoundException("Policy not found.");

        string yaml;
        if (!string.IsNullOrWhiteSpace(command.Yaml))
        {
            yaml = command.Yaml;
        }
        else
        {
            var latest = await _policies.GetLatestVersionAsync(command.OrganizationId, command.PolicyId, cancellationToken)
                         ?? throw new ConflictException("Policy has no YAML to test.");
            yaml = latest.Yaml;
        }

        var compiled = ParseYaml(policy.Slug, yaml);
        var attributes = command.Attributes ?? new Dictionary<string, JsonElement>();
        var context = command.Context ?? new Dictionary<string, JsonElement>();
        var envelope = new AuthorizationEnvelope(
            command.AgentId ?? "test-agent",
            command.Environment ?? "production",
            null,
            null,
            command.Action ?? compiled.Action,
            command.ResourceType ?? compiled.ResourceType,
            command.ResourceId ?? "test-resource",
            attributes,
            context);
        var evaluation = _engine.Evaluate(envelope, [compiled]);
        var actual = DecisionNames.ToApi(evaluation.Effect);
        var expected = string.IsNullOrWhiteSpace(command.ExpectedEffect)
            ? null
            : DecisionNames.ToApi(DecisionNames.Parse(command.ExpectedEffect));
        var passed = expected is null || string.Equals(expected, actual, StringComparison.Ordinal);
        return new PolicyTestResult(
            passed,
            actual,
            expected,
            evaluation.Reason,
            evaluation.PolicyId,
            evaluation.PolicyVersion,
            evaluation.MatchedRule);
    }

    public static bool IsAdministrationCatalog(string slug, string? action) =>
        slug.StartsWith("admin-", StringComparison.Ordinal)
        || (!string.IsNullOrEmpty(action) && ActionAdminHandler.IsAdministration(action));

    private async Task<bool> IsAdministrationPolicyAsync(PolicyDefinition policy, CancellationToken cancellationToken)
    {
        var application = await _applications.GetAsync(policy.OrganizationId, policy.ApplicationId, cancellationToken)
            ?? throw new NotFoundException("Application not found.");
        return application.Kind == ApplicationKinds.Administration
            && policy.Slug.StartsWith("admin-", StringComparison.Ordinal);
    }

    public async Task EnsureApplicationAccessAsync(string organizationId, string policyId, string selectedApplicationId, CancellationToken cancellationToken)
    {
        var policy = await _policies.GetAsync(organizationId, policyId, cancellationToken)
            ?? throw new NotFoundException("Policy not found.");
        if (policy.ApplicationId != selectedApplicationId && !await IsAdministrationPolicyAsync(policy, cancellationToken))
            throw new NotFoundException("Policy not found in the selected application.");
    }

    public async Task<IReadOnlyList<PolicySimulationCase>> ListCasesAsync(string organizationId, string policyId, CancellationToken cancellationToken)
    {
        var (policy, _) = await GetAsync(organizationId, policyId, cancellationToken);
        return await _simulations.ListAsync(organizationId, policy.ApplicationId, policy.Id, cancellationToken);
    }

    public Task SaveCaseAsync(string name, PolicyTestCommand command, CancellationToken cancellationToken) =>
        _policies.ExecuteLockedAsync(command.OrganizationId, command.PolicyId, async () =>
        {
            await SaveCaseLockedAsync(name, command, cancellationToken);
            return true;
        }, cancellationToken);

    private async Task SaveCaseLockedAsync(string name, PolicyTestCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160)
            throw new RequestValidationException("Case name is required and must be at most 160 characters.");
        if (command.ExpectedEffect is not ("ALLOW" or "DENY" or "REQUIRE_APPROVAL"))
            throw new RequestValidationException("Select an expected decision before saving a case.");
        var (policy, _) = await GetAsync(command.OrganizationId, command.PolicyId, cancellationToken);
        var yaml = command.Yaml ?? (await _policies.GetLatestVersionAsync(command.OrganizationId, command.PolicyId, cancellationToken))?.Yaml;
        var compiled = ParseYaml(policy.Slug, yaml ?? string.Empty);
        // Freeze the request match fields: changing candidate match must not change the fixture itself.
        var saved = command with { Yaml = null, Action = command.Action ?? compiled.Action, ResourceType = command.ResourceType ?? compiled.ResourceType };
        var json = JsonSerializer.Serialize(saved);
        if (json.Length > 65536) throw new RequestValidationException("Simulation case is too large (maximum 64 KiB).");
        await _simulations.AddAsync(new PolicySimulationCase
        {
            Id = IdGenerator.New("pcase"), OrganizationId = policy.OrganizationId, ApplicationId = policy.ApplicationId,
            PolicyId = policy.Id, Name = name.Trim(), RequestJson = json, ExpectedEffect = command.ExpectedEffect, CreatedAt = _clock.UtcNow
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public Task DeleteCaseAsync(string organizationId, string policyId, string caseId, CancellationToken cancellationToken) =>
        _policies.ExecuteLockedAsync(organizationId, policyId, async () =>
        {
            await DeleteCaseLockedAsync(organizationId, policyId, caseId, cancellationToken);
            return true;
        }, cancellationToken);

    private async Task DeleteCaseLockedAsync(string organizationId, string policyId, string caseId, CancellationToken cancellationToken)
    {
        var (policy, _) = await GetAsync(organizationId, policyId, cancellationToken);
        await _simulations.DeleteAsync(organizationId, policy.ApplicationId, policyId, caseId, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PolicyCaseComparison>> CompareCasesAsync(string organizationId, string policyId, string yaml, CancellationToken cancellationToken)
    {
        var (policy, versions) = await GetAsync(organizationId, policyId, cancellationToken);
        ParseYaml(policy.Slug, yaml);
        var published = versions.FirstOrDefault(x => x.Id == policy.PublishedVersionId);
        var cases = await _simulations.ListAsync(organizationId, policy.ApplicationId, policyId, cancellationToken);
        var results = new List<PolicyCaseComparison>();
        foreach (var fixture in cases)
        {
            var request = JsonSerializer.Deserialize<PolicyTestCommand>(fixture.RequestJson)
                ?? throw new RequestValidationException("Saved simulation case is invalid.");
            request = request with { OrganizationId = organizationId, PolicyId = policyId, ExpectedEffect = fixture.ExpectedEffect, Yaml = yaml };
            var candidate = await TestAsync(request, cancellationToken);
            var baseline = published is null ? null : await TestAsync(request with { Yaml = published.Yaml }, cancellationToken);
            results.Add(new PolicyCaseComparison(fixture.Id, fixture.Name, candidate, baseline));
        }
        return results;
    }

    public Task DeleteAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
        _policies.ExecuteLockedAsync(organizationId, policyId, async () =>
        {
            await DeleteLockedAsync(organizationId, policyId, cancellationToken);
            return true;
        }, cancellationToken);

    private async Task DeleteLockedAsync(string organizationId, string policyId, CancellationToken cancellationToken)
    {
        await EnsureCanDeleteAsync(organizationId, policyId, cancellationToken);
        await _grants.DeleteByPolicyIdAsync(organizationId, policyId, cancellationToken);
        await _policies.DeleteAsync(organizationId, policyId, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureCanDeleteAsync(string organizationId, string policyId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(policyId))
        {
            throw new RequestValidationException("policy_id is required.");
        }

        var policy = await _policies.GetAsync(organizationId, policyId, cancellationToken)
                     ?? throw new NotFoundException("Policy not found.");
        var versions = await _policies.ListVersionsAsync(organizationId, policyId, cancellationToken);
        var current = versions.FirstOrDefault(v => v.Id == policy.PublishedVersionId)
                      ?? versions.OrderByDescending(v => v.Version).FirstOrDefault();
        if (IsAdministrationCatalog(policy.Slug, current?.Action))
        {
            throw new RequestValidationException("Administration catalog policies cannot be deleted.");
        }
    }

    public Task ReplaceAgentsAsync(string organizationId, string policyId, IReadOnlyList<string> agentIds, CancellationToken cancellationToken) =>
        _policies.ExecuteLockedAsync(organizationId, policyId, async () =>
        {
            await ReplaceAgentsLockedAsync(organizationId, policyId, agentIds, cancellationToken);
            return true;
        }, cancellationToken);

    private async Task ReplaceAgentsLockedAsync(
        string organizationId,
        string policyId,
        IReadOnlyList<string> agentIds,
        CancellationToken cancellationToken)
    {
        var (policy, versions) = await GetAsync(organizationId, policyId, cancellationToken);
        var current = versions.FirstOrDefault(v => v.Id == policy.PublishedVersionId)
                      ?? versions.OrderByDescending(v => v.Version).FirstOrDefault();
        if (IsAdministrationCatalog(policy.Slug, current?.Action))
        {
            throw new RequestValidationException("Administration catalog policies cannot be changed.");
        }

        var normalized = await NormalizeAgentIdsAsync(
            organizationId,
            policy.ApplicationId,
            current?.Action,
            agentIds,
            cancellationToken);
        await _grants.ReplaceAsync(organizationId, policy.ApplicationId, policy.Id, normalized, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public static string CasesFingerprint(IReadOnlyList<PolicySimulationCase> cases) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cases.OrderBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => new { c.Id, c.Name, c.ExpectedEffect, c.RequestJson })))));

    private async Task<IReadOnlyList<string>> NormalizeAgentIdsAsync(
        string organizationId,
        string applicationId,
        string? actionName,
        IReadOnlyList<string>? agentIds,
        CancellationToken cancellationToken)
    {
        var requested = (agentIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (requested.Count == 0)
        {
            throw new RequestValidationException("Assign at least one agent to this policy.");
        }

        var agents = await _agents.ListAsync(organizationId, applicationId, cancellationToken);
        var allowed = agents.Select(agent => agent.Id).ToHashSet(StringComparer.Ordinal);
        if (requested.Any(id => !allowed.Contains(id)))
        {
            throw new RequestValidationException("Agents must belong to the same application as the policy.");
        }

        if (!string.IsNullOrWhiteSpace(actionName))
        {
            var action = await _actions.GetByNameAsync(organizationId, applicationId, actionName, cancellationToken);
            if (action is not null)
            {
                var actionAgents = await _actionGrants.ListAgentIdsForActionAsync(organizationId, action.Id, cancellationToken);
                var actionSet = actionAgents.ToHashSet(StringComparer.Ordinal);
                if (requested.Any(id => !actionSet.Contains(id)))
                {
                    throw new RequestValidationException("Policy agents must be a subset of the action's agents.");
                }
            }
        }

        return requested;
    }

    private static PolicyVersion NewVersion(
        string organizationId,
        string policyId,
        CompiledPolicy compiled,
        string yaml,
        DateTimeOffset now) =>
        new()
        {
            Id = IdGenerator.New("pver"),
            OrganizationId = organizationId,
            PolicyId = policyId,
            Version = compiled.Version,
            Yaml = yaml,
            Action = compiled.Action,
            ResourceType = compiled.ResourceType,
            Priority = compiled.Priority,
            Published = false,
            CreatedAt = now
        };

    private static CompiledPolicy ParseYaml(string policyId, string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new RequestValidationException("YAML is required.");
        }

        try
        {
            return PolicyYamlParser.Parse(policyId, yaml);
        }
        catch (Exception ex) when (ex is not FailClosedException and not RequestValidationException)
        {
            throw new RequestValidationException($"Invalid policy YAML: {ex.Message}");
        }
    }
}
