using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;

namespace Deleguard.Api.Http;

public static class ApplicationContextExtensions
{
    public const string CookieName = "dg_application";
    public const string ItemKey = "deleguard.application";
    public const string ListItemKey = "deleguard.applications";

    public static ApplicationListItem? GetProductApplication(this HttpContext http) =>
        http.Items.TryGetValue(ItemKey, out var value) ? value as ApplicationListItem : null;

    public static IReadOnlyList<ApplicationListItem> GetProductApplications(this HttpContext http) =>
        http.Items.TryGetValue(ListItemKey, out var value) && value is IReadOnlyList<ApplicationListItem> list
            ? list
            : [];

    public static string RequireProductApplicationId(this HttpContext http) =>
        http.GetProductApplication()?.Id
        ?? throw new InvalidOperationException("Product application context is not available.");
}

public sealed class ProductApplicationMiddleware
{
    private readonly RequestDelegate _next;

    public ProductApplicationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext http, IApplicationDirectory applications)
    {
        var user = http.GetConsoleUser();
        if (user is not null)
        {
            var products = await applications.ListProductAsync(user.OrganizationId, http.RequestAborted);
            http.Items[ApplicationContextExtensions.ListItemKey] = products;
            var requested = http.Request.Cookies[ApplicationContextExtensions.CookieName];
            var current = products.FirstOrDefault(item => string.Equals(item.Id, requested, StringComparison.Ordinal))
                          ?? products.FirstOrDefault();
            if (current is not null)
            {
                http.Items[ApplicationContextExtensions.ItemKey] = current;
                if (!string.Equals(requested, current.Id, StringComparison.Ordinal))
                {
                    http.Response.Cookies.Append(
                        ApplicationContextExtensions.CookieName,
                        current.Id,
                        new CookieOptions
                        {
                            HttpOnly = true,
                            IsEssential = true,
                            SameSite = SameSiteMode.Lax,
                            Path = "/"
                        });
                }
            }
        }

        await _next(http);
    }
}
