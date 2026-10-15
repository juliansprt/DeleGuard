namespace Deleguard.Domain.Evidence;

/// <summary>A consumed execution claim is never automatically released or replayed.</summary>
public sealed class OperationExecution
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string EnvironmentId { get; init; }
    public required string AgentId { get; init; }
    public required string DecisionId { get; init; }
    public required string PermitId { get; init; }
    public required string OperationId { get; init; }
    public required string OperationHash { get; init; }
    public required string ResourceSnapshotHash { get; init; }
    public string State { get; set; } = "UNKNOWN";
    public string Verification { get; set; } = "UNVERIFIED";
    public string? ReportDigest { get; set; }
    public string? ProviderReference { get; set; }
    public string? ResultHash { get; set; }
    public DateTimeOffset ConsumedAt { get; init; }
    public DateTimeOffset ExecuteBefore { get; init; }
    public DateTimeOffset? ReportedAt { get; set; }
    public string? AssignedTo { get; set; }
    public string? Resolution { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? AlertedAt { get; set; }
}
