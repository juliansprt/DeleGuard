using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages;

public sealed class LoginModel : PageModel
{
    public const string ReauthCookie = "dg_oidc_reauth";

    private readonly DemoAuthOptions _options;
    private readonly OidcOptions _oidc;
    private readonly IUserDirectory _users;
    private readonly IDataProtectionProvider _protection;

    public LoginModel(
        IOptions<DemoAuthOptions> options,
        IOptions<OidcOptions> oidc,
        IUserDirectory users,
        IDataProtectionProvider protection)
    {
        _options = options.Value;
        _oidc = oidc.Value;
        _users = users;
        _protection = protection;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string Token { get; set; } = string.Empty;

    public string? Error { get; set; }
    public bool OidcEnabled => _oidc.Enabled;
    public bool AllowPasswordLogin => !_oidc.Enabled;
    public bool AllowEmergencyToken => _options.AllowEmergencyConsoleToken;

    public async Task<IActionResult> OnGetAsync(string? error, string? register, string? idp, string? mode)
    {
        if (HttpContext.IsConsole())
        {
            Response.Cookies.Delete(ReauthCookie, CookiePath());
            return Redirect("/");
        }

        if (HttpContext.IsPendingOnboarding())
        {
            return Redirect("/onboarding");
        }

        Error = error switch
        {
            "unverified" => "Verify your email before signing in. Local compose delivers mail to http://localhost:8025.",
            "oidc" => "Sign-in was rejected by Keycloak. Sign out if a stale session is stuck, then try again.",
            "disabled" => "This user is disabled. Ask a platform admin in Keycloak to enable the account.",
            "session" => "Deleguard could not open a console session from this Keycloak login. Sign out and try again.",
            _ => null
        };

        if (OidcEnabled && string.IsNullOrEmpty(error) && !string.Equals(mode, "emergency", StringComparison.OrdinalIgnoreCase))
        {
            var registerRequested = string.Equals(register, "1", StringComparison.Ordinal)
                                    || string.Equals(register, "true", StringComparison.OrdinalIgnoreCase);
            if (User.Identity?.IsAuthenticated == true)
            {
                if (string.Equals(Request.Cookies[ReauthCookie], "1", StringComparison.Ordinal))
                {
                    Error = "Keycloak already has a session, but Deleguard could not open the console. Sign out, then Sign in again.";
                    return Page();
                }

                Response.Cookies.Append(ReauthCookie, "1", new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    Path = "/",
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromMinutes(5)
                });
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            return Challenge(
                OidcAuthentication.ChallengeProperties(idp, registerRequested),
                OpenIdConnectDefaults.AuthenticationScheme);
        }

        return Page();
    }

    public IActionResult OnGetLogout() => SignOutLocalAndKeycloak();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (AllowEmergencyToken && !string.IsNullOrWhiteSpace(Token) && string.Equals(Token, _options.ConsoleToken, StringComparison.Ordinal))
        {
            Response.Cookies.Append(AuthExtensions.ConsoleCookie, Token, SessionCookie());
            return Redirect("/");
        }

        if (!AllowPasswordLogin)
        {
            Error = "Use single sign-on to continue.";
            return Page();
        }

        var principal = await _users.AuthenticatePasswordAsync(_options.DemoOrganizationId, Email, Password, cancellationToken);
        if (principal is null)
        {
            Error = "Those credentials are not valid.";
            return Page();
        }

        Response.Cookies.Append(AuthExtensions.UserCookie, DeleguardAuthMiddleware.ProtectUserId(_protection, principal.UserId), SessionCookie());
        return Redirect("/");
    }

    public IActionResult OnPostLogout() => SignOutLocalAndKeycloak();

    private IActionResult SignOutLocalAndKeycloak()
    {
        Response.Cookies.Delete(AuthExtensions.ConsoleCookie, CookiePath());
        Response.Cookies.Delete(AuthExtensions.UserCookie, CookiePath());
        Response.Cookies.Delete(ReauthCookie, CookiePath());
        if (_oidc.Enabled)
        {
            return SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                OpenIdConnectDefaults.AuthenticationScheme,
                CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return Redirect("/");
    }

    private static CookieOptions SessionCookie() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/"
    };

    private static CookieOptions CookiePath() => new() { Path = "/" };
}
