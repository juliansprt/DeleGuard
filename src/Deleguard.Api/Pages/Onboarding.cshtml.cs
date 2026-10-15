using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages;

public sealed class OnboardingModel : PageModel
{
    private readonly CompleteOnboardingHandler _onboarding;

    public OnboardingModel(CompleteOnboardingHandler onboarding)
    {
        _onboarding = onboarding;
    }

    [BindProperty]
    public string OrganizationName { get; set; } = string.Empty;

    public string? Error { get; set; }
    public string Email { get; private set; } = string.Empty;

    public IActionResult OnGet()
    {
        if (HttpContext.IsConsole())
        {
            return Redirect("/start");
        }

        if (!HttpContext.IsPendingOnboarding() || !HumanIdentityClaims.TryParse(HttpContext.User, out var identity))
        {
            return Redirect("/login");
        }

        Email = identity.Email;
        OrganizationName = string.IsNullOrWhiteSpace(OrganizationName) ? SuggestName(identity.DisplayName, identity.Email) : OrganizationName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!HumanIdentityClaims.TryParse(HttpContext.User, out var identity))
        {
            return Redirect("/login");
        }

        Email = identity.Email;
        try
        {
            await _onboarding.HandleAsync(OrganizationName, identity, cancellationToken);
            return Redirect("/start");
        }
        catch (Exception ex) when (ex is RequestValidationException or ForbiddenException or ConflictException)
        {
            Error = ex.Message;
            return Page();
        }
    }

    private static string SuggestName(string displayName, string email)
    {
        if (!string.IsNullOrWhiteSpace(displayName) && displayName.Contains(' ', StringComparison.Ordinal) == false && displayName.Contains('@', StringComparison.Ordinal) == false)
        {
            return displayName.Trim() + " org";
        }

        var at = email.IndexOf('@');
        return at > 0 ? email[..at] + " org" : "";
    }
}
