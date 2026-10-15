using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Identity;

namespace Deleguard.UnitTests;

public sealed class OnboardingHandlerTests
{
    [Fact]
    public async Task Complete_rejects_short_organization_name()
    {
        var handler = new CompleteOnboardingHandler(new FakeHumans(), new FakeTenants(), new FakeUow());
        var identity = new FederatedIdentity("http://issuer", "sub_1", "user@example.com", "User", true);
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.HandleAsync("ab", identity, CancellationToken.None));
        Assert.Contains("3 and 80", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Complete_rejects_unverified_email()
    {
        var handler = new CompleteOnboardingHandler(new FakeHumans(), new FakeTenants(), new FakeUow());
        var identity = new FederatedIdentity("http://issuer", "sub_1", "user@example.com", "User", EmailVerified: false);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.HandleAsync("Acme Corp", identity, CancellationToken.None));
    }

    [Fact]
    public async Task Complete_returns_existing_federated_user()
    {
        var existing = new ConsoleUserPrincipal("org_1", "usr_1", "user@example.com", "User", [AccessControlIds.OrgAdminRole], [], false);
        var humans = new FakeHumans { Existing = existing };
        var tenants = new FakeTenants();
        var handler = new CompleteOnboardingHandler(humans, tenants, new FakeUow());
        var identity = new FederatedIdentity("http://issuer", "sub_1", "user@example.com", "User", true);

        var result = await handler.HandleAsync("Ignored Org", identity, CancellationToken.None);

        Assert.Equal("org_1", result.OrganizationId);
        Assert.Empty(tenants.Created);
    }

    [Fact]
    public async Task Complete_provisions_new_org_when_unknown()
    {
        var tenants = new FakeTenants();
        var handler = new CompleteOnboardingHandler(new FakeHumans(), tenants, new FakeUow());
        var identity = new FederatedIdentity("http://issuer", "sub_new", "owner@example.com", "Owner", true);

        var result = await handler.HandleAsync("  Northwind  Trading  ", identity, CancellationToken.None);

        Assert.Equal("Northwind Trading", Assert.Single(tenants.Created));
        Assert.Equal("org_new", result.OrganizationId);
        Assert.Contains(AccessControlIds.OrgAdminRole, result.Roles);
    }

    [Fact]
    public async Task Resolve_does_not_link_unverified_email()
    {
        var handler = new ResolveHumanSessionHandler(new FakeHumans { Pending = true }, new FakeTenants(), new FakeUow());
        var identity = new FederatedIdentity("http://issuer", "sub_x", "invitee@example.com", "Invitee", false);
        Assert.Null(await handler.HandleAsync(identity, CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_provisions_org_for_unknown_verified_user()
    {
        var tenants = new FakeTenants();
        var handler = new ResolveHumanSessionHandler(new FakeHumans(), tenants, new FakeUow());
        var identity = new FederatedIdentity("http://issuer", "sub_new", "owner@example.com", "Ada", true, "Northwind");

        var result = await handler.HandleAsync(identity, CancellationToken.None);

        Assert.Equal("Northwind", Assert.Single(tenants.Created));
        Assert.Equal("org_new", result!.OrganizationId);
    }

    [Fact]
    public async Task Resolve_uses_display_name_when_company_is_blank()
    {
        var tenants = new FakeTenants();
        var handler = new ResolveHumanSessionHandler(new FakeHumans(), tenants, new FakeUow());
        var identity = new FederatedIdentity("http://issuer", "sub_new", "owner@example.com", "Ada Lovelace", true);

        await handler.HandleAsync(identity, CancellationToken.None);

        Assert.Equal("Ada Lovelace workspace", Assert.Single(tenants.Created));
    }

    [Fact]
    public async Task Complete_accepts_blank_name_and_uses_company()
    {
        var tenants = new FakeTenants();
        var handler = new CompleteOnboardingHandler(new FakeHumans(), tenants, new FakeUow());
        var identity = new FederatedIdentity("http://issuer", "sub_new", "owner@example.com", "Owner", true, "Acme Corp");

        await handler.HandleAsync("  ", identity, CancellationToken.None);

        Assert.Equal("Acme Corp", Assert.Single(tenants.Created));
    }

    private sealed class FakeUow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken) => action();
    }

    private sealed class FakeHumans : IHumanIdentityDirectory
    {
        public ConsoleUserPrincipal? Existing { get; init; }
        public bool Pending { get; init; }

        public Task<ConsoleUserPrincipal?> FindByExternalSubjectAsync(string issuer, string subject, CancellationToken cancellationToken) =>
            Task.FromResult(Existing);

        public Task<ConsoleUserPrincipal?> GetPrincipalByUserIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(Existing);

        public Task<ConsoleUserPrincipal?> LinkVerifiedEmailAsync(FederatedIdentity identity, CancellationToken cancellationToken) =>
            Task.FromResult(Pending
                ? new ConsoleUserPrincipal("org_invited", "usr_invited", identity.Email, identity.DisplayName, ["operator"], [], false)
                : null);
    }

    private sealed class FakeTenants : ITenantBootstrapper
    {
        public List<string> Created { get; } = [];

        public Task<ConsoleUserPrincipal> ProvisionNewOrganizationAsync(string organizationName, FederatedIdentity identity, CancellationToken cancellationToken)
        {
            Created.Add(organizationName);
            return Task.FromResult(new ConsoleUserPrincipal(
                "org_new",
                "usr_new",
                identity.Email,
                identity.DisplayName,
                [AccessControlIds.OrgAdminRole],
                PermissionCatalog.All.Select(x => x.Name).ToList(),
                false));
        }
    }
}
