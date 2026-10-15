using System.Text.Json;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Approvals;
using Deleguard.Domain.Audit;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Evidence;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Policies;
using Deleguard.Policy;

namespace Deleguard.UnitTests;

public sealed class AuthorizeGrantTests
{
    [Fact]
    public async Task Unknown_action_in_this_application_is_deny()
    {
        var result = await Handler().HandleAsync(Command("missing.action"), CancellationToken.None);
        Assert.Equal("DENY", result.Decision);
        Assert.Equal("Unknown action.", result.Reason);
    }

    [Fact]
    public async Task Homonymous_action_in_another_application_is_unknown()
    {
        var actions = new Actions();
        actions.Add("app_b", "invoice.cancel");
        var result = await Handler(actions).HandleAsync(Command("invoice.cancel"), CancellationToken.None);
        Assert.Equal("DENY", result.Decision);
        Assert.Equal("Unknown action.", result.Reason);
    }

    [Fact]
    public async Task Missing_action_grant_is_deny()
    {
        var actions = new Actions();
        actions.Add("app_demo", "email.send");
        var result = await Handler(actions).HandleAsync(Command("email.send"), CancellationToken.None);
        Assert.Equal("DENY", result.Decision);
        Assert.Equal("Agent is not allowed to request this action.", result.Reason);
    }

    [Fact]
    public async Task Product_agent_cannot_call_administration_actions()
    {
        var actions = new Actions();
        actions.Add("app_demo", "admin.user.list");
        var grants = new Grants();
        grants.Allow("agt_invoice", "act_app_demo_admin.user.list");
        var result = await Handler(actions, grants).HandleAsync(Command("admin.user.list"), CancellationToken.None);
        Assert.Equal("DENY", result.Decision);
        Assert.Contains("admin-mcp", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Granted_action_with_no_published_policy_is_deny()
    {
        var actions = new Actions();
        actions.Add("app_demo", "email.send");
        var grants = new Grants();
        grants.Allow("agt_invoice", "act_app_demo_email.send");
        var result = await Handler(actions, grants).HandleAsync(Command("email.send"), CancellationToken.None);
        Assert.Equal("DENY", result.Decision);
    }

    [Fact]
    public async Task Registry_failure_is_fail_closed()
    {
        await Assert.ThrowsAsync<FailClosedException>(() =>
            Handler(new Actions { Throw = true }).HandleAsync(Command("invoice.cancel"), CancellationToken.None));
    }

    private static AuthorizeHandler Handler(Actions? actions = null, Grants? grants = null) =>
        new(
            actions ?? new Actions(),
            grants ?? new Grants(),
            new Policies(),
            new NativePolicyEngine(),
            new Decisions(),
            new Idempotency(),
            new Approvals(),
            new Permits(),
            new Signer(),
            new Audit(),
            new Outbox(),
            new Actors(),
            new Unit(),
            new Clock());

    private static AuthorizeCommand Command(string action) =>
        new(
            new AgentPrincipal("org_demo", "app_demo", "env_demo", "production", "agt_invoice", "invoice-agent"),
            "invoice-agent",
            "production",
            null,
            null,
            null,
            action,
            "invoice",
            "FAC-1",
            null,
            null,
            null,
            Guid.NewGuid().ToString("N"));

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-09-16T18:00:00Z");
    }

    private sealed class Unit : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken) => action();
    }

    private sealed class Actions : IActionRegistry
    {
        public bool Throw { get; init; }
        private readonly List<ActionDefinition> _items = [];

        public void Add(string applicationId, string name) =>
            _items.Add(new ActionDefinition
            {
                Id = $"act_{applicationId}_{name}",
                OrganizationId = "org_demo",
                ApplicationId = applicationId,
                Name = name,
                ResourceType = "invoice",
                Risk = RiskLevel.High,
                CreatedAt = DateTimeOffset.UtcNow
            });

        public Task<ActionDefinition?> GetByNameAsync(string organizationId, string applicationId, string name, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("db down");
            }

            return Task.FromResult(_items.FirstOrDefault(x =>
                x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Name == name));
        }

        public Task<ActionDefinition?> GetAsync(string organizationId, string actionId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.FirstOrDefault(x => x.Id == actionId));

        public Task<IReadOnlyList<ActionDefinition>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActionDefinition>>(_items);

        public Task AddAsync(ActionDefinition action, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateAsync(string organizationId, string actionId, string resourceType, RiskLevel risk, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(string organizationId, string actionId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Grants : IActionGrantStore
    {
        private readonly HashSet<(string Agent, string Action)> _allowed = [];

        public void Allow(string agentId, string actionId) => _allowed.Add((agentId, actionId));

        public Task<bool> HasAsync(string organizationId, string agentId, string actionId, CancellationToken cancellationToken) =>
            Task.FromResult(_allowed.Contains((agentId, actionId)));

        public Task<IReadOnlyList<string>> ListAgentIdsForActionAsync(string organizationId, string actionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ListAgentIdsByActionIdsAsync(
            string organizationId,
            IReadOnlyList<string> actionIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(new Dictionary<string, IReadOnlyList<string>>());

        public Task ReplaceAsync(string organizationId, string applicationId, string actionId, IReadOnlyList<string> agentIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteByActionIdAsync(string organizationId, string actionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class Policies : IPolicyRepository
    {
        public Task<T> ExecuteLockedAsync<T>(string organizationId, string policyId, Func<Task<T>> action, CancellationToken cancellationToken) => action();
        public Task<IReadOnlyList<CompiledPolicy>> GetPublishedAsync(
            string organizationId,
            string applicationId,
            string action,
            string resourceType,
            string agentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompiledPolicy>>([]);

        public Task<IReadOnlyList<PolicySummary>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PolicySummary>>([]);

        public Task<PolicyDefinition?> GetAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
            Task.FromResult<PolicyDefinition?>(null);

        public Task<IReadOnlyList<PolicyVersion>> ListVersionsAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PolicyVersion>>([]);

        public Task<PolicyVersion?> GetVersionAsync(string organizationId, string policyId, string versionId, CancellationToken cancellationToken) =>
            Task.FromResult<PolicyVersion?>(null);

        public Task<PolicyVersion?> GetLatestVersionAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
            Task.FromResult<PolicyVersion?>(null);

        public Task AddDefinitionAsync(PolicyDefinition policy, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddVersionAsync(PolicyVersion version, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<PolicyVersion>> GetVersionsForUpdateAsync(string organizationId, string policyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PolicyVersion>>([]);

        public Task DeleteAsync(string organizationId, string policyId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Decisions : IDecisionStore
    {
        public Task<AuthorizationDecision?> GetAsync(string organizationId, string decisionId, CancellationToken cancellationToken) =>
            Task.FromResult<AuthorizationDecision?>(null);

        public Task AddAsync(AuthorizationDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Idempotency : IIdempotencyStore
    {
        public Task<IdempotencyRecord?> GetAsync(string organizationId, string applicationId, string key, CancellationToken cancellationToken) =>
            Task.FromResult<IdempotencyRecord?>(null);

        public Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Approvals : IApprovalStore
    {
        public Task AddAsync(ApprovalRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ApprovalRequest?> GetAsync(string organizationId, string approvalId, CancellationToken cancellationToken) =>
            Task.FromResult<ApprovalRequest?>(null);

        public Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(string organizationId, string? applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApprovalRequest>>([]);

        public Task AddEventAsync(ApprovalEvent approvalEvent, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ApprovalRequest>> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApprovalRequest>>([]);
    }

    private sealed class Permits : IPermitStore
    {
        public Task AddAsync(Permit permit, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Permit?> GetAsync(string organizationId, string permitId, CancellationToken cancellationToken) =>
            Task.FromResult<Permit?>(null);

        public Task<Permit?> GetByDecisionAsync(string organizationId, string decisionId, CancellationToken cancellationToken) =>
            Task.FromResult<Permit?>(null);
    }

    private sealed class Signer : IPermitSigner
    {
        public string Sign(Permit permit, AgentPrincipal agent, string? actorId) => "signed";
    }

    private sealed class Audit : IAuditLedger
    {
        public Task AppendAsync(string organizationId, string eventType, string? decisionId, JsonElement payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AuditEvent>> ListAsync(
            string organizationId,
            int take,
            CancellationToken cancellationToken,
            string? eventType = null,
            string? q = null,
            string? applicationId = null) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    private sealed class Outbox : IOutbox
    {
        public Task EnqueueAsync(string organizationId, string eventType, JsonElement payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<OutboxEvent>> DequeueUnprocessedAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OutboxEvent>>([]);

        public Task MarkProcessedAsync(string id, DateTimeOffset processedAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class Actors : IActorDirectory
    {
        public Task<IReadOnlyList<string>> GetRolesAsync(string organizationId, string? actorId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
