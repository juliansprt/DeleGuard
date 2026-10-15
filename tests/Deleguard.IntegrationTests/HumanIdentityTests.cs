using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Deleguard.IntegrationTests;

public sealed class HumanIdentityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("deleguard")
        .WithUsername("deleguard")
        .WithPassword("deleguard")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                    ["DemoAuth:ApiKeyPepper"] = "deleguard-dev-pepper",
                    ["DemoAuth:ConsoleToken"] = DemoSeeder.DemoConsoleToken,
                    ["DemoAuth:AllowEmergencyConsoleToken"] = "true",
                    ["Oidc:Enabled"] = "false",
                    ["Permits:SigningKey"] = "deleguard-dev-permit-hmac-key-change-in-prod-32"
                });
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Onboarding_creates_isolated_organizations()
    {
        var (orgA, userA) = await ProvisionAsync("Acme Steel", "https://idp.example", "sub-acme", "owner@acme.test");
        var (orgB, userB) = await ProvisionAsync("Globex", "https://idp.example", "sub-globex", "owner@globex.test");
        Assert.NotEqual(orgA, orgB);

        using var clientA = ConsoleClient(userA);
        using var clientB = ConsoleClient(userB);

        var usersA = await clientA.GetFromJsonAsync<JsonElement>("/v1/users");
        var usersB = await clientB.GetFromJsonAsync<JsonElement>("/v1/users");
        Assert.Contains(usersA.EnumerateArray(), item => item.GetProperty("email").GetString() == "owner@acme.test");
        Assert.DoesNotContain(usersA.EnumerateArray(), item => item.GetProperty("email").GetString() == "owner@globex.test");
        Assert.Contains(usersB.EnumerateArray(), item => item.GetProperty("email").GetString() == "owner@globex.test");
        Assert.DoesNotContain(usersB.EnumerateArray(), item => item.GetProperty("email").GetString() == "owner@acme.test");
    }

    [Fact]
    public async Task Verified_email_links_invited_user_instead_of_new_org()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var humans = scope.ServiceProvider.GetRequiredService<IHumanIdentityDirectory>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var invited = await users.CreateAsync(
            DemoSeeder.OrganizationId,
            "invitee@demo.local",
            "Invitee",
            ["operator"],
            null,
            CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var linked = await humans.LinkVerifiedEmailAsync(
            new FederatedIdentity("https://idp.example", "sub-invitee", "invitee@demo.local", "Invitee", true),
            CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        Assert.NotNull(linked);
        Assert.Equal(DemoSeeder.OrganizationId, linked.OrganizationId);
        Assert.Equal(invited.UserId, linked.UserId);

        var bySubject = await humans.FindByExternalSubjectAsync("https://idp.example", "sub-invitee", CancellationToken.None);
        Assert.Equal(invited.UserId, bySubject?.UserId);
    }

    [Fact]
    public async Task Unverified_email_is_not_linked()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var humans = scope.ServiceProvider.GetRequiredService<IHumanIdentityDirectory>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await users.CreateAsync(DemoSeeder.OrganizationId, "pending@demo.local", "Pending", ["auditor"], null, CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var linked = await humans.LinkVerifiedEmailAsync(
            new FederatedIdentity("https://idp.example", "sub-pending", "pending@demo.local", "Pending", false),
            CancellationToken.None);
        Assert.Null(linked);
    }

    [Fact]
    public async Task Console_user_cookie_cannot_call_authorize()
    {
        var (_, userId) = await ProvisionAsync("Authz Boundary", "https://idp.example", "sub-boundary", "admin@boundary.test");
        using var client = ConsoleClient(userId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/authorize")
        {
            Content = JsonContent.Create(new
            {
                action = "invoice.cancel",
                resource = new { type = "invoice", id = "x" }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_agent_credential", body.GetProperty("error").GetString());
    }

    private async Task<(string OrganizationId, string UserId)> ProvisionAsync(string name, string issuer, string subject, string email)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantBootstrapper>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var created = await tenants.ProvisionNewOrganizationAsync(
            name,
            new FederatedIdentity(issuer, subject, email, name, true),
            CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);
        return (created.OrganizationId, created.UserId);
    }

    private HttpClient ConsoleClient(string userId)
    {
        var protection = _factory!.Services.GetRequiredService<IDataProtectionProvider>();
        var cookie = DeleguardAuthMiddleware.ProtectUserId(protection, userId);
        var client = _factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{AuthExtensions.UserCookie}={cookie}");
        return client;
    }
}
