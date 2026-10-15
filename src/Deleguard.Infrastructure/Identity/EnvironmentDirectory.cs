using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Identity;

public sealed class EnvironmentDirectory : IEnvironmentDirectory
{
    private readonly DeleguardDbContext _db;

    public EnvironmentDirectory(DeleguardDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<EnvironmentListItem>> ListAsync(
        string organizationId,
        string applicationId,
        CancellationToken cancellationToken)
    {
        var environments = await _db.Environments.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ApplicationId == applicationId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var counts = await AgentCountsAsync(organizationId, applicationId, cancellationToken);
        return environments.Select(env => ToItem(env, counts.GetValueOrDefault(env.Id))).ToList();
    }

    public async Task<EnvironmentListItem?> GetAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var environment = await _db.Environments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == environmentId, cancellationToken);
        if (environment is null)
        {
            return null;
        }

        var count = await _db.Agents.CountAsync(
            x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId,
            cancellationToken);
        return ToItem(environment, count);
    }

    public async Task<EnvironmentListItem> CreateAsync(
        string organizationId,
        string applicationId,
        string name,
        CancellationToken cancellationToken)
    {
        if (!await _db.Applications.AnyAsync(
                x => x.OrganizationId == organizationId && x.Id == applicationId,
                cancellationToken))
        {
            throw new NotFoundException("Application not found.");
        }

        if (await NameTakenAsync(organizationId, applicationId, name, null, cancellationToken))
        {
            throw new ConflictException("An environment with that name already exists.");
        }

        var environment = new OrganizationEnvironment
        {
            Id = IdGenerator.New("env"),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _db.Environments.AddAsync(environment, cancellationToken);
        return ToItem(environment, 0);
    }

    public async Task<EnvironmentListItem> UpdateAsync(string organizationId, string environmentId, string name, CancellationToken cancellationToken)
    {
        var environment = await RequireTrackedAsync(organizationId, environmentId, cancellationToken);
        if (await NameTakenAsync(organizationId, environment.ApplicationId, name, environmentId, cancellationToken))
        {
            throw new ConflictException("An environment with that name already exists.");
        }

        environment.Name = name;
        var count = await _db.Agents.CountAsync(
            x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId,
            cancellationToken);
        return ToItem(environment, count);
    }

    public async Task EnsureCanDeleteAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var environment = await RequireTrackedAsync(organizationId, environmentId, cancellationToken);
        var agents = await _db.Agents.CountAsync(
            x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId,
            cancellationToken);
        if (agents > 0)
        {
            throw new ConflictException("Move or disable agents before deleting this environment.");
        }

        var remaining = await _db.Environments.CountAsync(
            x => x.OrganizationId == organizationId && x.ApplicationId == environment.ApplicationId,
            cancellationToken);
        if (remaining <= 1)
        {
            throw new ConflictException("An application must keep at least one environment.");
        }
    }

    public async Task DeleteAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        await EnsureCanDeleteAsync(organizationId, environmentId, cancellationToken);
        var environment = await RequireTrackedAsync(organizationId, environmentId, cancellationToken);
        _db.Environments.Remove(environment);
    }

    private async Task<OrganizationEnvironment> RequireTrackedAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        await _db.Environments.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.Id == environmentId,
            cancellationToken)
        ?? throw new NotFoundException("Environment not found.");

    private Task<bool> NameTakenAsync(
        string organizationId,
        string applicationId,
        string name,
        string? exceptId,
        CancellationToken cancellationToken) =>
        _db.Environments.AnyAsync(
            x => x.OrganizationId == organizationId
                 && x.ApplicationId == applicationId
                 && x.Name == name
                 && (exceptId == null || x.Id != exceptId),
            cancellationToken);

    private async Task<Dictionary<string, int>> AgentCountsAsync(
        string organizationId,
        string applicationId,
        CancellationToken cancellationToken) =>
        await _db.Agents.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ApplicationId == applicationId)
            .GroupBy(x => x.EnvironmentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

    private static EnvironmentListItem ToItem(OrganizationEnvironment environment, int agentCount) =>
        new(environment.Id, environment.Name, environment.ApplicationId, environment.CreatedAt, agentCount);
}
