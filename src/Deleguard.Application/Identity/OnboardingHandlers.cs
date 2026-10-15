using Deleguard.Application.Abstractions;

namespace Deleguard.Application.Identity;

public sealed class ResolveHumanSessionHandler
{
    private readonly IHumanIdentityDirectory _humans;
    private readonly ITenantBootstrapper _tenants;
    private readonly IUnitOfWork _uow;

    public ResolveHumanSessionHandler(IHumanIdentityDirectory humans, ITenantBootstrapper tenants, IUnitOfWork uow)
    {
        _humans = humans;
        _tenants = tenants;
        _uow = uow;
    }

    public async Task<ConsoleUserPrincipal?> HandleAsync(FederatedIdentity identity, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identity.Issuer) || string.IsNullOrWhiteSpace(identity.Subject))
        {
            return null;
        }

        var existing = await _humans.FindByExternalSubjectAsync(identity.Issuer, identity.Subject, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        if (!identity.EmailVerified)
        {
            return null;
        }

        var linked = await _humans.LinkVerifiedEmailAsync(identity, cancellationToken);
        if (linked is not null)
        {
            await _uow.SaveChangesAsync(cancellationToken);
            return linked;
        }

        var created = await _tenants.ProvisionNewOrganizationAsync(
            CompleteOnboardingHandler.ResolveOrganizationName(identity),
            identity,
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return created;
    }
}

public sealed class CompleteOnboardingHandler
{
    private readonly IHumanIdentityDirectory _humans;
    private readonly ITenantBootstrapper _tenants;
    private readonly IUnitOfWork _uow;

    public CompleteOnboardingHandler(IHumanIdentityDirectory humans, ITenantBootstrapper tenants, IUnitOfWork uow)
    {
        _humans = humans;
        _tenants = tenants;
        _uow = uow;
    }

    public async Task<ConsoleUserPrincipal> HandleAsync(string organizationName, FederatedIdentity identity, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identity.Issuer) || string.IsNullOrWhiteSpace(identity.Subject))
        {
            throw new RequestValidationException("Federated identity is incomplete.");
        }

        if (!identity.EmailVerified)
        {
            throw new ForbiddenException("Verify your email before creating an organization.");
        }

        var existing = await _humans.FindByExternalSubjectAsync(identity.Issuer, identity.Subject, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var linked = await _humans.LinkVerifiedEmailAsync(identity, cancellationToken);
        if (linked is not null)
        {
            await _uow.SaveChangesAsync(cancellationToken);
            return linked;
        }

        var name = string.IsNullOrWhiteSpace(organizationName)
            ? ResolveOrganizationName(identity)
            : NormalizeName(organizationName);
        var created = await _tenants.ProvisionNewOrganizationAsync(name, identity, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return created;
    }

    internal static string ResolveOrganizationName(FederatedIdentity identity)
    {
        if (TryNormalizeOptional(identity.Company, out var company))
        {
            return company;
        }

        var display = identity.DisplayName?.Trim() ?? "";
        if (display.Length >= 2 && display.Contains('@', StringComparison.Ordinal) == false)
        {
            var named = display + " workspace";
            if (named.Length is >= 3 and <= 80)
            {
                return named;
            }
        }

        var at = identity.Email.IndexOf('@');
        var local = at > 0 ? identity.Email[..at].Trim() : "";
        var fallback = string.IsNullOrWhiteSpace(local) ? "New workspace" : local + " workspace";
        return NormalizeName(fallback);
    }

    internal static string NormalizeName(string organizationName)
    {
        var name = string.Join(' ', organizationName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 3 or > 80)
        {
            throw new RequestValidationException("Organization name must be between 3 and 80 characters.");
        }

        if (name.Any(char.IsControl))
        {
            throw new RequestValidationException("Organization name contains invalid characters.");
        }

        return name;
    }

    private static bool TryNormalizeOptional(string? value, out string name)
    {
        name = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            name = NormalizeName(value);
            return true;
        }
        catch (RequestValidationException)
        {
            return false;
        }
    }
}
