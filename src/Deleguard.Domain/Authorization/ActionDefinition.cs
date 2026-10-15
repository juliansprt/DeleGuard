using Deleguard.Domain.Common;

namespace Deleguard.Domain.Authorization;

public sealed class ActionDefinition
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string Name { get; set; }
    public required string ResourceType { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.Medium;
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class AgentActionGrant
{
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string AgentId { get; init; }
    public required string ActionDefinitionId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
