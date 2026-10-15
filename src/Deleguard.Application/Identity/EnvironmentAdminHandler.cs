using System.Text.RegularExpressions;
using Deleguard.Application.Abstractions;

namespace Deleguard.Application.Identity;

public sealed class EnvironmentAdminHandler
{
    private static readonly Regex NamePattern = new("^[a-z][a-z0-9_-]{1,63}$", RegexOptions.Compiled);
    private readonly IEnvironmentDirectory _environments;
    private readonly IUnitOfWork _uow;

    public EnvironmentAdminHandler(IEnvironmentDirectory environments, IUnitOfWork uow)
    {
        _environments = environments;
        _uow = uow;
    }

    public Task<IReadOnlyList<EnvironmentListItem>> ListAsync(
        string organizationId,
        string applicationId,
        CancellationToken cancellationToken) =>
        _environments.ListAsync(organizationId, applicationId, cancellationToken);

    public async Task<EnvironmentListItem> GetAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(environmentId))
        {
            throw new RequestValidationException("environment_id is required.");
        }

        return await _environments.GetAsync(organizationId, environmentId, cancellationToken)
               ?? throw new NotFoundException("Environment not found.");
    }

    public async Task<EnvironmentListItem> CreateAsync(
        string organizationId,
        string applicationId,
        string name,
        CancellationToken cancellationToken)
    {
        var created = await _environments.CreateAsync(organizationId, applicationId, NormalizeName(name), cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task<EnvironmentListItem> UpdateAsync(string organizationId, string environmentId, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(environmentId))
        {
            throw new RequestValidationException("environment_id is required.");
        }

        var updated = await _environments.UpdateAsync(organizationId, environmentId, NormalizeName(name), cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public Task EnsureCanDeleteAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(environmentId))
        {
            throw new RequestValidationException("environment_id is required.");
        }

        return _environments.EnsureCanDeleteAsync(organizationId, environmentId, cancellationToken);
    }

    public async Task DeleteAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        await EnsureCanDeleteAsync(organizationId, environmentId, cancellationToken);
        await _environments.DeleteAsync(organizationId, environmentId, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public static string NormalizeName(string name)
    {
        var trimmed = name.Trim();
        if (!NamePattern.IsMatch(trimmed))
        {
            throw new RequestValidationException("name must be a lowercase slug (letters, digits, _ or -).");
        }

        return trimmed;
    }
}
