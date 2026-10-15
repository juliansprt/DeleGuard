namespace Deleguard.Domain.Policies;

public sealed class PolicySimulationCase
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string ApplicationId { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RequestJson { get; set; } = string.Empty;
    public string ExpectedEffect { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
