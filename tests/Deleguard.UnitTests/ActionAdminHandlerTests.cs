using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;

namespace Deleguard.UnitTests;

public sealed class ActionAdminHandlerTests
{
    [Fact]
    public async Task Rejects_uppercase_name_with_pattern_hint()
    {
        var handler = Create();
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.CreateAsync(Command("Invoice.Cancel"), CancellationToken.None));
        Assert.Contains("lowercase", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_uppercase_resource_type()
    {
        var handler = Create();
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.CreateAsync(Command("invoice.cancel", "Invoice"), CancellationToken.None));
        Assert.Contains("resource_type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Creates_dotted_lowercase_name()
    {
        var store = new Store();
        var handler = Create(store);
        var created = await handler.CreateAsync(Command("email.send"), CancellationToken.None);
        Assert.Equal("email.send", created.Name);
        Assert.Equal("email", created.ResourceType);
        Assert.Equal(RiskLevel.High, created.Risk);
        Assert.Equal("app_default", created.ApplicationId);
        Assert.False(ActionAdminHandler.IsAdministration(created.Name));
    }

    [Fact]
    public void Classifies_administration_names()
    {
        Assert.True(ActionAdminHandler.IsAdministration("admin.action.create"));
        Assert.False(ActionAdminHandler.IsAdministration("invoice.cancel"));
    }

    [Fact]
    public async Task Conflict_when_name_exists_in_same_application()
    {
        var store = new Store();
        var handler = Create(store);
        await handler.CreateAsync(Command("email.send"), CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.CreateAsync(Command("email.send"), CancellationToken.None));
    }

    [Fact]
    public async Task Same_name_is_allowed_in_another_application()
    {
        var store = new Store();
        var apps = new Applications();
        var agents = new Agents();
        apps.Add("app_b", ApplicationKinds.Product);
        agents.Add("agt_b", "app_b");
        var handler = Create(store, apps: apps, agents: agents);
        await handler.CreateAsync(Command("email.send"), CancellationToken.None);
        var other = await handler.CreateAsync(Command("email.send", applicationId: "app_b", agentIds: ["agt_b"]), CancellationToken.None);
        Assert.Equal("app_b", other.ApplicationId);
        Assert.Equal(2, (await store.ListAsync("org", null, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Rejects_create_without_agents()
    {
        var handler = Create();
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.CreateAsync(Command("email.send", agentIds: []), CancellationToken.None));
        Assert.Contains("at least one agent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_admin_action_in_product_application()
    {
        var handler = Create();
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.CreateAsync(Command("admin.action.create"), CancellationToken.None));
        Assert.Contains("Administration actions", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Updates_resource_type_and_risk_but_not_name()
    {
        var store = new Store();
        var handler = Create(store);
        var created = await handler.CreateAsync(Command("email.send"), CancellationToken.None);
        var updated = await handler.UpdateAsync(new UpdateActionCommand("org", created.Id, "mailbox", "LOW", ["agt_1"]), CancellationToken.None);
        Assert.Equal("email.send", updated.Name);
        Assert.Equal("mailbox", updated.ResourceType);
        Assert.Equal(RiskLevel.Low, updated.Risk);
    }

    [Fact]
    public async Task Rejects_update_and_delete_of_administration_actions()
    {
        var store = new Store();
        store.Seed(new ActionDefinition
        {
            Id = "act_admin",
            OrganizationId = "org",
            ApplicationId = "app_admin",
            Name = "admin.action.create",
            ResourceType = "action",
            Risk = RiskLevel.Medium,
            CreatedAt = DateTimeOffset.Parse("2026-09-13T18:00:00Z")
        });
        var handler = Create(store);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.UpdateAsync(new UpdateActionCommand("org", "act_admin", "action", "LOW", ["agt_1"]), CancellationToken.None));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.DeleteAsync("org", "act_admin", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_action_and_mcp_bindings()
    {
        var store = new Store();
        var bindings = new Bindings();
        var grants = new Grants();
        var handler = Create(store, bindings, grants);
        var created = await handler.CreateAsync(Command("email.send"), CancellationToken.None);
        bindings.BoundActionIds.Add(created.Id);
        await handler.DeleteAsync("org", created.Id, CancellationToken.None);
        Assert.Empty(await store.ListAsync("org", null, CancellationToken.None));
        Assert.Empty(bindings.BoundActionIds);
        Assert.False(await grants.HasAsync("org", "agt_1", created.Id, CancellationToken.None));
    }

    private static CreateActionCommand Command(
        string name,
        string resourceType = "email",
        string risk = "HIGH",
        string applicationId = "app_default",
        IReadOnlyList<string>? agentIds = null) =>
        new("org", applicationId, name, resourceType, risk, agentIds ?? ["agt_1"]);

    private static ActionAdminHandler Create(
        Store? store = null,
        Bindings? bindings = null,
        Grants? grants = null,
        Applications? apps = null,
        Agents? agents = null) =>
        new(
            store ?? new Store(),
            grants ?? new Grants(),
            agents ?? new Agents(),
            apps ?? new Applications(),
            bindings ?? new Bindings(),
            new Clock(),
            new Unit());

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-09-13T18:00:00Z");
    }

    private sealed class Unit : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken) => action();
    }

    private sealed class Store : IActionRegistry
    {
        private readonly List<ActionDefinition> _items = [];

        public void Seed(ActionDefinition action) => _items.Add(action);

        public Task<ActionDefinition?> GetByNameAsync(string organizationId, string applicationId, string name, CancellationToken cancellationToken) =>
            Task.FromResult(_items.FirstOrDefault(x =>
                x.OrganizationId == organizationId && x.ApplicationId == applicationId && x.Name == name));

        public Task<ActionDefinition?> GetAsync(string organizationId, string actionId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.FirstOrDefault(x => x.OrganizationId == organizationId && x.Id == actionId));

        public Task<IReadOnlyList<ActionDefinition>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActionDefinition>>(_items
                .Where(x => x.OrganizationId == organizationId && (applicationId is null || x.ApplicationId == applicationId))
                .ToList());

        public Task AddAsync(ActionDefinition action, CancellationToken cancellationToken)
        {
            _items.Add(action);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(string organizationId, string actionId, string resourceType, RiskLevel risk, CancellationToken cancellationToken)
        {
            var item = _items.First(x => x.OrganizationId == organizationId && x.Id == actionId);
            item.ResourceType = resourceType;
            item.Risk = risk;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string organizationId, string actionId, CancellationToken cancellationToken)
        {
            _items.RemoveAll(x => x.OrganizationId == organizationId && x.Id == actionId);
            return Task.CompletedTask;
        }
    }

    private sealed class Grants : IActionGrantStore
    {
        private readonly HashSet<(string Org, string Agent, string Action)> _items = [];

        public Task<bool> HasAsync(string organizationId, string agentId, string actionId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.Contains((organizationId, agentId, actionId)));

        public Task<IReadOnlyList<string>> ListAgentIdsForActionAsync(string organizationId, string actionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(_items.Where(x => x.Org == organizationId && x.Action == actionId).Select(x => x.Agent).ToList());

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ListAgentIdsByActionIdsAsync(
            string organizationId,
            IReadOnlyList<string> actionIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                actionIds.ToDictionary(id => id, id => (IReadOnlyList<string>)_items.Where(x => x.Action == id).Select(x => x.Agent).ToList()));

        public Task ReplaceAsync(string organizationId, string applicationId, string actionId, IReadOnlyList<string> agentIds, CancellationToken cancellationToken)
        {
            _items.RemoveWhere(x => x.Org == organizationId && x.Action == actionId);
            foreach (var agentId in agentIds)
            {
                _items.Add((organizationId, agentId, actionId));
            }

            return Task.CompletedTask;
        }

        public Task DeleteByActionIdAsync(string organizationId, string actionId, CancellationToken cancellationToken)
        {
            _items.RemoveWhere(x => x.Org == organizationId && x.Action == actionId);
            return Task.CompletedTask;
        }
    }

    private sealed class Agents : IAgentDirectory
    {
        private readonly List<AgentListItem> _items =
        [
            new("agt_1", "invoice-agent", "Invoice", "app_default", "env_1", "production", true, "dgk_x")
        ];

        public void Add(string id, string applicationId) =>
            _items.Add(new(id, id, id, applicationId, "env_1", "production", true, "dgk_x"));

        public Task<AgentPrincipal?> AuthenticateAsync(string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult<AgentPrincipal?>(null);

        public Task<Agent?> GetAsync(string organizationId, string agentId, CancellationToken cancellationToken) =>
            Task.FromResult<Agent?>(null);

        public Task<AgentPrincipal?> GetPrincipalAsync(string organizationId, string agentId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentPrincipal?>(null);

        public Task<AgentPrincipal?> GetPrincipalByExternalIdAsync(string organizationId, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentPrincipal?>(null);

        public Task<IReadOnlyList<AgentListItem>> ListAsync(string organizationId, string? applicationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentListItem>>(_items.Where(x => applicationId is null || x.ApplicationId == applicationId).ToList());

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

    private sealed class Applications : IApplicationDirectory
    {
        private readonly Dictionary<string, ApplicationListItem> _items = new(StringComparer.Ordinal)
        {
            ["app_default"] = new("app_default", "default", "Default", ApplicationKinds.Product, DateTimeOffset.UtcNow, 1),
            ["app_admin"] = new("app_admin", "administration", "Administration", ApplicationKinds.Administration, DateTimeOffset.UtcNow, 1)
        };

        public void Add(string id, string kind) =>
            _items[id] = new(id, id, id, kind, DateTimeOffset.UtcNow, 0);

        public Task<IReadOnlyList<ApplicationListItem>> ListAsync(string organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApplicationListItem>>(_items.Values.ToList());

        public Task<IReadOnlyList<ApplicationListItem>> ListProductAsync(string organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApplicationListItem>>(_items.Values.Where(x => x.Kind == ApplicationKinds.Product).ToList());

        public Task<ApplicationListItem?> GetAsync(string organizationId, string applicationId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.GetValueOrDefault(applicationId));

        public Task<ApplicationListItem> GetAdministrationAsync(string organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(_items["app_admin"]);

        public Task<ApplicationListItem> GetDefaultProductAsync(string organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(_items["app_default"]);

        public Task<ApplicationListItem> ResolveProductAsync(string organizationId, string? idOrSlug, CancellationToken cancellationToken) =>
            GetDefaultProductAsync(organizationId, cancellationToken);

        public Task<ApplicationListItem> CreateProductAsync(string organizationId, string name, string slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationListItem> UpdateAsync(string organizationId, string applicationId, string name, string slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task EnsureCanDeleteAsync(string organizationId, string applicationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string organizationId, string applicationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Bindings : IGuardMappingStore
    {
        public List<string> BoundActionIds { get; } = [];

        public Task<IReadOnlyList<GuardMappingRecord>> ListAsync(string organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GuardMappingRecord>>([]);

        public Task<ActionMcpBinding?> GetAsync(string organizationId, string bindingId, CancellationToken cancellationToken) =>
            Task.FromResult<ActionMcpBinding?>(null);

        public Task<ActionMcpBinding?> FindByToolAsync(string organizationId, string mcpServer, string mcpTool, CancellationToken cancellationToken) =>
            Task.FromResult<ActionMcpBinding?>(null);

        public Task AddAsync(ActionMcpBinding binding, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(ActionMcpBinding binding, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteByActionIdAsync(string organizationId, string actionId, CancellationToken cancellationToken)
        {
            BoundActionIds.Remove(actionId);
            return Task.CompletedTask;
        }
    }
}
