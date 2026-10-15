using Deleguard.Application.Abstractions;
using Deleguard.Domain.Audit;

namespace Deleguard.Application.Audit;

public sealed class ListAuditEventsHandler
{
    private readonly IAuditLedger _audit;

    public ListAuditEventsHandler(IAuditLedger audit)
    {
        _audit = audit;
    }

    public Task<IReadOnlyList<AuditEvent>> HandleAsync(
        string organizationId,
        int take,
        CancellationToken cancellationToken,
        string? eventType = null,
        string? q = null,
        string? applicationId = null) =>
        _audit.ListAsync(organizationId, take, cancellationToken, eventType, q, applicationId);
}
