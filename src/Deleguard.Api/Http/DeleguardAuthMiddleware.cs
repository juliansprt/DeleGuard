using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Http;

public static class AuthExtensions
{
    public const string AgentItem = "deleguard.agent";
    public const string ConsoleItem = "deleguard.console";
    public const string ConsoleUserItem = "deleguard.console.user";
    public const string McpItem = "deleguard.mcp";
    public const string ConsoleCookie = "deleguard_console";
    public const string UserCookie = "deleguard_user";
    public const string DataProtectionPurpose = "deleguard.console.user";

    public static AgentPrincipal? GetAgentPrincipal(this HttpContext http) =>
        http.Items.TryGetValue(AgentItem, out var value) ? value as AgentPrincipal : null;

    public static ConsoleUserPrincipal? GetConsoleUser(this HttpContext http) =>
        http.Items.TryGetValue(ConsoleUserItem, out var value) ? value as ConsoleUserPrincipal : null;

    public static McpCallerPrincipal? GetMcpPrincipal(this HttpContext http) =>
        http.Items.TryGetValue(McpItem, out var value) ? value as McpCallerPrincipal : null;

    public static bool IsConsole(this HttpContext http) =>
        http.Items.ContainsKey(ConsoleItem) && http.GetConsoleUser() is not null;

    public static void EnsureConsole(this HttpContext http)
    {
        if (!http.IsConsole())
        {
            throw new Application.AgentAuthenticationException("Console authentication required.");
        }
    }

    public static ConsoleUserPrincipal RequireConsoleUser(this HttpContext http)
    {
        http.EnsureConsole();
        return http.GetConsoleUser() ?? throw new Application.AgentAuthenticationException("Console authentication required.");
    }

    public static ConsoleUserPrincipal EnsurePermission(this HttpContext http, string permission)
    {
        var user = http.RequireConsoleUser();
        if (!user.HasPermission(permission))
        {
            throw new Application.ForbiddenException($"Missing permission '{permission}'.");
        }

        return user;
    }

    public static string RequireOrganizationId(this HttpContext http) =>
        http.RequireConsoleUser().OrganizationId;

    public static bool IsPendingOnboarding(this HttpContext http) =>
        http.Items.ContainsKey(DeleguardAuthMiddleware.PendingOnboardingItem);

    public static bool IsUnverifiedEmail(this HttpContext http) =>
        http.Items.ContainsKey(DeleguardAuthMiddleware.UnverifiedEmailItem);

    public static bool IsDisabledConsoleUser(this HttpContext http) =>
        http.Items.ContainsKey(DeleguardAuthMiddleware.DisabledUserItem);

    public static bool IsConsoleSessionError(this HttpContext http) =>
        http.Items.ContainsKey(DeleguardAuthMiddleware.SessionErrorItem);
}

public sealed class DeleguardAuthMiddleware
{
    public const string PendingOnboardingItem = "deleguard.oidc.pending";
    public const string UnverifiedEmailItem = "deleguard.oidc.unverified";
    public const string DisabledUserItem = "deleguard.oidc.disabled";
    public const string SessionErrorItem = "deleguard.oidc.session_error";

    private readonly RequestDelegate _next;

    public DeleguardAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAgentDirectory agents,
        IUserDirectory users,
        IHumanIdentityDirectory humans,
        IMcpDirectory mcp,
        ResolveHumanSessionHandler sessions,
        IDataProtectionProvider protection,
        IOptions<DemoAuthOptions> auth,
        IOptions<OidcOptions> oidcOptions,
        ILogger<DeleguardAuthMiddleware> logger)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var token = ReadBearer(context);
        var cookieToken = context.Request.Cookies[AuthExtensions.ConsoleCookie];
        var userCookie = context.Request.Cookies[AuthExtensions.UserCookie];
        var options = auth.Value;
        var allowEmergency = options.AllowEmergencyConsoleToken && !string.IsNullOrWhiteSpace(options.ConsoleToken);

        if (!string.IsNullOrWhiteSpace(token) && token.StartsWith("dgm_", StringComparison.Ordinal))
        {
            var mcpPrincipal = await mcp.AuthenticateAsync(token, context.RequestAborted);
            if (mcpPrincipal is not null)
            {
                context.Items[AuthExtensions.McpItem] = mcpPrincipal;
            }
        }
        else if (allowEmergency && !string.IsNullOrWhiteSpace(token) && string.Equals(token, options.ConsoleToken, StringComparison.Ordinal))
        {
            await SetBootstrapAsync(context, users, options, context.RequestAborted);
        }
        else if (!string.IsNullOrWhiteSpace(token) && token.StartsWith("dgk_", StringComparison.Ordinal))
        {
            var principal = await agents.AuthenticateAsync(token, context.RequestAborted);
            if (principal is not null)
            {
                context.Items[AuthExtensions.AgentItem] = principal;
            }
        }
        else if (allowEmergency && !string.IsNullOrWhiteSpace(cookieToken) && string.Equals(cookieToken, options.ConsoleToken, StringComparison.Ordinal))
        {
            await SetBootstrapAsync(context, users, options, context.RequestAborted);
        }
        else if (context.User.Identity?.IsAuthenticated == true)
        {
            var oidc = oidcOptions.Value;
            if (HumanIdentityClaims.TryParse(context.User, oidc.Authority, out var federated))
            {
                if (!oidc.RequireVerifiedEmail && !string.IsNullOrWhiteSpace(federated.Email))
                {
                    federated = federated with { EmailVerified = true };
                }

                try
                {
                    var principal = await sessions.HandleAsync(federated, context.RequestAborted);
                    if (principal is not null)
                    {
                        if (HumanIdentityClaims.IsPlatformAdmin(context.User))
                        {
                            principal = principal.WithPlatformAdmin();
                        }

                        SetConsole(context, principal);
                    }
                    else if (string.IsNullOrWhiteSpace(federated.Email) || !federated.EmailVerified)
                    {
                        context.Items[UnverifiedEmailItem] = true;
                    }
                    else
                    {
                        context.Items[PendingOnboardingItem] = federated;
                    }
                }
                catch (ForbiddenException)
                {
                    context.Items[DisabledUserItem] = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to resolve the Deleguard console session from Keycloak.");
                    context.Items[SessionErrorItem] = true;
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(userCookie))
        {
            var userId = UnprotectUserId(protection, userCookie);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var principal = await humans.GetPrincipalByUserIdAsync(userId, context.RequestAborted)
                                ?? await users.GetPrincipalAsync(options.DemoOrganizationId, userId, context.RequestAborted);
                if (principal is not null)
                {
                    SetConsole(context, principal);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(token))
        {
            var principal = await agents.AuthenticateAsync(token, context.RequestAborted);
            if (principal is not null)
            {
                context.Items[AuthExtensions.AgentItem] = principal;
            }
        }

        var needsAgent = path.StartsWith("/v1/authorize", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/v1/operations", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/v1/permits", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/v1/evaluations", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/v1/decisions", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/v1/executions", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/v1/guard", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/v1/gateway/config", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/v1/gateway/spend", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/mcp/guard", StringComparison.OrdinalIgnoreCase);
        var needsMcp = HttpMethods.IsPost(context.Request.Method) &&
                       path.Equals("/mcp", StringComparison.OrdinalIgnoreCase);
        var needsConsole = path.StartsWith("/v1/approvals", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/audit", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/agents", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/environments", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/actions", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/policies", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/webhooks", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/users", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/roles", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/mcp", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/gateway/settings", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/v1/gateway/routes", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/approvals", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/audit", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/ide", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/agents", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/environments", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/actions", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/policies", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/webhooks", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/users", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/roles", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/access/mcp", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/access/ai-gateway", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/playground", StringComparison.OrdinalIgnoreCase);

        if (needsAgent && context.GetAgentPrincipal() is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "invalid_agent_credential" });
            return;
        }

        if (needsMcp && context.GetMcpPrincipal() is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            var metadata = $"{context.Request.Scheme}://{context.Request.Host}/.well-known/oauth-protected-resource";
            context.Response.Headers.WWWAuthenticate =
                $"Bearer realm=\"deleguard-admin\", resource_metadata=\"{metadata}\"";
            var error = token is not null && (token.StartsWith("dgk_", StringComparison.Ordinal) || token.StartsWith("dgc_", StringComparison.Ordinal))
                ? "mcp_requires_dgm_token"
                : "invalid_mcp_credential";
            await context.Response.WriteAsJsonAsync(new { error });
            return;
        }

        if (IsAnonymousPath(path))
        {
            await _next(context);
            return;
        }

        if (context.IsUnverifiedEmail())
        {
            if (path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "email_unverified" });
                return;
            }

            context.Response.Redirect("/login?error=unverified");
            return;
        }

        if (context.IsDisabledConsoleUser() || context.IsConsoleSessionError())
        {
            if (path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "console_session_unavailable" });
                return;
            }

            context.Response.Redirect(context.IsDisabledConsoleUser() ? "/login?error=disabled" : "/login?error=session");
            return;
        }

        if (context.IsPendingOnboarding())
        {
            if (path.StartsWith("/onboarding", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            if (path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "onboarding_required" });
                return;
            }

            context.Response.Redirect("/onboarding");
            return;
        }

        if (path.StartsWith("/onboarding", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect(context.IsConsole() ? "/" : "/login");
            return;
        }

        if (needsConsole && !context.IsConsole())
        {
            if (path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "console_authentication_required" });
                return;
            }

            context.Response.Redirect("/login");
            return;
        }

        await _next(context);
    }

    private static bool IsAnonymousPath(string path) =>
        path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/signin-oidc", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/signout-callback-oidc", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/signout-oidc", StringComparison.OrdinalIgnoreCase);

    private static async Task SetBootstrapAsync(HttpContext context, IUserDirectory users, DemoAuthOptions options, CancellationToken cancellationToken)
    {
        var principal = await users.GetBootstrapPrincipalAsync(options.DemoOrganizationId, cancellationToken);
        if (principal is null)
        {
            return;
        }

        SetConsole(context, principal);
    }

    private static void SetConsole(HttpContext context, ConsoleUserPrincipal principal)
    {
        context.Items[AuthExtensions.ConsoleItem] = true;
        context.Items[AuthExtensions.ConsoleUserItem] = principal;
    }

    private static string? ReadBearer(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        return null;
    }

    public static string ProtectUserId(IDataProtectionProvider protection, string userId)
    {
        var protector = protection.CreateProtector(AuthExtensions.DataProtectionPurpose);
        return protector.Protect(userId);
    }

    private static string? UnprotectUserId(IDataProtectionProvider protection, string cookie)
    {
        try
        {
            var protector = protection.CreateProtector(AuthExtensions.DataProtectionPurpose);
            return protector.Unprotect(cookie);
        }
        catch
        {
            return null;
        }
    }
}
