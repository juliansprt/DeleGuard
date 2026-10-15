using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;
using Deleguard.Application.Audit;
using Deleguard.Application.Authorization;
using Deleguard.Application.Identity;
using Deleguard.Application.Policies;
using Deleguard.Domain.Audit;
using Deleguard.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Deleguard.Api.Pages;

public sealed class IndexModel : PageModel
{
    private readonly AgentAdminHandler _agents;
    private readonly ActionAdminHandler _actions;
    private readonly PolicyAdminHandler _policies;
    private readonly ListAuditEventsHandler _audit;
    private readonly IControlQueries _queries;
    private readonly IClock _clock;

    public IndexModel(
        AgentAdminHandler agents,
        ActionAdminHandler actions,
        PolicyAdminHandler policies,
        ListAuditEventsHandler audit, IControlQueries queries, IClock clock)
    {
        _agents = agents;
        _actions = actions;
        _policies = policies;
        _audit = audit; _queries = queries; _clock = clock;
    }

    public ControlSummary? Summary { get; private set; }
    public int CoverageGaps { get; private set; }
    public bool HasReadyIntegration { get; private set; }
    public bool CanRead(string permission) => HttpContext.GetConsoleUser()?.HasPermission(permission) == true;
    public bool IsSignedIn { get; private set; }
    public int AgentCount { get; private set; }
    public int PendingApprovals { get; private set; }
    public int ChecksToday { get; private set; }
    public int DenialsToday { get; private set; }
    public int PublishedPolicies { get; private set; }
    public int HighRiskActions { get; private set; }
    public bool HasAgent { get; private set; }
    public bool HasAction { get; private set; }
    public bool HasPublishedPolicy { get; private set; }
    public IReadOnlyList<AuditEvent> RecentEvents { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        IsSignedIn = HttpContext.IsConsole();
        if (!IsSignedIn)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return Redirect("/login");
            }

            return Page();
        }

        var org = HttpContext.RequireOrganizationId();
        var appId = HttpContext.RequireProductApplicationId();
        var agents = await _agents.ListAsync(org, appId, cancellationToken);
        var actions = await _actions.ListAsync(org, appId, cancellationToken);
        var policies = await _policies.ListAsync(org, appId, cancellationToken);
        Summary = await _queries.SummaryAsync(org, appId, _clock.UtcNow, cancellationToken);
        if (CanRead(PermissionCatalog.ActionsRead) && CanRead(PermissionCatalog.PoliciesRead) && CanRead(PermissionCatalog.AgentsRead))
        {
            var coverage = await _queries.CoverageAsync(org, appId, null, cancellationToken);
            CoverageGaps = coverage.Count(r => r.Status != "Ready to evaluate");
            HasReadyIntegration = coverage.Any(r => r.AgentEnabled && r.ActionAssigned && r.Policies.Count > 0);
        }
        var events = await _audit.HandleAsync(org, 8, cancellationToken, applicationId: appId);

        AgentCount = agents.Count;
        PendingApprovals = Summary.PendingApprovals;
        PublishedPolicies = policies.Count(p => p.PublishedVersionNumber is not null);
        var productActions = actions.Where(a => !ActionAdminHandler.IsAdministration(a.Name)).ToList();
        HighRiskActions = productActions.Count(a => a.Risk is RiskLevel.High or RiskLevel.Critical);
        ChecksToday = Summary.ChecksToday;
        DenialsToday = Summary.DenialsToday;
        HasAgent = AgentCount > 0;
        HasAction = productActions.Count > 0;
        HasPublishedPolicy = PublishedPolicies > 0;
        RecentEvents = events.Take(8).ToList();
        return Page();
    }
}
