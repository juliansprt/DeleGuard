using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Identity;

public sealed class HumanIdentityDirectory : IHumanIdentityDirectory
{
    private readonly DeleguardDbContext _db;
    private readonly IUserDirectory _users;

    public HumanIdentityDirectory(DeleguardDbContext db, IUserDirectory users)
    {
        _db = db;
        _users = users;
    }

    public async Task<ConsoleUserPrincipal?> FindByExternalSubjectAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        var needle = NormalizeIssuer(issuer);
        var matches = await _db.Users.AsNoTracking()
            .Where(x => x.ExternalSubject == subject)
            .ToListAsync(cancellationToken);
        var user = matches.FirstOrDefault(x => string.Equals(NormalizeIssuer(x.ExternalIssuer), needle, StringComparison.Ordinal));
        if (user is null)
        {
            return null;
        }

        if (!user.Enabled)
        {
            throw new ForbiddenException("This user is disabled.");
        }

        return await _users.GetPrincipalAsync(user.OrganizationId, user.Id, cancellationToken);
    }

    public async Task<ConsoleUserPrincipal?> GetPrincipalByUserIdAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId && x.Enabled, cancellationToken);
        if (user is null)
        {
            return null;
        }

        return await _users.GetPrincipalAsync(user.OrganizationId, user.Id, cancellationToken);
    }

    public async Task<ConsoleUserPrincipal?> LinkVerifiedEmailAsync(FederatedIdentity identity, CancellationToken cancellationToken)
    {
        if (!identity.EmailVerified)
        {
            return null;
        }

        var email = identity.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var matches = await _db.Users
            .Where(x => x.Enabled && x.Email == email && x.ExternalSubject == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        if (matches.Count != 1)
        {
            return null;
        }

        var user = matches[0];
        user.ExternalIssuer = NormalizeIssuer(identity.Issuer);
        user.ExternalSubject = identity.Subject;
        if (!string.IsNullOrWhiteSpace(identity.DisplayName))
        {
            user.DisplayName = identity.DisplayName.Trim();
        }

        return await _users.GetPrincipalAsync(user.OrganizationId, user.Id, cancellationToken);
    }

    internal static string NormalizeIssuer(string? issuer) =>
        (issuer ?? "").Trim().TrimEnd('/');
}
