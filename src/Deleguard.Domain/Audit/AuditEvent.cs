namespace Deleguard.Domain.Audit;

public sealed class AuditEvent
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public string? ApplicationId { get; init; }
    public required string EventType { get; init; }
    public string? DecisionId { get; init; }
    public required string PayloadJson { get; init; }
    public required string PreviousEventHash { get; init; }
    public required string EventHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class OutboxEvent
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string EventType { get; init; }
    public required string PayloadJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class TenantAuditHead
{
    public required string OrganizationId { get; init; }
    public required string LastEventHash { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
