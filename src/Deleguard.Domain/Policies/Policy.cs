namespace Deleguard.Domain.Policies;

public sealed class PolicyDefinition
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string Slug { get; set; }
    public string? PublishedVersionId { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class AgentPolicyGrant
{
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string AgentId { get; init; }
    public required string PolicyId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class PolicyVersion
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string PolicyId { get; init; }
    public required int Version { get; init; }
    public required string Yaml { get; init; }
    public required string Action { get; init; }
    public required string ResourceType { get; init; }
    public int Priority { get; init; }
    public bool Published { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
}
