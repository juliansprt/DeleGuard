using Deleguard.Api.Http;
using Deleguard.Application.Approvals;
using Deleguard.Domain.Approvals;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages.Approvals;

public sealed class IndexModel : PageModel
{
    private readonly ListApprovalsHandler _handler;

    public IndexModel(ListApprovalsHandler handler)
    {
        _handler = handler;
    }

    public IReadOnlyList<ApprovalRequest> Items { get; private set; } = [];
    [BindProperty(SupportsGet=true)]public string? Q {get;set;}
    [BindProperty(SupportsGet=true)]public bool Expiring {get;set;}
    public int Total {get;private set;}

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(Deleguard.Domain.Identity.PermissionCatalog.ApprovalsRead);
        Items = await _handler.HandleAsync(
            HttpContext.RequireOrganizationId(),
            cancellationToken,
            HttpContext.RequireProductApplicationId());
        Total=Items.Count;
        var query=Items.AsEnumerable();
        Q=Q?.Trim();if(Q?.Length>200)Q=Q[..200];
        if(!string.IsNullOrWhiteSpace(Q))query=query.Where(i=>i.CanonicalFactsJson.Contains(Q,StringComparison.OrdinalIgnoreCase)||i.Id.Contains(Q,StringComparison.OrdinalIgnoreCase));
        if(Expiring)query=query.Where(i=>i.ExpiresAt<=DateTimeOffset.UtcNow.AddMinutes(15));
        Items=query.OrderBy(i=>i.ExpiresAt).ToArray();
    }
}
