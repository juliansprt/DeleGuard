using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages.Environments;

public sealed class IndexModel : PageModel
{
    private readonly EnvironmentAdminHandler _handler;
    private readonly IAdminActionGateway _gateway;

    public IndexModel(EnvironmentAdminHandler handler, IAdminActionGateway gateway)
    {
        _handler = handler;
        _gateway = gateway;
    }

    public IReadOnlyList<EnvironmentListItem> Environments { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool CanWrite { get; private set; }
    public string? PendingApprovalId { get; private set; }

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.EnvironmentsRead);
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.EnvironmentsWrite);
        try
        {
            var name = EnvironmentAdminHandler.NormalizeName(Name);
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.environment.create",
                "environment",
                name,
                null,
                PermissionCatalog.EnvironmentsWrite,
                ct => _handler.CreateAsync(
                    HttpContext.RequireOrganizationId(),
                    HttpContext.RequireProductApplicationId(),
                    name,
                    ct),
                cancellationToken);
            if (ApplyOutcome(outcome, out _))
            {
                return RedirectToPage();
            }
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync(string environmentId, string updatedName, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.EnvironmentsWrite);
        try
        {
            await _handler.GetAsync(HttpContext.RequireOrganizationId(), environmentId, cancellationToken);
            var name = EnvironmentAdminHandler.NormalizeName(updatedName);
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.environment.update",
                "environment",
                environmentId,
                null,
                PermissionCatalog.EnvironmentsWrite,
                ct => _handler.UpdateAsync(HttpContext.RequireOrganizationId(), environmentId, name, ct),
                cancellationToken);
            if (ApplyOutcome(outcome, out _))
            {
                Message = "Environment renamed. Authorize clients must send the new name.";
            }
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or NotFoundException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string environmentId, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.EnvironmentsWrite);
        try
        {
            await _handler.EnsureCanDeleteAsync(HttpContext.RequireOrganizationId(), environmentId, cancellationToken);
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.environment.delete",
                "environment",
                environmentId,
                null,
                PermissionCatalog.EnvironmentsWrite,
                async ct =>
                {
                    await _handler.DeleteAsync(HttpContext.RequireOrganizationId(), environmentId, ct);
                    return true;
                },
                cancellationToken);
            if (ApplyOutcome(outcome, out _))
            {
                Message = "Environment deleted.";
            }
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or NotFoundException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.EnvironmentsWrite) == true;
        Environments = await _handler.ListAsync(
            HttpContext.RequireOrganizationId(),
            HttpContext.RequireProductApplicationId(),
            cancellationToken);
    }

    private bool ApplyOutcome<T>(AdminActionOutcome<T> outcome, out T? value)
    {
        value = default;
        if (outcome.Allowed)
        {
            value = outcome.Value;
            return true;
        }

        PendingApprovalId = outcome.Pending ? outcome.ApprovalId : null;
        IsError = !outcome.Pending;
        Message = outcome.Pending ? "Waiting for approval." : outcome.Reason ?? "Denied by policy.";
        return false;
    }
}
