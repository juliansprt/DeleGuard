using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Approvals;
using Deleguard.Domain.Approvals;
using Deleguard.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages.Approvals;

public sealed class DetailModel : PageModel
{
    private readonly IApprovalStore _approvals;
    private readonly ResolveApprovalHandler _resolver;
    private readonly IDecisionStore _decisions;
    public string? Reason { get; private set; }
    public bool CanResolve => HttpContext.GetConsoleUser()?.HasPermission("approvals.resolve") == true;

    public DetailModel(IApprovalStore approvals, ResolveApprovalHandler resolver, IDecisionStore decisions)
    {
        _approvals = approvals;
        _resolver = resolver; _decisions = decisions;
    }

    public ApprovalRequest? Approval { get; private set; }
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public string PrettyFacts { get; private set; } = string.Empty;
    public CanonicalFactsView Facts { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var user = HttpContext.RequireConsoleUser();
            var result = await _resolver.HandleAsync(new ResolveApprovalCommand(HttpContext.RequireOrganizationId(), id, user.Email, user.Roles, user.Permissions, true, null, null), cancellationToken);
            Message = $"Approved. A permit was issued for decision {result.DecisionId}.";
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDenyAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var user = HttpContext.RequireConsoleUser();
            await _resolver.HandleAsync(new ResolveApprovalCommand(HttpContext.RequireOrganizationId(), id, user.Email, user.Roles, user.Permissions, false, null, "Denied from console."), cancellationToken);
            Message = "Denied. The agent will receive DENY.";
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(id, cancellationToken);
        return Page();
    }

    private async Task LoadAsync(string id, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(Deleguard.Domain.Identity.PermissionCatalog.ApprovalsRead);
        Approval = await _approvals.GetAsync(HttpContext.RequireOrganizationId(), id, cancellationToken);
        if (Approval is not null)
        {
            var decision = await _decisions.GetAsync(HttpContext.RequireOrganizationId(), Approval.DecisionId, cancellationToken);
            Reason = decision?.Reason;
            PrettyFacts = ConsoleFormatting.PrettyJson(Approval.CanonicalFactsJson);
            Facts = CanonicalFactsView.Parse(Approval.CanonicalFactsJson);
        }
    }
}
