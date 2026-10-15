namespace Deleguard.Domain.Evidence;
public sealed class RemoteGuardCall
{
    public required string DecisionId { get; init; }
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string EnvironmentId { get; init; }
    public required string AgentId { get; init; }
    public required string Tool { get; init; }
    public required string ProtectedArguments { get; init; }
    public required string BindingHash { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
