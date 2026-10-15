using System.Security.Claims;
using Deleguard.Api.Http;
using Deleguard.Domain.Identity;

namespace Deleguard.IntegrationTests;

public sealed class HumanIdentityClaimsTests
{
    [Fact]
    public void TryParse_uses_fallback_issuer_when_iss_is_missing()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "user-1"),
                new Claim("email", "ada@example.com"),
                new Claim("name", "Ada Lovelace"),
                new Claim("email_verified", "false")
            ],
            authenticationType: "oidc"));

        Assert.True(HumanIdentityClaims.TryParse(user, "http://localhost:8081/realms/deleguard/", out var identity));
        Assert.Equal("http://localhost:8081/realms/deleguard", identity.Issuer);
        Assert.Equal("user-1", identity.Subject);
        Assert.Equal("ada@example.com", identity.Email);
        Assert.False(identity.EmailVerified);
    }

    [Fact]
    public void TryParse_requires_subject()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("iss", "http://localhost:8081/realms/deleguard")],
            authenticationType: "oidc"));

        Assert.False(HumanIdentityClaims.TryParse(user, "http://localhost:8081/realms/deleguard", out _));
    }

    [Fact]
    public void IsEmailVerified_accepts_boolean_true()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("email_verified", "True")],
            authenticationType: "oidc"));

        Assert.True(HumanIdentityClaims.IsEmailVerified(user));
    }

    [Fact]
    public void IsPlatformAdmin_reads_roles_claim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("roles", AccessControlIds.PlatformAdminRole)],
            authenticationType: "oidc"));

        Assert.True(HumanIdentityClaims.IsPlatformAdmin(user));
    }
}
