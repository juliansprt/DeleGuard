using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Notifications;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Webhooks;

public sealed class DetailModel : PageModel
{
    public const string RevealedSecretKey = "webhook_secret";
    public const string RevealedPrefixKey = "webhook_secret_prefix";

    private readonly WebhookAdminHandler _handler;
    private readonly IAdminActionGateway _gateway;

    public DetailModel(WebhookAdminHandler handler, IAdminActionGateway gateway)
    {
        _handler = handler;
        _gateway = gateway;
    }

    public WebhookEndpointListItem? Endpoint { get; private set; }
    public IReadOnlyList<WebhookAttemptItem> Attempts { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public string? RevealedSecret { get; private set; }
    public string? RevealedPrefix { get; private set; }
    public bool CanWrite { get; private set; }
    public string? PendingApprovalId { get; private set; }

    [BindProperty]
    public DateTimeOffset? RecoverSince { get; set; }
    [BindProperty]
    public DateTimeOffset? RecoverUntil { get; set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        RecoverUntil = DateTimeOffset.UtcNow;
        RecoverSince = RecoverUntil.Value.AddDays(-1);
        HttpContext.EnsurePermission(PermissionCatalog.WebhooksRead);
        RevealedSecret = TempData[RevealedSecretKey] as string;
        RevealedPrefix = TempData[RevealedPrefixKey] as string;
        try
        {
            await LoadAsync(id, cancellationToken);
            return Page();
        }
        catch (NotFoundException)
        {
            Endpoint = null;
            return Page();
        }
    }

    public Task<IActionResult> OnPostPingAsync(string id, CancellationToken cancellationToken) =>
        RunAsync(id, "admin.webhook.ping", "Ping sent.",
            async ct =>
            {
                await _handler.PingAsync(HttpContext.RequireOrganizationId(), id, ct);
                return true;
            }, cancellationToken);

    public async Task<IActionResult> OnPostRotateAsync(string id, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.WebhooksWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.webhook.rotate_secret",
                "webhook",
                id,
                null,
                PermissionCatalog.WebhooksWrite,
                ct => _handler.RotateSecretAsync(HttpContext.RequireOrganizationId(), id, ct),
                cancellationToken);
            if (ApplyOutcome(outcome, out var rotated) && rotated is not null)
            {
                TempData[RevealedSecretKey] = rotated.Secret;
                TempData[RevealedPrefixKey] = rotated.SecretPrefix;
                return RedirectToPage(new { id });
            }
        }
        catch (Exception ex) when (ex is RequestValidationException or NotFoundException or ConflictException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await SafeLoadAsync(id, cancellationToken);
        return Page();
    }

    public Task<IActionResult> OnPostDisableAsync(string id, CancellationToken cancellationToken) =>
        RunAsync(id, "admin.webhook.update", "Endpoint disabled.",
            ct => _handler.UpdateAsync(new UpdateWebhookCommand(HttpContext.RequireOrganizationId(), id, null, null, null, "disabled"), ct),
            cancellationToken);

    public Task<IActionResult> OnPostEnableAsync(string id, CancellationToken cancellationToken) =>
        RunAsync(id, "admin.webhook.update", "Endpoint enabled.",
            ct => _handler.UpdateAsync(new UpdateWebhookCommand(HttpContext.RequireOrganizationId(), id, null, null, null, "enabled"), ct),
            cancellationToken);

    public Task<IActionResult> OnPostResendAsync(string id, string messageId, CancellationToken cancellationToken) =>
        RunAsync(id, "admin.webhook.resend", "Message queued for resend.",
            async ct =>
            {
                await _handler.ResendAsync(HttpContext.RequireOrganizationId(), id, messageId, ct);
                return true;
            }, cancellationToken);

    public async Task<IActionResult> OnPostRecoverAsync(string id, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.WebhooksWrite);
        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            var since = WebhookAdminHandler.ParseRecoveryInstant(form[nameof(RecoverSince)].ToString(), "since");
            var until = WebhookAdminHandler.ParseRecoveryInstant(form[nameof(RecoverUntil)].ToString(), "until");
            WebhookAdminHandler.ValidateRecoveryRange(since, until);
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.webhook.recover",
                "webhook",
                id,
                null,
                PermissionCatalog.WebhooksWrite,
                ct => _handler.RecoverAsync(HttpContext.RequireOrganizationId(), id, since, until, ct),
                cancellationToken);
            if (ApplyOutcome(outcome, out var count))
            {
                Message = $"Requeued {count} failed messages.";
            }
        }
        catch (Exception ex) when (ex is RequestValidationException or NotFoundException or ConflictException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await SafeLoadAsync(id, cancellationToken);
        return Page();
    }

    private async Task<IActionResult> RunAsync<T>(
        string id,
        string action,
        string success,
        Func<CancellationToken, Task<T>> execute,
        CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.WebhooksWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                action,
                "webhook",
                id,
                null,
                PermissionCatalog.WebhooksWrite,
                execute,
                cancellationToken);
            if (ApplyOutcome(outcome, out _))
            {
                TempData["flash"] = success;
                return RedirectToPage(new { id });
            }
        }
        catch (Exception ex) when (ex is RequestValidationException or NotFoundException or ConflictException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await SafeLoadAsync(id, cancellationToken);
        return Page();
    }

    private async Task LoadAsync(string id, CancellationToken cancellationToken)
    {
        Endpoint = await _handler.GetAsync(HttpContext.RequireOrganizationId(), id, cancellationToken);
        Attempts = await _handler.ListAttemptsAsync(HttpContext.RequireOrganizationId(), id, cancellationToken);
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.WebhooksWrite) == true;
        if (TempData["flash"] is string flash && string.IsNullOrWhiteSpace(Message))
        {
            Message = flash;
        }
    }

    private async Task SafeLoadAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            await LoadAsync(id, cancellationToken);
        }
        catch (NotFoundException)
        {
            Endpoint = null;
        }
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
