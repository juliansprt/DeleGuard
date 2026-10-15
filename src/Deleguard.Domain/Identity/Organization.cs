namespace Deleguard.Domain.Identity;

public sealed class Organization
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

public static class ApplicationKinds
{
    public const string Product = "product";
    public const string Administration = "administration";
}

public static class ApplicationSlugs
{
    public const string Default = "default";
    public const string Administration = "administration";
}

public sealed class OrganizationApplication
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public bool Suspended { get; set; }

    public bool IsAdministration =>
        string.Equals(Kind, ApplicationKinds.Administration, StringComparison.Ordinal);

    public bool IsProduct =>
        string.Equals(Kind, ApplicationKinds.Product, StringComparison.Ordinal);
}

public sealed class OrganizationEnvironment
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class Agent
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string ApplicationId { get; init; }
    public required string EnvironmentId { get; init; }
    public required string ExternalId { get; set; }
    public required string DisplayName { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class AgentCredential
{
    public required string Id { get; init; }
    public required string OrganizationId { get; init; }
    public required string AgentId { get; init; }
    public required string KeyPrefix { get; init; }
    public required string KeyHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null;
}
