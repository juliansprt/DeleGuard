using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Notifications;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Notifications;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Webhooks;

public sealed class IndexModel : PageModel
{
    public const string RevealedSecretKey = "webhook_secret";
    public const string RevealedPrefixKey = "webhook_secret_prefix";

    private readonly WebhookAdminHandler _handler;
    private readonly IAdminActionGateway _gateway;

    public IndexModel(WebhookAdminHandler handler, IAdminActionGateway gateway)
    {
        _handler = handler;
        _gateway = gateway;
    }

    public IReadOnlyList<WebhookEndpointListItem> Endpoints { get; private set; } = [];
    public IReadOnlyList<string> Catalog { get; } = WebhookEventTypes.Subscribable;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public string? RevealedSecret { get; private set; }
    public string? RevealedPrefix { get; private set; }
    public bool CanWrite { get; private set; }
    public string? PendingApprovalId { get; private set; }
    public int TotalCount { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public string Description { get; set; } = string.Empty;

    [BindProperty]
    public string EndpointUrl { get; set; } = string.Empty;

    [BindProperty]
    public List<string> SelectedEventTypes { get; set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.WebhooksRead);
        RevealedSecret = TempData[RevealedSecretKey] as string;
        RevealedPrefix = TempData[RevealedPrefixKey] as string;
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.WebhooksWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.webhook.create",
                "webhook",
                EndpointUrl,
                null,
                PermissionCatalog.WebhooksWrite,
                ct => _handler.CreateAsync(new CreateWebhookCommand(
                    HttpContext.RequireOrganizationId(),
                    HttpContext.RequireProductApplicationId(),
                    Description,
                    EndpointUrl,
                    SelectedEventTypes), ct),
                cancellationToken);
            if (ApplyOutcome(outcome, out var created) && created is not null)
            {
                TempData[RevealedSecretKey] = created.Secret;
                TempData[RevealedPrefixKey] = created.SecretPrefix;
                return RedirectToPage("Detail", new { id = created.EndpointId });
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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.WebhooksWrite) == true;
        var all = await _handler.ListAsync(
            HttpContext.RequireOrganizationId(),
            HttpContext.RequireProductApplicationId(),
            cancellationToken);
        TotalCount = all.Count;
        var query = Q?.Trim();
        Endpoints = string.IsNullOrEmpty(query)
            ? all
            : all.Where(item =>
                    item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Url.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.EventTypes.Any(type => type.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToList();
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
