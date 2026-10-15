using Deleguard.Domain.Common;

namespace Deleguard.Domain.Evidence;

public sealed class Permit
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string DecisionId { get; init; }
    public required string Jti { get; init; }
    public required string Action { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public string? ResourceSnapshotHash { get; init; }
    public string? OperationId { get; init; }
    public string? OperationHash { get; init; }
    public int? PolicyVersion { get; init; }
    public required string Token { get; set; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && ExpiresAt > now;
}

public sealed class ExecutionEvidence
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string DecisionId { get; init; }
    public string? PermitId { get; init; }
    public required ExecutionStatus Status { get; init; }
    public string? ProviderReference { get; init; }
    public string? ResultHash { get; init; }
    public string? ResourceSnapshotHash { get; init; }
    public required string MetadataJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
