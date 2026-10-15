using Deleguard.Api.Http;
using Deleguard.Application.Audit;
using Deleguard.Domain.Audit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages.Audit;

public sealed class IndexModel : PageModel
{
    public const string TypeAll = "all";
    public const string TypeDecisions = "decisions";
    public const string TypeApprovals = "approvals";
    public const string TypeExecutions = "executions";

    private readonly ListAuditEventsHandler _handler;

    public IndexModel(ListAuditEventsHandler handler)
    {
        _handler = handler;
    }

    public IReadOnlyList<AuditEvent> Events { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = TypeAll;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(Deleguard.Domain.Identity.PermissionCatalog.AuditRead);
        Events = await _handler.HandleAsync(
            HttpContext.RequireOrganizationId(),
            100,
            cancellationToken,
            EventTypeFilter(Type),
            string.IsNullOrWhiteSpace(Q) ? null : Q.Trim(),
            HttpContext.RequireProductApplicationId());
    }

    public string TabHref(string type)
    {
        var query = new List<string>();
        if (!string.Equals(type, TypeAll, StringComparison.OrdinalIgnoreCase))
        {
            query.Add("type=" + Uri.EscapeDataString(type));
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            query.Add("q=" + Uri.EscapeDataString(Q.Trim()));
        }

        return query.Count == 0 ? "/audit" : "/audit?" + string.Join("&", query);
    }

    public bool IsType(string type) =>
        string.Equals(NormalizeType(Type), type, StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        TypeDecisions => TypeDecisions,
        TypeApprovals => TypeApprovals,
        TypeExecutions => TypeExecutions,
        _ => TypeAll
    };

    internal static string? EventTypeFilter(string? type) => NormalizeType(type) switch
    {
        TypeDecisions => "authorization.decided",
        TypeApprovals => "approval",
        TypeExecutions => "execution.reported",
        _ => null
    };
}
