using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Deleguard.Api.Http;

public static class OidcAuthentication
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string ChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    public const string IdpHintItem = "kc_idp_hint";
    public const string ActionItem = "oidc_action";
    public const string RegisterAction = "register";

    public static WebApplicationBuilder AddDeleguardOidc(this WebApplicationBuilder builder)
    {
        var oidc = builder.Configuration.GetSection(OidcOptions.Section).Get<OidcOptions>() ?? new OidcOptions();
        builder.Services.AddAuthorization();
        var authentication = builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieScheme;
            options.DefaultChallengeScheme = oidc.Enabled ? ChallengeScheme : CookieScheme;
            options.DefaultSignOutScheme = oidc.Enabled ? ChallengeScheme : CookieScheme;
        }).AddCookie(CookieScheme, options =>
        {
            options.Cookie.Name = ".Deleguard.Console";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.Cookie.IsEssential = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.LoginPath = "/login";
            options.LogoutPath = "/login";
            options.AccessDeniedPath = "/login";
        });

        if (!oidc.Enabled)
        {
            return builder;
        }

        if (string.IsNullOrWhiteSpace(oidc.Authority) || string.IsNullOrWhiteSpace(oidc.ClientId) || string.IsNullOrWhiteSpace(oidc.ClientSecret))
        {
            throw new InvalidOperationException("Oidc:Authority, Oidc:ClientId, and Oidc:ClientSecret are required when Oidc:Enabled is true.");
        }

        authentication.AddOpenIdConnect(ChallengeScheme, options =>
        {
            options.Authority = oidc.Authority.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(oidc.MetadataAddress))
            {
                options.MetadataAddress = oidc.MetadataAddress;
            }

            options.ClientId = oidc.ClientId;
            options.ClientSecret = oidc.ClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            // Persist only the id_token (not access/refresh) so RP-initiated logout can send id_token_hint.
            options.SaveTokens = false;
            options.SignOutScheme = CookieScheme;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.MapInboundClaims = false;
            options.UseTokenLifetime = false;
            options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
            options.RequireHttpsMetadata = oidc.Authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            options.CallbackPath = "/signin-oidc";
            options.SignedOutCallbackPath = "/signout-callback-oidc";
            options.RemoteSignOutPath = "/signout-oidc";
            options.SignedOutRedirectUri = "/";
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("email");
            options.Scope.Add("profile");
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            if (!string.IsNullOrWhiteSpace(oidc.BackchannelAuthority))
            {
                options.BackchannelHttpHandler = new KeycloakBackchannelHandler(oidc.Authority, oidc.BackchannelAuthority);
            }

            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context =>
                {
                    CopyIdentityClaims(context.Principal, context.SecurityToken, context.Options.Authority);
                    if (context.Properties is not null)
                    {
                        PersistIdTokenHint(
                            context.Properties,
                            context.SecurityToken,
                            context.TokenEndpointResponse?.IdToken ?? context.ProtocolMessage.IdToken);
                    }

                    return Task.CompletedTask;
                },
                OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.ResponseMode = OpenIdConnectResponseMode.Query;
                    if (context.Properties.Items.TryGetValue(IdpHintItem, out var hint) && !string.IsNullOrWhiteSpace(hint))
                    {
                        context.ProtocolMessage.SetParameter("kc_idp_hint", hint);
                    }

                    if (context.Properties.Items.TryGetValue(ActionItem, out var action)
                        && string.Equals(action, RegisterAction, StringComparison.Ordinal))
                    {
                        context.ProtocolMessage.Prompt = "create";
                    }

                    return Task.CompletedTask;
                },
                OnRedirectToIdentityProviderForSignOut = context =>
                {
                    AttachLogoutClientId(context.ProtocolMessage, context.Options.ClientId);
                    return Task.CompletedTask;
                },
                OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect("/login?error=oidc");
                    return Task.CompletedTask;
                }
            };
        });

        return builder;
    }

    public static AuthenticationProperties ChallengeProperties(string? idpHint = null, bool register = false)
    {
        var properties = new AuthenticationProperties { RedirectUri = "/" };
        if (!string.IsNullOrWhiteSpace(idpHint))
        {
            properties.Items[IdpHintItem] = idpHint;
        }

        if (register)
        {
            properties.Items[ActionItem] = RegisterAction;
        }

        return properties;
    }

    internal static void PersistIdTokenHint(
        AuthenticationProperties properties,
        Microsoft.IdentityModel.Tokens.SecurityToken? token,
        string? fallbackIdToken)
    {
        var idToken = ReadEncodedToken(token) ?? fallbackIdToken;
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return;
        }

        properties.StoreTokens(
        [
            new AuthenticationToken { Name = OpenIdConnectParameterNames.IdToken, Value = idToken }
        ]);
    }

    internal static void AttachLogoutClientId(OpenIdConnectMessage message, string? clientId)
    {
        if (string.IsNullOrWhiteSpace(message.ClientId) && !string.IsNullOrWhiteSpace(clientId))
        {
            message.ClientId = clientId;
        }
    }

    private static void CopyIdentityClaims(ClaimsPrincipal? principal, Microsoft.IdentityModel.Tokens.SecurityToken? securityToken, string? authority)
    {
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        AddIfMissing(identity, "iss", securityToken?.Issuer);
        AddIfMissing(identity, "iss", HumanIdentityClaims.NormalizeIssuer(authority));
        foreach (var claim in ReadTokenClaims(securityToken))
        {
            if (claim.Type is "sub" or "email" or "email_verified" or "name" or "company" or "given_name" or "family_name" or "preferred_username")
            {
                AddIfMissing(identity, claim.Type, claim.Value);
            }
            else if (claim.Type is "roles" or "role")
            {
                AddIfMissing(identity, "roles", claim.Value);
            }
        }

        AddIfMissing(identity, "sub", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        AddIfMissing(identity, "sub", ReadTokenSubject(securityToken));
        AddIfMissing(identity, "email", identity.FindFirst(ClaimTypes.Email)?.Value);
    }

    private static IEnumerable<Claim> ReadTokenClaims(Microsoft.IdentityModel.Tokens.SecurityToken? token)
    {
        if (token is null)
        {
            return [];
        }

        if (token is JwtSecurityToken jwt)
        {
            return jwt.Claims;
        }

        return token.GetType().GetProperty("Claims")?.GetValue(token) is IEnumerable<Claim> claims
            ? claims
            : [];
    }

    private static string? ReadEncodedToken(Microsoft.IdentityModel.Tokens.SecurityToken? token)
    {
        if (token is JwtSecurityToken jwt && !string.IsNullOrWhiteSpace(jwt.RawData))
        {
            return jwt.RawData;
        }

        return token?.GetType().GetProperty("EncodedToken")?.GetValue(token) as string;
    }

    private static string? ReadTokenSubject(Microsoft.IdentityModel.Tokens.SecurityToken? token)
    {
        if (token is null)
        {
            return null;
        }

        if (token is JwtSecurityToken jwt)
        {
            return jwt.Subject;
        }

        return token.GetType().GetProperty("Subject")?.GetValue(token) as string;
    }

    private static void AddIfMissing(ClaimsIdentity identity, string type, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (string.Equals(type, "roles", StringComparison.Ordinal))
        {
            if (identity.FindAll(type).Any(c => string.Equals(c.Value, value, StringComparison.Ordinal)))
            {
                return;
            }

            identity.AddClaim(new Claim(type, value));
            return;
        }

        if (identity.FindFirst(type) is null)
        {
            identity.AddClaim(new Claim(type, value));
        }
    }

    private sealed class KeycloakBackchannelHandler : DelegatingHandler
    {
        private readonly Uri _publicOrigin;
        private readonly Uri _internalOrigin;

        public KeycloakBackchannelHandler(string authority, string backchannelAuthority)
            : base(new HttpClientHandler())
        {
            _publicOrigin = new Uri(authority);
            _internalOrigin = new Uri(backchannelAuthority.TrimEnd('/') + "/");
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null
                && string.Equals(request.RequestUri.Scheme, _publicOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.RequestUri.Host, _publicOrigin.Host, StringComparison.OrdinalIgnoreCase)
                && request.RequestUri.Port == _publicOrigin.Port)
            {
                var rewritten = new UriBuilder(request.RequestUri)
                {
                    Scheme = _internalOrigin.Scheme,
                    Host = _internalOrigin.Host,
                    Port = _internalOrigin.IsDefaultPort ? -1 : _internalOrigin.Port
                };
                request.RequestUri = rewritten.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}

public static class HumanIdentityClaims
{
    public static bool TryParse(ClaimsPrincipal user, out FederatedIdentity identity) =>
        TryParse(user, fallbackIssuer: null, out identity);

    public static bool TryParse(ClaimsPrincipal user, string? fallbackIssuer, out FederatedIdentity identity)
    {
        identity = new FederatedIdentity("", "", "", "", false);
        var issuer = NormalizeIssuer(First(user, "iss") ?? fallbackIssuer);
        var subject = First(user, "sub", ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        var email = First(user, "email", ClaimTypes.Email) ?? "";
        var given = First(user, "given_name");
        var family = First(user, "family_name");
        var combined = string.Join(' ', new[] { given, family }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var name = First(user, "name", "preferred_username")
                   ?? (combined.Length > 0 ? combined : null)
                   ?? email;
        identity = new FederatedIdentity(issuer, subject, email, name, IsEmailVerified(user), First(user, "company"));
        return true;
    }

    public static bool IsEmailVerified(ClaimsPrincipal user)
    {
        var value = First(user, "email_verified");
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "1", StringComparison.Ordinal);
    }

    public static bool IsPlatformAdmin(ClaimsPrincipal user) =>
        user.FindAll("roles").Any(c => string.Equals(c.Value, AccessControlIds.PlatformAdminRole, StringComparison.Ordinal))
        || user.IsInRole(AccessControlIds.PlatformAdminRole);

    public static string? NormalizeIssuer(string? issuer) =>
        string.IsNullOrWhiteSpace(issuer) ? null : issuer.Trim().TrimEnd('/');

    private static string? First(ClaimsPrincipal user, params string[] types)
    {
        foreach (var type in types)
        {
            var value = user.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
