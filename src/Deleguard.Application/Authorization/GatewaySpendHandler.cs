using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;

namespace Deleguard.Application.Authorization;

public sealed record ReserveGatewaySpendCommand(
    AgentPrincipal Principal,
    string DecisionId,
    decimal EstimatedCostUsd);

public sealed record SettleGatewaySpendCommand(
    AgentPrincipal Principal,
    string DecisionId,
    decimal ActualCostUsd);

public sealed class GatewaySpendHandler
{
    private readonly IDecisionStore _decisions;
    private readonly IPermitStore _permits;
    private readonly ISpendLedger _spend;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public GatewaySpendHandler(
        IDecisionStore decisions,
        IPermitStore permits,
        ISpendLedger spend,
        IUnitOfWork uow,
        IClock clock)
    {
        _decisions = decisions;
        _permits = permits;
        _spend = spend;
        _uow = uow;
        _clock = clock;
    }

    public async Task<GatewaySpendView> ReserveAsync(ReserveGatewaySpendCommand command, CancellationToken cancellationToken)
    {
        if (command.EstimatedCostUsd < 0)
        {
            throw new RequestValidationException("estimated_cost_usd must be zero or positive.");
        }

        var decision = await _decisions.GetAsync(command.Principal.OrganizationId, command.DecisionId, cancellationToken)
                       ?? throw new NotFoundException("Decision not found.");
        if (!string.Equals(decision.AgentId, command.Principal.AgentId, StringComparison.Ordinal))
        {
            throw new NotFoundException("Decision not found.");
        }

        if (decision.Decision != DecisionEffect.Allow || decision.PermitId is null)
        {
            throw new ConflictException("Spend can only be reserved for an ALLOW decision with a live permit.");
        }

        var permit = await _permits.GetByDecisionAsync(command.Principal.OrganizationId, decision.Id, cancellationToken)
                     ?? throw new ConflictException("Permit not found for decision.");
        if (!permit.IsActive(_clock.UtcNow))
        {
            throw new ConflictException("Permit is expired or revoked.");
        }

        var reserved = await _spend.ReserveAsync(
            command.Principal.OrganizationId,
            command.Principal.AgentId,
            command.DecisionId,
            command.EstimatedCostUsd,
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return reserved;
    }

    public async Task<GatewaySpendView> SettleAsync(SettleGatewaySpendCommand command, CancellationToken cancellationToken)
    {
        if (command.ActualCostUsd < 0)
        {
            throw new RequestValidationException("actual_cost_usd must be zero or positive.");
        }

        var settled = await _spend.SettleAsync(
            command.Principal.OrganizationId,
            command.Principal.AgentId,
            command.DecisionId,
            command.ActualCostUsd,
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return settled;
    }

    public async Task ReleaseAsync(AgentPrincipal principal, string decisionId, CancellationToken cancellationToken)
    {
        await _spend.ReleaseAsync(principal.OrganizationId, principal.AgentId, decisionId, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
