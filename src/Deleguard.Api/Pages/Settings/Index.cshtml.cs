using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Settings;

public sealed class IndexModel : PageModel
{
    private readonly OrganizationAdminHandler _organizations;
    private readonly ApplicationAdminHandler _applications;
    private readonly IAdminActionGateway _gateway;

    public IndexModel(
        OrganizationAdminHandler organizations,
        ApplicationAdminHandler applications,
        IAdminActionGateway gateway)
    {
        _organizations = organizations;
        _applications = applications;
        _gateway = gateway;
    }

    public OrganizationView? Organization { get; private set; }
    public IReadOnlyList<ApplicationListItem> Applications { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool CanRename { get; private set; }
    public bool CanWriteApps { get; private set; }
    public string? PendingApprovalId { get; private set; }

    [BindProperty]
    public string OrganizationName { get; set; } = string.Empty;

    [BindProperty]
    public string ApplicationName { get; set; } = string.Empty;

    [BindProperty]
    public string ApplicationSlug { get; set; } = string.Empty;

    [BindProperty]
    public string ApplicationId { get; set; } = string.Empty;

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.ApplicationsRead) != true
            && HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.OrganizationWrite) != true)
        {
            throw new ForbiddenException($"Missing permission '{PermissionCatalog.ApplicationsRead}'.");
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public IActionResult OnPostSelect()
    {
        HttpContext.EnsureConsole();
        if (!string.IsNullOrWhiteSpace(ApplicationId))
        {
            HttpContext.Response.Cookies.Append(
                ApplicationContextExtensions.CookieName,
                ApplicationId,
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Path = "/"
                });
        }

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && ReturnUrl.StartsWith('/'))
        {
            return Redirect(ReturnUrl);
        }

        return Redirect("/settings");
    }

    public async Task<IActionResult> OnPostRenameAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.OrganizationWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.organization.update",
                "organization",
                caller.OrganizationId,
                null,
                PermissionCatalog.OrganizationWrite,
                ct => _organizations.UpdateNameAsync(caller.OrganizationId, OrganizationName, ct),
                cancellationToken);
            if (outcome.Allowed)
            {
                return RedirectToPage();
            }

            PendingApprovalId = outcome.Pending ? outcome.ApprovalId : null;
            IsError = !outcome.Pending;
            Message = outcome.Pending ? "Waiting for approval." : outcome.Reason ?? "Denied by policy.";
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateApplicationAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.ApplicationsWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.application.create",
                "application",
                ApplicationSlug,
                null,
                PermissionCatalog.ApplicationsWrite,
                ct => _applications.CreateAsync(caller.OrganizationId, ApplicationName, ApplicationSlug, ct),
                cancellationToken);
            if (outcome.Allowed && outcome.Value is not null)
            {
                HttpContext.Response.Cookies.Append(
                    ApplicationContextExtensions.CookieName,
                    outcome.Value.Id,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax,
                        Path = "/"
                    });
                return RedirectToPage();
            }

            PendingApprovalId = outcome.Pending ? outcome.ApprovalId : null;
            IsError = !outcome.Pending;
            Message = outcome.Pending ? "Waiting for approval." : outcome.Reason ?? "Denied by policy.";
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateApplicationAsync(
        string applicationId,
        string updatedName,
        string updatedSlug,
        CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.ApplicationsWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.application.update",
                "application",
                applicationId,
                null,
                PermissionCatalog.ApplicationsWrite,
                ct => _applications.UpdateAsync(caller.OrganizationId, applicationId, updatedName, updatedSlug, ct),
                cancellationToken);
            if (outcome.Allowed)
            {
                return RedirectToPage();
            }

            PendingApprovalId = outcome.Pending ? outcome.ApprovalId : null;
            IsError = !outcome.Pending;
            Message = outcome.Pending ? "Waiting for approval." : outcome.Reason ?? "Denied by policy.";
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or ForbiddenException or NotFoundException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteApplicationAsync(string applicationId, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.ApplicationsWrite);
        try
        {
            await _applications.EnsureCanDeleteAsync(caller.OrganizationId, applicationId, cancellationToken);
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.application.delete",
                "application",
                applicationId,
                null,
                PermissionCatalog.ApplicationsWrite,
                async ct =>
                {
                    await _applications.DeleteAsync(caller.OrganizationId, applicationId, ct);
                    return true;
                },
                cancellationToken);
            if (outcome.Allowed)
            {
                return RedirectToPage();
            }

            PendingApprovalId = outcome.Pending ? outcome.ApprovalId : null;
            IsError = !outcome.Pending;
            Message = outcome.Pending ? "Waiting for approval." : outcome.Reason ?? "Denied by policy.";
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or ForbiddenException or NotFoundException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var org = HttpContext.RequireOrganizationId();
        Organization = await _organizations.GetAsync(org, cancellationToken);
        OrganizationName = Organization.Name;
        Applications = await _applications.ListProductAsync(org, cancellationToken);
        CanRename = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.OrganizationWrite) == true;
        CanWriteApps = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.ApplicationsWrite) == true;
    }
}
