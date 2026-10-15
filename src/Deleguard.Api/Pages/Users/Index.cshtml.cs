using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages.Users;

public sealed class IndexModel : PageModel
{
    public const string RevealedPasswordKey = "user_password";
    public const string RevealedEmailKey = "user_email";

    private readonly UserAdminHandler _users;
    private readonly RoleAdminHandler _roles;
    private readonly IAdminActionGateway _gateway;

    public IndexModel(UserAdminHandler users, RoleAdminHandler roles, IAdminActionGateway gateway)
    {
        _users = users;
        _roles = roles;
        _gateway = gateway;
    }

    public IReadOnlyList<UserListItem> Users { get; private set; } = [];
    public IReadOnlyList<RoleListItem> Roles { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool CanWrite { get; private set; }
    public string? RevealedPassword { get; private set; }
    public string? RevealedEmail { get; private set; }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty]
    public List<string> SelectedRoles { get; set; } = [];

    [BindProperty]
    public string AssignUserId { get; set; } = string.Empty;

    [BindProperty]
    public List<string> AssignRoles { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.UsersRead);
        RevealedPassword = TempData[RevealedPasswordKey] as string;
        RevealedEmail = TempData[RevealedEmailKey] as string;
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.UsersWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.user.create",
                "user",
                Email,
                null,
                PermissionCatalog.UsersWrite,
                ct => _users.CreateAsync(HttpContext.RequireOrganizationId(), Email, DisplayName, SelectedRoles, null, ct),
                cancellationToken);
            if (!outcome.Allowed)
            {
                IsError = !outcome.Pending;
                Message = Describe(outcome);
                await LoadAsync(cancellationToken);
                return Page();
            }

            TempData[RevealedPasswordKey] = outcome.Value!.TemporaryPassword;
            TempData[RevealedEmailKey] = outcome.Value.Email;
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDisableAsync(string userId, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.UsersWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.user.disable",
                "user",
                userId,
                null,
                PermissionCatalog.UsersWrite,
                async ct =>
                {
                    await _users.DisableAsync(HttpContext.RequireOrganizationId(), userId, ct);
                    return true;
                },
                cancellationToken);
            Message = outcome.Allowed ? "User disabled." : Describe(outcome);
            IsError = !outcome.Allowed && !outcome.Pending;
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAssignAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.UsersWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.role.assign",
                "role",
                AssignUserId,
                null,
                PermissionCatalog.UsersWrite,
                async ct =>
                {
                    await _users.AssignRolesAsync(HttpContext.RequireOrganizationId(), AssignUserId, AssignRoles, ct);
                    return true;
                },
                cancellationToken);
            Message = outcome.Allowed ? "Roles updated." : Describe(outcome);
            IsError = !outcome.Allowed && !outcome.Pending;
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.UsersWrite) == true;
        Users = await _users.ListAsync(HttpContext.RequireOrganizationId(), cancellationToken);
        Roles = await _roles.ListAsync(HttpContext.RequireOrganizationId(), cancellationToken);
    }

    private static string Describe<T>(AdminActionOutcome<T> outcome)
    {
        if (outcome.Pending)
        {
            return $"Waiting for approval {outcome.ApprovalId}.";
        }

        return outcome.Reason ?? "Denied by policy.";
    }
}
