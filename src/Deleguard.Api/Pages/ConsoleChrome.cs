using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Approvals;

namespace Deleguard.Api.Pages;

public sealed class ConsoleChrome
{
    private readonly ListApprovalsHandler _approvals;
    private readonly IHttpContextAccessor _http;
    private int? _pending;

    public ConsoleChrome(ListApprovalsHandler approvals, IHttpContextAccessor http)
    {
        _approvals = approvals;
        _http = http;
    }

    public IReadOnlyList<ApplicationListItem> ProductApplications =>
        _http.HttpContext?.GetProductApplications() ?? [];

    public ApplicationListItem? CurrentApplication =>
        _http.HttpContext?.GetProductApplication();

    public async Task<int> PendingApprovalsAsync(CancellationToken cancellationToken)
    {
        if (_pending is not null)
        {
            return _pending.Value;
        }

        var org = _http.HttpContext?.GetConsoleUser()?.OrganizationId;
        if (string.IsNullOrWhiteSpace(org))
        {
            _pending = 0;
            return 0;
        }

        var items = await _approvals.HandleAsync(org, cancellationToken);
        _pending = items.Count;
        return _pending.Value;
    }
}
