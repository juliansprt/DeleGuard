using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages.Roles;

public sealed class IndexModel : PageModel
{
    private readonly RoleAdminHandler _roles;
    private readonly IAdminActionGateway _gateway;

    public IndexModel(RoleAdminHandler roles, IAdminActionGateway gateway)
    {
        _roles = roles;
        _gateway = gateway;
    }

    public IReadOnlyList<RoleListItem> Roles { get; private set; } = [];
    public IReadOnlyList<(string Name, string Description)> Catalog { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool CanWrite { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.RolesRead);
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(string roleId, List<string> selected, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.RolesWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.role.assign",
                "role",
                roleId,
                null,
                PermissionCatalog.RolesWrite,
                async ct =>
                {
                    await _roles.SetPermissionsAsync(HttpContext.RequireOrganizationId(), roleId, selected ?? [], ct);
                    return true;
                },
                cancellationToken);
            Message = outcome.Allowed ? "Permissions saved." : (outcome.Pending ? $"Waiting for approval {outcome.ApprovalId}." : outcome.Reason);
            IsError = !outcome.Allowed && !outcome.Pending;
        }
        catch (Exception ex) when (ex is RequestValidationException or ForbiddenException or NotFoundException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.RolesWrite) == true;
        Roles = await _roles.ListAsync(HttpContext.RequireOrganizationId(), cancellationToken);
        Catalog = _roles.Catalog();
    }
}
