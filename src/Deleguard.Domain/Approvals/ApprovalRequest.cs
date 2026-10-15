using Deleguard.Domain.Common;

namespace Deleguard.Domain.Approvals;

public sealed class ApprovalRequest
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string DecisionId { get; init; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public required DateTimeOffset ExpiresAt { get; set; }
    public required string ApproverRolesJson { get; init; }
    public required string CanonicalFactsJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
}

public sealed class ApprovalEvent
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string ApprovalRequestId { get; init; }
    public required string Action { get; init; }
    public required string Actor { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
