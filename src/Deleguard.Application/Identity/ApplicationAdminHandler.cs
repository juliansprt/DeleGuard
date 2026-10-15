using System.Text.RegularExpressions;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;

namespace Deleguard.Application.Identity;

public sealed class OrganizationAdminHandler
{
    private readonly IOrganizationDirectory _organizations;
    private readonly IUnitOfWork _uow;

    public OrganizationAdminHandler(IOrganizationDirectory organizations, IUnitOfWork uow)
    {
        _organizations = organizations;
        _uow = uow;
    }

    public async Task<OrganizationView> GetAsync(string organizationId, CancellationToken cancellationToken)
    {
        var organization = await _organizations.GetAsync(organizationId, cancellationToken)
                           ?? throw new NotFoundException("Organization not found.");
        return new OrganizationView(organization.Id, organization.Name, organization.CreatedAt);
    }

    public async Task<OrganizationView> UpdateNameAsync(string organizationId, string name, CancellationToken cancellationToken)
    {
        var normalized = CompleteOnboardingHandler.NormalizeName(name);
        await _organizations.UpdateNameAsync(organizationId, normalized, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return await GetAsync(organizationId, cancellationToken);
    }
}

public sealed record OrganizationView(string Id, string Name, DateTimeOffset CreatedAt);

public sealed class ApplicationAdminHandler
{
    private static readonly Regex SlugPattern = new("^[a-z][a-z0-9_-]{1,63}$", RegexOptions.Compiled);
    private readonly IApplicationDirectory _applications;
    private readonly IUnitOfWork _uow;

    public ApplicationAdminHandler(IApplicationDirectory applications, IUnitOfWork uow)
    {
        _applications = applications;
        _uow = uow;
    }

    public Task<IReadOnlyList<ApplicationListItem>> ListAsync(string organizationId, CancellationToken cancellationToken) =>
        _applications.ListAsync(organizationId, cancellationToken);

    public Task<IReadOnlyList<ApplicationListItem>> ListProductAsync(string organizationId, CancellationToken cancellationToken) =>
        _applications.ListProductAsync(organizationId, cancellationToken);

    public async Task<ApplicationListItem> GetAsync(string organizationId, string applicationId, CancellationToken cancellationToken) =>
        await _applications.GetAsync(organizationId, applicationId, cancellationToken)
        ?? throw new NotFoundException("Application not found.");

    public Task<ApplicationListItem> GetAdministrationAsync(string organizationId, CancellationToken cancellationToken) =>
        _applications.GetAdministrationAsync(organizationId, cancellationToken);

    public Task<ApplicationListItem> GetDefaultProductAsync(string organizationId, CancellationToken cancellationToken) =>
        _applications.GetDefaultProductAsync(organizationId, cancellationToken);

    public Task<ApplicationListItem> ResolveProductAsync(
        string organizationId,
        string? idOrSlug,
        CancellationToken cancellationToken) =>
        _applications.ResolveProductAsync(organizationId, idOrSlug, cancellationToken);

    public async Task<ApplicationListItem> CreateAsync(
        string organizationId,
        string name,
        string slug,
        CancellationToken cancellationToken)
    {
        var created = await _applications.CreateProductAsync(
            organizationId,
            NormalizeName(name),
            NormalizeSlug(slug),
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task<ApplicationListItem> UpdateAsync(
        string organizationId,
        string applicationId,
        string name,
        string slug,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new RequestValidationException("application_id is required.");
        }

        var updated = await _applications.UpdateAsync(
            organizationId,
            applicationId,
            NormalizeName(name),
            NormalizeSlug(slug),
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public Task EnsureCanDeleteAsync(string organizationId, string applicationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new RequestValidationException("application_id is required.");
        }

        return _applications.EnsureCanDeleteAsync(organizationId, applicationId, cancellationToken);
    }

    public async Task DeleteAsync(string organizationId, string applicationId, CancellationToken cancellationToken)
    {
        await EnsureCanDeleteAsync(organizationId, applicationId, cancellationToken);
        await _applications.DeleteAsync(organizationId, applicationId, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public static string NormalizeSlug(string slug)
    {
        var trimmed = slug.Trim().ToLowerInvariant();
        if (!SlugPattern.IsMatch(trimmed))
        {
            throw new RequestValidationException("slug must be a lowercase id (letters, digits, _ or -).");
        }

        if (string.Equals(trimmed, ApplicationSlugs.Administration, StringComparison.Ordinal))
        {
            throw new RequestValidationException("The administration slug is reserved.");
        }

        return trimmed;
    }

    public static string NormalizeName(string name)
    {
        var trimmed = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (trimmed.Length is < 1 or > 80)
        {
            throw new RequestValidationException("Application name must be between 1 and 80 characters.");
        }

        return trimmed;
    }
}
