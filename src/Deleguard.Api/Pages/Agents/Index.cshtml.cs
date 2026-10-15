using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages.Agents;

public sealed class IndexModel : PageModel
{
    public const string RevealedKeyKey = "agent_api_key";
    public const string RevealedPrefixKey = "agent_key_prefix";

    private readonly AgentAdminHandler _handler;
    private readonly IAdminActionGateway _gateway;

    public IndexModel(AgentAdminHandler handler, IAdminActionGateway gateway)
    {
        _handler = handler;
        _gateway = gateway;
    }

    public IReadOnlyList<AgentListItem> Agents { get; private set; } = [];
    public IReadOnlyList<EnvironmentInfo> Environments { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool CanWrite { get; private set; }
    public string? RevealedKey { get; private set; }
    public string? RevealedPrefix { get; private set; }
    public string? PendingApprovalId { get; private set; }
    public string GuardSnippet { get; private set; } = string.Empty;

    [BindProperty]
    public string ExternalId { get; set; } = string.Empty;

    [BindProperty]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty]
    public string EnvironmentName { get; set; } = "production";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AgentsRead);
        RevealedKey = TempData[RevealedKeyKey] as string;
        RevealedPrefix = TempData[RevealedPrefixKey] as string;
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.agent.create",
                "agent",
                ExternalId,
                null,
                PermissionCatalog.AgentsWrite,
                ct => _handler.CreateAsync(new CreateAgentCommand(
                    HttpContext.RequireOrganizationId(),
                    HttpContext.RequireProductApplicationId(),
                    ExternalId,
                    DisplayName,
                    EnvironmentName), ct),
                cancellationToken);
            if (!ApplyOutcome(outcome, out var created) || created is null)
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            TempData[RevealedKeyKey] = created.ApiKey;
            TempData[RevealedPrefixKey] = created.KeyPrefix;
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

    public async Task<IActionResult> OnPostRotateAsync(string agentId, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.agent.rotate_key",
                "agent",
                agentId,
                null,
                PermissionCatalog.AgentsWrite,
                ct => _handler.RotateKeyAsync(HttpContext.RequireOrganizationId(), agentId, ct),
                cancellationToken);
            if (!ApplyOutcome(outcome, out var rotated) || rotated is null)
            {
                await LoadAsync(cancellationToken);
                return Page();
            }

            TempData[RevealedKeyKey] = rotated.ApiKey;
            TempData[RevealedPrefixKey] = rotated.KeyPrefix;
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public Task<IActionResult> OnPostDisableAsync(string agentId, CancellationToken cancellationToken) =>
        SetEnabledAsync(agentId, false, cancellationToken);

    public Task<IActionResult> OnPostEnableAsync(string agentId, CancellationToken cancellationToken) =>
        SetEnabledAsync(agentId, true, cancellationToken);

    private async Task<IActionResult> SetEnabledAsync(string agentId, bool enabled, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.AgentsWrite);
        try
        {
            await _handler.EnsureCanSetEnabledAsync(HttpContext.RequireOrganizationId(), agentId, enabled, cancellationToken);
            var outcome = await _gateway.RunAsync(
                caller,
                enabled ? "admin.agent.enable" : "admin.agent.disable",
                "agent",
                agentId,
                null,
                PermissionCatalog.AgentsWrite,
                async ct =>
                {
                    await _handler.SetEnabledAsync(HttpContext.RequireOrganizationId(), agentId, enabled, ct);
                    return true;
                },
                cancellationToken);
            if (ApplyOutcome(outcome, out _))
            {
                Message = enabled
                    ? "Agent enabled. It can call /v1/authorize with its current key."
                    : "Agent disabled. Authorize calls from this agent will fail closed.";
            }
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
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.AgentsWrite) == true;
        var appId = HttpContext.RequireProductApplicationId();
        Agents = await _handler.ListAsync(HttpContext.RequireOrganizationId(), appId, cancellationToken);
        Environments = await _handler.ListEnvironmentsAsync(HttpContext.RequireOrganizationId(), appId, cancellationToken);
        GuardSnippet =
            "{\n  \"mcpServers\": {\n    \"deleguard-guard\": {\n      \"url\": \"" +
            $"{Request.Scheme}://{Request.Host}/mcp/guard" +
            "\",\n      \"headers\": { \"Authorization\": \"Bearer " + (RevealedKey ?? "dgk_YOUR_AGENT_KEY") + "\" }\n    }\n  }\n}";
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
