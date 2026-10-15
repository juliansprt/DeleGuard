using System.Text.Json;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Application.Evidence;
using Deleguard.Application.Playground;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Policies;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class PlaygroundHandlerTests
{
    [Fact]
    public async Task Catalog_omits_admin_mcp_disabled_agents_and_admin_actions()
    {
        var handler = Handler();
        var catalog = await handler.LoadCatalogAsync("org_demo", "app_demo", CancellationToken.None);

        Assert.Equal(["invoice-agent"], catalog.Agents.Select(agent => agent.ExternalId));
        Assert.Equal(["email.send", "invoice.cancel"], catalog.Actions.Select(action => action.Name));
        Assert.Equal(["invoice-cancel-production"], catalog.Policies.Select(policy => policy.Slug));
        Assert.True(catalog.Scenarios.TryGetValue("invoice.cancel", out var invoice));
        Assert.Equal(["ALLOW", "REQUIRE_APPROVAL", "DENY"], invoice.Select(scenario => scenario.Effect));
        Assert.False(catalog.Scenarios.ContainsKey("email.send"));
    }

    [Fact]
    public async Task Authorize_rejects_invalid_key()
    {
        var handler = Handler();
        var error = await Assert.ThrowsAsync<AgentAuthenticationException>(() =>
            handler.AuthorizeAsync("org_demo", "agt_invoice", "dgk_nope", "invoice.cancel", "invoice", "FAC-1", null, CancellationToken.None));
        Assert.Equal("Invalid agent API key.", error.Message);
    }

    [Fact]
    public async Task Authorize_rejects_other_tenant_key()
    {
        var handler = Handler();
        var error = await Assert.ThrowsAsync<AgentAuthenticationException>(() =>
            handler.AuthorizeAsync("org_demo", "agt_invoice", "dgk_other", "invoice.cancel", "invoice", "FAC-1", null, CancellationToken.None));
        Assert.Equal("Invalid agent API key.", error.Message);
    }

    [Fact]
    public async Task Authorize_rejects_admin_mcp_key()
    {
        var handler = Handler();
        var error = await Assert.ThrowsAsync<AgentAuthenticationException>(() =>
            handler.AuthorizeAsync("org_demo", "agt_admin_mcp", "dgk_admin", "invoice.cancel", "invoice", "FAC-1", null, CancellationToken.None));
        Assert.Equal("Invalid agent API key.", error.Message);
    }

    [Fact]
    public async Task Authorize_rejects_administration_action()
    {
        var handler = Handler();
        var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.AuthorizeAsync("org_demo", "agt_invoice", "dgk_ok", "admin.policy.publish", "policy", "pol_1", null, CancellationToken.None));
        Assert.Equal("Playground cannot authorize administration actions.", error.Message);
    }

    [Fact]
    public async Task Authorize_without_key_binds_selected_agent()
    {
        var authorize = new RecordingAuthorize();
        var agents = new FakeAgents();
        var handler = Handler(agents: agents, authorize: authorize.HandleAsync);
        var result = await handler.AuthorizeAsync(
            "org_demo",
            "agt_invoice",
            "",
            "invoice.cancel",
            "invoice",
            "FAC-DRAFT",
            null,
            CancellationToken.None);

        Assert.Equal("ALLOW", result.Decision.Decision);
        Assert.Equal("invoice-agent", result.AgentExternalId);
        Assert.Equal("agt_invoice", agents.LastBoundAgentId);
        Assert.NotNull(authorize.Last);
        Assert.Equal("agt_invoice", authorize.Last!.Principal.AgentId);
        Assert.Equal("invoice-agent", authorize.Last.Principal.AgentExternalId);
        Assert.Equal("playground", authorize.Last.AuditSource);
    }

    [Fact]
    public async Task Authorize_without_key_requires_agent()
    {
        var handler = Handler();
        var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.AuthorizeAsync("org_demo", "", "", "invoice.cancel", "invoice", "FAC-1", null, CancellationToken.None));
        Assert.Equal("Select an agent.", error.Message);
    }

    [Fact]
    public async Task Authorize_without_key_rejects_unknown_disabled_and_admin_mcp_agents()
    {
        var handler = Handler();
        foreach (var agentId in new[] { "agt_missing", "agt_off", "agt_admin_mcp" })
        {
            var error = await Assert.ThrowsAsync<NotFoundException>(() =>
                handler.AuthorizeAsync("org_demo", agentId, "", "invoice.cancel", "invoice", "FAC-1", null, CancellationToken.None));
            Assert.Equal("Agent not found or disabled.", error.Message);
        }
    }

    [Fact]
    public async Task Authorize_pasted_key_does_not_bind_by_agent_id()
    {
        var agents = new FakeAgents();
        var handler = Handler(agents: agents);
        await handler.AuthorizeAsync("org_demo", "agt_invoice", "dgk_ok", "invoice.cancel", "invoice", "FAC-1", null, CancellationToken.None);
        Assert.Null(agents.LastBoundAgentId);
    }

    [Fact]
    public async Task Authorize_uses_authenticated_agent_not_console_and_marks_playground_source()
    {
        var authorize = new RecordingAuthorize();
        var handler = Handler(authorize: authorize.HandleAsync);
        var result = await handler.AuthorizeAsync(
            "org_demo",
            "agt_invoice",
            "dgk_ok",
            "invoice.cancel",
            "invoice",
            "FAC-DRAFT",
            new Dictionary<string, JsonElement> { ["status"] = JsonSerializer.SerializeToElement("draft") },
            CancellationToken.None);

        Assert.Equal("ALLOW", result.Decision.Decision);
        Assert.Equal("invoice-agent", result.AgentExternalId);
        Assert.Equal("production", result.EnvironmentName);
        Assert.False(result.EvidenceReported);
        Assert.NotNull(authorize.Last);
        Assert.Equal("invoice-agent", authorize.Last!.Principal.AgentExternalId);
        Assert.Equal("agt_invoice", authorize.Last.Principal.AgentId);
        Assert.Equal("invoice-agent", authorize.Last.RequestedAgentId);
        Assert.Equal("production", authorize.Last.RequestedEnvironment);
        Assert.Equal("service", authorize.Last.ActorType);
        Assert.Equal("invoice.cancel", authorize.Last.Action);
        Assert.Equal("FAC-DRAFT", authorize.Last.ResourceId);
        Assert.Null(authorize.Last.Context);
        Assert.Equal("playground", authorize.Last.AuditSource);
        Assert.StartsWith("playground-", authorize.Last.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_execution_requires_allow_and_permit()
    {
        var evidence = new RecordingEvidence();
        var handler = Handler(
            getDecision: (_, _, _) => Task.FromResult(new DecisionDto(
                "REQUIRE_APPROVAL", "dec_wait", "needs a human", "pol_1", 1, null, "apr_1", null)),
            reportExecution: evidence.HandleAsync);

        var result = await handler.ReportExecutionAsync("org_demo", "agt_invoice", "dgk_ok", "dec_wait", null, CancellationToken.None);
        Assert.Equal("REQUIRE_APPROVAL", result.Decision.Decision);
        Assert.False(result.EvidenceReported);
        Assert.Null(evidence.Last);
    }

    [Fact]
    public async Task Report_execution_records_evidence_for_permitted_allow()
    {
        var evidence = new RecordingEvidence();
        var handler = Handler(
            getDecision: (_, _, _) => Task.FromResult(new DecisionDto(
                "ALLOW", "dec_ok", "ok", "pol_1", 1, "permit-token", null, DateTimeOffset.UtcNow.AddMinutes(5))),
            reportExecution: evidence.HandleAsync);

        var attributes = new Dictionary<string, JsonElement> { ["status"] = JsonSerializer.SerializeToElement("draft") };
        var result = await handler.ReportExecutionAsync("org_demo", "agt_invoice", "dgk_ok", "dec_ok", attributes, CancellationToken.None);
        Assert.True(result.EvidenceReported);
        Assert.NotNull(evidence.Last);
        Assert.Equal("dec_ok", evidence.Last!.DecisionId);
        Assert.Equal("SUCCESS", evidence.Last.Status);
        Assert.Equal("agt_invoice", evidence.Last.Principal.AgentId);
        Assert.Equal("playground", evidence.Last.Metadata!["source"].GetString());
        Assert.Equal("playground", evidence.Last.AuditSource);
        Assert.Equal(CanonicalJson.HashHex(JsonSerializer.Serialize(attributes)), evidence.Last.ResourceSnapshotHash);
    }

    [Fact]
    public async Task Report_execution_without_key_binds_selected_agent()
    {
        var evidence = new RecordingEvidence();
        var handler = Handler(
            getDecision: (_, _, _) => Task.FromResult(new DecisionDto(
                "ALLOW", "dec_ok", "ok", "pol_1", 1, "permit-token", null, DateTimeOffset.UtcNow.AddMinutes(5))),
            reportExecution: evidence.HandleAsync);

        var result = await handler.ReportExecutionAsync("org_demo", "agt_invoice", "", "dec_ok", null, CancellationToken.None);
        Assert.True(result.EvidenceReported);
        Assert.Equal("agt_invoice", evidence.Last!.Principal.AgentId);
    }

    private static PlaygroundHandler Handler(
        FakeAgents? agents = null,
        Func<AuthorizeCommand, CancellationToken, Task<DecisionDto>>? authorize = null,
        Func<AgentPrincipal, string, CancellationToken, Task<DecisionDto>>? getDecision = null,
        Func<ReportExecutionCommand, CancellationToken, Task>? reportExecution = null) =>
        new(
            agents ?? new FakeAgents(),
            new FakeActions(),
            new FakePolicies(),
            authorize ?? ((_, _) => Task.FromResult(new DecisionDto(
                "ALLOW", "dec_1", "ok", "pol_1", 1, "permit", null, null))),
            getDecision ?? ((_, _, _) => Task.FromResult(new DecisionDto(
                "DENY", "dec_none", "missing", null, null, null, null, null))),
            reportExecution ?? ((_, _) => Task.CompletedTask));

    private sealed class RecordingAuthorize
    {
        public AuthorizeCommand? Last { get; private set; }

        public Task<DecisionDto> HandleAsync(AuthorizeCommand command, CancellationToken cancellationToken)
        {
            Last = command;
            return Task.FromResult(new DecisionDto("ALLOW", "dec_1", "ok", "pol_1", 1, "permit", null, null));
        }
    }

    private sealed class RecordingEvidence
    {
        public ReportExecutionCommand? Last { get; private set; }

        public Task HandleAsync(ReportExecutionCommand command, CancellationToken cancellationToken)
        {
            Last = command;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAgents : IAgentDirectory
    {
        private static readonly AgentPrincipal Invoice = new("org_demo", "app_demo", "env_demo", "production", "agt_invoice", "invoice-agent");
        private static readonly AgentPrincipal Other = new("org_other", "app_x", "env_x", "production", "agt_x", "other-agent");
        private static readonly AgentPrincipal AdminMcp = new("org_demo", "app_demo_admin", "env_demo", "production", "agt_admin_mcp", AccessControlIds.AdminMcpExternalId);

        public string? LastBoundAgentId { get; private set; }

        public Task<AgentPrincipal?> AuthenticateAsync(string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult<AgentPrincipal?>(apiKey switch
            {
                "dgk_ok" => Invoice,
                "dgk_other" => Other,
                "dgk_admin" => AdminMcp,
                _ => null
            });

        public Task<Agent?> GetAsync(string organizationId, string agentId, CancellationToken cancellationToken) =>
            Task.FromResult<Agent?>(null);

        public Task<AgentPrincipal?> GetPrincipalAsync(string organizationId, string agentId, CancellationToken cancellationToken)
        {
            LastBoundAgentId = agentId;
            if (!string.Equals(organizationId, "org_demo", StringComparison.Ordinal))
            {
                return Task.FromResult<AgentPrincipal?>(null);
            }

            return Task.FromResult<AgentPrincipal?>(agentId switch
            {
                "agt_invoice" => Invoice,
                "agt_admin_mcp" => AdminMcp,
                _ => null
            });
        }

        public Task<AgentPrincipal?> GetPrincipalByExternalIdAsync(string organizationId, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentPrincipal?>(null);

        public Task<IReadOnlyList<AgentListItem>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentListItem>>([
                new("agt_invoice", "invoice-agent", "Invoice Agent", "app_demo", "env_demo", "production", true, "dgk_demo_inv"),
                new("agt_off", "disabled-agent", "Off", "app_demo", "env_demo", "production", false, "dgk_off"),
                new("agt_admin_mcp", AccessControlIds.AdminMcpExternalId, "Admin MCP", "app_demo_admin", "env_demo", "production", true, "dgk_admin")
            ]);

        public Task<IReadOnlyList<EnvironmentInfo>> ListEnvironmentsAsync(string organizationId, string applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EnvironmentInfo>>([]);

        public Task<CreatedAgentSecret> CreateAsync(string organizationId, string applicationId, string externalId, string displayName, string environmentName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<T> ExecuteLockedAsync<T>(string organizationId, string agentId, Func<Task<T>> action, CancellationToken cancellationToken) => action();

        public Task<CreatedAgentSecret> RotateKeyAsync(string organizationId, string agentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetEnabledAsync(string organizationId, string agentId, bool enabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeActions : IActionRegistry
    {
        public Task<ActionDefinition?> GetByNameAsync(string organizationId, string applicationId, string name, CancellationToken cancellationToken) =>
            Task.FromResult<ActionDefinition?>(null);

        public Task<ActionDefinition?> GetAsync(string organizationId, string actionId, CancellationToken cancellationToken) =>
            Task.FromResult<ActionDefinition?>(null);

        public Task<IReadOnlyList<ActionDefinition>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActionDefinition>>([
                new()
                {
                    Id = "act_invoice",
                    OrganizationId = organizationId,
                    ApplicationId = applicationId ?? "app_demo",
                    Name = "invoice.cancel",
                    ResourceType = "invoice",
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new()
                {
                    Id = "act_email",
                    OrganizationId = organizationId,
                    ApplicationId = applicationId ?? "app_demo",
                    Name = "email.send",
                    ResourceType = "email",
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new()
                {
                    Id = "act_admin",
                    OrganizationId = organizationId,
                    ApplicationId = "app_demo_admin",
                    Name = "admin.policy.publish",
                    ResourceType = "policy",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]);

        public Task AddAsync(ActionDefinition action, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(string organizationId, string actionId, string resourceType, RiskLevel risk, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string organizationId, string actionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakePolicies : IPolicyRepository
    {
        public Task<T> ExecuteLockedAsync<T>(string organizationId, string policyId, Func<Task<T>> action, CancellationToken cancellationToken) => action();
        public Task<IReadOnlyList<CompiledPolicy>> GetPublishedAsync(string organizationId, string applicationId, string action, string resourceType, string agentId, CancellationToken cancellationToken)
        {
            if (string.Equals(action, "invoice.cancel", StringComparison.Ordinal)
                && string.Equals(resourceType, "invoice", StringComparison.Ordinal))
            {
                var yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "policies", "invoice-cancel-production.yaml"));
                return Task.FromResult<IReadOnlyList<CompiledPolicy>>([PolicyYamlParser.Parse("invoice-cancel-production", yaml)]);
            }

            return Task.FromResult<IReadOnlyList<CompiledPolicy>>([]);
        }

        public Task<IReadOnlyList<PolicySummary>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PolicySummary>>([
                new("pol_invoice", "invoice-cancel-production", "app_demo", "ver_1", 1, 1, "invoice.cancel", "invoice"),
                new("pol_draft", "email-draft", "app_demo", null, 1, null, "email.send", "email"),
                new("pol_admin", "admin-users", "app_demo_admin", "ver_a", 1, 1, "admin.user.create", "user")
            ]);

        public Task<PolicyDefinition?> GetAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
            Task.FromResult<PolicyDefinition?>(null);

        public Task<IReadOnlyList<PolicyVersion>> ListVersionsAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PolicyVersion>>([]);

        public Task<PolicyVersion?> GetVersionAsync(string organizationId, string policyId, string versionId, CancellationToken cancellationToken) =>
            Task.FromResult<PolicyVersion?>(null);

        public Task<PolicyVersion?> GetLatestVersionAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
            Task.FromResult<PolicyVersion?>(null);

        public Task AddDefinitionAsync(PolicyDefinition policy, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddVersionAsync(PolicyVersion version, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<PolicyVersion>> GetVersionsForUpdateAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PolicyVersion>>([]);

        public Task DeleteAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
