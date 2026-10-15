namespace Deleguard.Domain.Authorization;

public sealed class ActionMcpBinding
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string ActionDefinitionId { get; init; }
    public required string McpServer { get; set; }
    public required string McpTool { get; set; }
    public required string ResourceIdPath { get; set; }
    public string AttributePathsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; init; }
}
