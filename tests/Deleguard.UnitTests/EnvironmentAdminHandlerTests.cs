using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;

namespace Deleguard.UnitTests;

public sealed class EnvironmentAdminHandlerTests
{
    [Fact]
    public async Task Create_rejects_invalid_name()
    {
        var handler = new EnvironmentAdminHandler(new FakeEnvironments(), new FakeUow());
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.CreateAsync("org_demo", "app_1", "Production", CancellationToken.None));
        Assert.Contains("lowercase slug", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_fails_closed_when_agents_remain()
    {
        var directory = new FakeEnvironments
        {
            Items =
            [
                new EnvironmentListItem("env_1", "production", "app_1", DateTimeOffset.UtcNow, 2),
                new EnvironmentListItem("env_2", "staging", "app_1", DateTimeOffset.UtcNow, 0)
            ]
        };
        var handler = new EnvironmentAdminHandler(directory, new FakeUow());
        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.DeleteAsync("org_demo", "env_1", CancellationToken.None));
        Assert.Equal(2, directory.Items.Count);
    }

    [Fact]
    public async Task Delete_fails_closed_on_last_environment()
    {
        var directory = new FakeEnvironments
        {
            Items = [new EnvironmentListItem("env_1", "staging", "app_1", DateTimeOffset.UtcNow, 0)]
        };
        var handler = new EnvironmentAdminHandler(directory, new FakeUow());
        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.DeleteAsync("org_demo", "env_1", CancellationToken.None));
        Assert.Single(directory.Items);
    }

    private sealed class FakeUow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken) => action();
    }

    private sealed class FakeEnvironments : IEnvironmentDirectory
    {
        public List<EnvironmentListItem> Items { get; init; } = [];

        public Task<IReadOnlyList<EnvironmentListItem>> ListAsync(string organizationId, string applicationId, CancellationToken cancellationToken) =>
            Task.FromResult((IReadOnlyList<EnvironmentListItem>)Items.Where(item => item.ApplicationId == applicationId).ToList());

        public Task<EnvironmentListItem?> GetAsync(string organizationId, string environmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(item => item.Id == environmentId));

        public Task<EnvironmentListItem> CreateAsync(string organizationId, string applicationId, string name, CancellationToken cancellationToken)
        {
            var item = new EnvironmentListItem("env_new", name, applicationId, DateTimeOffset.UtcNow, 0);
            Items.Add(item);
            return Task.FromResult(item);
        }

        public Task<EnvironmentListItem> UpdateAsync(string organizationId, string environmentId, string name, CancellationToken cancellationToken)
        {
            var current = Items.First(item => item.Id == environmentId);
            var updated = current with { Name = name };
            Items[Items.IndexOf(current)] = updated;
            return Task.FromResult(updated);
        }

        public Task EnsureCanDeleteAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
        {
            var item = Items.FirstOrDefault(x => x.Id == environmentId) ?? throw new NotFoundException("Environment not found.");
            if (item.AgentCount > 0)
            {
                throw new ConflictException("Move or disable agents before deleting this environment.");
            }

            if (Items.Count(x => x.ApplicationId == item.ApplicationId) <= 1)
            {
                throw new ConflictException("An application must keep at least one environment.");
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
        {
            EnsureCanDeleteAsync(organizationId, environmentId, cancellationToken).GetAwaiter().GetResult();
            Items.RemoveAll(item => item.Id == environmentId);
            return Task.CompletedTask;
        }
    }
}
