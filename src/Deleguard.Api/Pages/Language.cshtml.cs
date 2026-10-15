using Deleguard.Api.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages;

public sealed class LanguageModel : PageModel
{
    public IActionResult OnGet() => Redirect("/");
    public IActionResult OnPost(string culture, string? returnUrl)
    {
        if (!UiText.SupportedLanguages.Contains(culture, StringComparer.Ordinal)) return BadRequest();
        Response.Cookies.Append(UiText.CookieName, culture, new CookieOptions
        {
            HttpOnly = true, IsEssential = true, Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromDays(365)
        });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
