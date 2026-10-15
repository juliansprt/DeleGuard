using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Identity;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages.Access;

public sealed class McpModel : PageModel
{
    public const string RevealedTokenKey = "mcp_token";
    public const string RevealedPrefixKey = "mcp_prefix";

    private readonly McpAdminHandler _mcp;
    private readonly UserAdminHandler _users;
    private readonly IAdminActionGateway _gateway;

    public McpModel(McpAdminHandler mcp, UserAdminHandler users, IAdminActionGateway gateway)
    {
        _mcp = mcp;
        _users = users;
        _gateway = gateway;
    }

    [BindProperty]
    public bool Enabled { get; set; }
    public IReadOnlyList<string> EnabledTools { get; private set; } = [];
    public IReadOnlyList<string> AvailableTools { get; private set; } = AdminMcpCatalog.ToolNames;
    public IReadOnlyList<McpCredentialListItem> Credentials { get; private set; } = [];
    public IReadOnlyList<UserListItem> Users { get; private set; } = [];
    public string McpUrl { get; private set; } = "/mcp";
    public string Snippet { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool CanWrite { get; private set; }
    public string? RevealedToken { get; private set; }
    public string? RevealedPrefix { get; private set; }

    [BindProperty]
    public List<string> SelectedTools { get; set; } = [];

    [BindProperty]
    public string IssueUserId { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.McpRead);
        RevealedToken = TempData[RevealedTokenKey] as string;
        RevealedPrefix = TempData[RevealedPrefixKey] as string;
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.McpWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.mcp.set_tools",
                "mcp",
                "settings",
                null,
                PermissionCatalog.McpWrite,
                ct => _mcp.UpdateSettingsAsync(HttpContext.RequireOrganizationId(), Enabled, SelectedTools, ct),
                cancellationToken);
            Message = outcome.Allowed ? "MCP settings saved." : (outcome.Pending ? $"Waiting for approval {outcome.ApprovalId}." : outcome.Reason);
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

    public async Task<IActionResult> OnPostIssueAsync(CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.McpWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.mcp.issue_token",
                "mcp",
                IssueUserId,
                null,
                PermissionCatalog.McpWrite,
                ct => _mcp.IssueTokenAsync(HttpContext.RequireOrganizationId(), IssueUserId, null, ct),
                cancellationToken);
            if (outcome is { Allowed: true, Value: not null })
            {
                TempData[RevealedTokenKey] = outcome.Value.Token;
                TempData[RevealedPrefixKey] = outcome.Value.KeyPrefix;
                return RedirectToPage();
            }

            Message = outcome.Pending ? $"Waiting for approval {outcome.ApprovalId}." : outcome.Reason;
            IsError = !outcome.Pending;
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(string credentialId, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.McpWrite);
        try
        {
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.mcp.revoke_token",
                "mcp",
                credentialId,
                null,
                PermissionCatalog.McpWrite,
                async ct =>
                {
                    await _mcp.RevokeTokenAsync(HttpContext.RequireOrganizationId(), credentialId, ct);
                    return true;
                },
                cancellationToken);
            Message = outcome.Allowed ? "Token revoked." : (outcome.Pending ? $"Waiting for approval {outcome.ApprovalId}." : outcome.Reason);
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
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.McpWrite) == true;
        var settings = await _mcp.GetSettingsAsync(HttpContext.RequireOrganizationId(), cancellationToken);
        Enabled = settings.Enabled;
        EnabledTools = settings.EnabledTools;
        AvailableTools = settings.AvailableTools;
        Credentials = await _mcp.ListCredentialsAsync(HttpContext.RequireOrganizationId(), cancellationToken);
        Users = await _users.ListAsync(HttpContext.RequireOrganizationId(), cancellationToken);
        McpUrl = $"{Request.Scheme}://{Request.Host}/mcp";
        var tokenHint = RevealedToken ?? "dgm_YOUR_TOKEN";
        Snippet =
            "{\n  \"mcpServers\": {\n    \"deleguard-admin\": {\n      \"type\": \"streamable-http\",\n      \"url\": \"" + McpUrl +
            "\",\n      \"headers\": {\n        \"Authorization\": \"Bearer " + tokenHint + "\"\n      }\n    }\n  }\n}";
    }
}
