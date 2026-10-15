using Deleguard.Domain.Identity;

namespace Deleguard.Infrastructure.Persistence;

internal static class ApplicationSeed
{
    public static OrganizationApplication Administration(
        string organizationId,
        DateTimeOffset now,
        string? id = null) =>
        new()
        {
            Id = id ?? IdFor(organizationId, "admin"),
            OrganizationId = organizationId,
            Slug = ApplicationSlugs.Administration,
            Name = "Administration",
            Kind = ApplicationKinds.Administration,
            CreatedAt = now
        };

    public static OrganizationApplication DefaultProduct(
        string organizationId,
        string organizationName,
        DateTimeOffset now,
        string? id = null) =>
        new()
        {
            Id = id ?? IdFor(organizationId, "default"),
            OrganizationId = organizationId,
            Slug = ApplicationSlugs.Default,
            Name = string.IsNullOrWhiteSpace(organizationName) ? "Default" : organizationName.Trim(),
            Kind = ApplicationKinds.Product,
            CreatedAt = now
        };

    public static OrganizationEnvironment Production(
        string organizationId,
        string applicationId,
        DateTimeOffset now,
        string? id = null) =>
        new()
        {
            Id = id ?? Deleguard.Domain.Common.IdGenerator.New("env"),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Name = "production",
            CreatedAt = now
        };

    private static string IdFor(string organizationId, string suffix)
    {
        var trimmed = organizationId.StartsWith("org_", StringComparison.Ordinal)
            ? organizationId["org_".Length..]
            : organizationId;
        var id = $"app_{suffix}_{trimmed}";
        return id.Length <= 64 ? id : id[..64];
    }
}
