using Deleguard.Domain.Common;

namespace Deleguard.Domain.Authorization;

public sealed class AuthorizationDecision
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string EnvironmentId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentExternalId { get; init; }
    public string? ActorId { get; init; }
    public string? ActorType { get; init; }
    public string? DelegationId { get; init; }
    public required string Action { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public required string ResourceAttributesJson { get; init; }
    public string? ResourceSnapshotHash { get; init; }
    public required string ContextJson { get; init; }
    public required DecisionEffect Decision { get; set; }
    public string? Reason { get; set; }
    public string? PolicyId { get; set; }
    public int? PolicyVersion { get; set; }
    public string? MatchedRule { get; set; }
    public required string RequestDigest { get; init; }
    public required string IdempotencyKey { get; init; }
    public string? OperationId { get; init; }
    public string? OperationHash { get; init; }
    public string? PermitId { get; set; }
    public string? ApprovalId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class IdempotencyRecord
{
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string Key { get; init; }
    public required string RequestDigest { get; init; }
    public required string DecisionId { get; init; }
    public required string ResponseJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
