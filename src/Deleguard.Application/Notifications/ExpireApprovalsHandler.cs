using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Approvals;
using Deleguard.Domain.Common;

namespace Deleguard.Application.Notifications;

public sealed class ExpireApprovalsHandler
{
    private readonly IApprovalStore _approvals;
    private readonly IDecisionStore _decisions;
    private readonly IAuditLedger _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public ExpireApprovalsHandler(
        IApprovalStore approvals,
        IDecisionStore decisions,
        IAuditLedger audit,
        IOutbox outbox,
        IUnitOfWork uow,
        IClock clock)
    {
        _approvals = approvals;
        _decisions = decisions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _clock = clock;
    }

    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var expired = await _approvals.ExpireDueAsync(now, cancellationToken);
        foreach (var request in expired)
        {
            await _approvals.AddEventAsync(new ApprovalEvent
            {
                Id = IdGenerator.New("aev"),
                OrganizationId = request.OrganizationId,
                ApprovalRequestId = request.Id,
                Action = "EXPIRE",
                Actor = "system",
                CreatedAt = now
            }, cancellationToken);

            var decision = await _decisions.GetAsync(request.OrganizationId, request.DecisionId, cancellationToken);
            var payload = JsonSerializer.SerializeToElement(new
            {
                approval_id = request.Id,
                decision_id = request.DecisionId,
                application_id = decision?.ApplicationId,
                action = "EXPIRE"
            });
            await _audit.AppendAsync(request.OrganizationId, "approval.expired", request.DecisionId, payload, cancellationToken);
            await _outbox.EnqueueAsync(request.OrganizationId, "approval.expired", payload, cancellationToken);
        }

        if (expired.Count > 0)
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }

        return expired.Count;
    }
}
