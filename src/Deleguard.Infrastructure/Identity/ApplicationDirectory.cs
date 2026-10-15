using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Deleguard.Infrastructure.Identity;

public sealed class OrganizationDirectory : IOrganizationDirectory
{
    private readonly DeleguardDbContext _db;

    public OrganizationDirectory(DeleguardDbContext db) => _db = db;

    public Task<Organization?> GetAsync(string organizationId, CancellationToken cancellationToken) =>
        _db.Organizations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == organizationId, cancellationToken);

    public async Task UpdateNameAsync(string organizationId, string name, CancellationToken cancellationToken)
    {
        var organization = await _db.Organizations.FirstOrDefaultAsync(x => x.Id == organizationId, cancellationToken)
                           ?? throw new NotFoundException("Organization not found.");
        organization.Name = name;
    }
}

public sealed class ApplicationDirectory : IApplicationDirectory
{
    private readonly DeleguardDbContext _db;

    public ApplicationDirectory(DeleguardDbContext db) => _db = db;

    public async Task<IReadOnlyList<ApplicationListItem>> ListAsync(string organizationId, CancellationToken cancellationToken)
    {
        var apps = await _db.Applications.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var counts = await AgentCountsAsync(organizationId, cancellationToken);
        return apps.Select(app => ToItem(app, counts.GetValueOrDefault(app.Id))).ToList();
    }

    public async Task<IReadOnlyList<ApplicationListItem>> ListProductAsync(string organizationId, CancellationToken cancellationToken)
    {
        var apps = await _db.Applications.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Kind == ApplicationKinds.Product)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var counts = await AgentCountsAsync(organizationId, cancellationToken);
        return apps.Select(app => ToItem(app, counts.GetValueOrDefault(app.Id))).ToList();
    }

    public async Task<ApplicationListItem?> GetAsync(string organizationId, string applicationId, CancellationToken cancellationToken)
    {
        var app = await _db.Applications.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == applicationId, cancellationToken);
        if (app is null)
        {
            return null;
        }

        var count = await _db.Agents.CountAsync(
            x => x.OrganizationId == organizationId && x.ApplicationId == applicationId,
            cancellationToken);
        return ToItem(app, count);
    }

    public async Task<ApplicationListItem> GetAdministrationAsync(string organizationId, CancellationToken cancellationToken)
    {
        var app = await _db.Applications.AsNoTracking()
                      .FirstOrDefaultAsync(
                          x => x.OrganizationId == organizationId && x.Kind == ApplicationKinds.Administration,
                          cancellationToken)
                  ?? throw new FailClosedException("Administration application is not provisioned.");
        var count = await _db.Agents.CountAsync(
            x => x.OrganizationId == organizationId && x.ApplicationId == app.Id,
            cancellationToken);
        return ToItem(app, count);
    }

    public async Task<ApplicationListItem> GetDefaultProductAsync(string organizationId, CancellationToken cancellationToken)
    {
        var apps = await ListProductAsync(organizationId, cancellationToken);
        return apps.FirstOrDefault()
               ?? throw new FailClosedException("This organization has no product application.");
    }

    public async Task<ApplicationListItem> ResolveProductAsync(
        string organizationId,
        string? idOrSlug,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idOrSlug))
        {
            return await GetDefaultProductAsync(organizationId, cancellationToken);
        }

        var key = idOrSlug.Trim();
        var app = await _db.Applications.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId
                     && x.Kind == ApplicationKinds.Product
                     && (x.Id == key || x.Slug == key),
                cancellationToken);
        if (app is null)
        {
            throw new NotFoundException("Application not found.");
        }

        var count = await _db.Agents.CountAsync(
            x => x.OrganizationId == organizationId && x.ApplicationId == app.Id,
            cancellationToken);
        return ToItem(app, count);
    }

    public async Task<ApplicationListItem> CreateProductAsync(
        string organizationId,
        string name,
        string slug,
        CancellationToken cancellationToken)
    {
        if (await _db.Applications.AnyAsync(
                x => x.OrganizationId == organizationId && x.Slug == slug,
                cancellationToken))
        {
            throw new ConflictException("An application with that id already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var application = new OrganizationApplication
        {
            Id = IdGenerator.New("app"),
            OrganizationId = organizationId,
            Slug = slug,
            Name = name,
            Kind = ApplicationKinds.Product,
            CreatedAt = now
        };
        await _db.Applications.AddAsync(application, cancellationToken);
        await _db.Environments.AddAsync(new OrganizationEnvironment
        {
            Id = IdGenerator.New("env"),
            OrganizationId = organizationId,
            ApplicationId = application.Id,
            Name = "production",
            CreatedAt = now
        }, cancellationToken);
        return ToItem(application, 0);
    }

    public async Task<ApplicationListItem> UpdateAsync(
        string organizationId,
        string applicationId,
        string name,
        string slug,
        CancellationToken cancellationToken)
    {
        var application = await RequireProductTrackedAsync(organizationId, applicationId, cancellationToken);
        if (await _db.Applications.AnyAsync(
                x => x.OrganizationId == organizationId && x.Slug == slug && x.Id != applicationId,
                cancellationToken))
        {
            throw new ConflictException("An application with that id already exists.");
        }

        application.Name = name;
        application.Slug = slug;
        var count = await _db.Agents.CountAsync(
            x => x.OrganizationId == organizationId && x.ApplicationId == applicationId,
            cancellationToken);
        return ToItem(application, count);
    }

    public async Task EnsureCanDeleteAsync(string organizationId, string applicationId, CancellationToken cancellationToken)
    {
        var application = await RequireProductTrackedAsync(organizationId, applicationId, cancellationToken);
        var agents = await _db.Agents.CountAsync(
            x => x.OrganizationId == organizationId && x.ApplicationId == application.Id,
            cancellationToken);
        if (agents > 0)
        {
            throw new ConflictException("Move or disable agents before deleting this application.");
        }

        var remaining = await _db.Applications.CountAsync(
            x => x.OrganizationId == organizationId && x.Kind == ApplicationKinds.Product,
            cancellationToken);
        if (remaining <= 1)
        {
            throw new ConflictException("An organization must keep at least one application.");
        }
    }

    public async Task DeleteAsync(string organizationId, string applicationId, CancellationToken cancellationToken)
    {
        await EnsureCanDeleteAsync(organizationId, applicationId, cancellationToken);
        var application = await RequireProductTrackedAsync(organizationId, applicationId, cancellationToken);
        var environments = await _db.Environments
            .Where(x => x.OrganizationId == organizationId && x.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);
        var actions = await _db.ActionDefinitions
            .Where(x => x.OrganizationId == organizationId && x.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);
        var actionIds = actions.Select(x => x.Id).ToList();
        var bindings = await _db.ActionMcpBindings
            .Where(x => x.OrganizationId == organizationId && actionIds.Contains(x.ActionDefinitionId))
            .ToListAsync(cancellationToken);
        var actionGrants = await _db.AgentActionGrants
            .Where(x => x.OrganizationId == organizationId && actionIds.Contains(x.ActionDefinitionId))
            .ToListAsync(cancellationToken);
        var policies = await _db.Policies
            .Where(x => x.OrganizationId == organizationId && x.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);
        var policyIds = policies.Select(x => x.Id).ToList();
        var policyVersions = await _db.PolicyVersions
            .Where(x => x.OrganizationId == organizationId && policyIds.Contains(x.PolicyId))
            .ToListAsync(cancellationToken);
        var policyGrants = await _db.AgentPolicyGrants
            .Where(x => x.OrganizationId == organizationId && policyIds.Contains(x.PolicyId))
            .ToListAsync(cancellationToken);
        var webhooks = await _db.WebhookEndpoints
            .Where(x => x.OrganizationId == organizationId && x.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);

        _db.AgentActionGrants.RemoveRange(actionGrants);
        _db.ActionMcpBindings.RemoveRange(bindings);
        _db.AgentPolicyGrants.RemoveRange(policyGrants);
        _db.PolicyVersions.RemoveRange(policyVersions);
        _db.Policies.RemoveRange(policies);
        _db.ActionDefinitions.RemoveRange(actions);
        _db.WebhookEndpoints.RemoveRange(webhooks);
        _db.Environments.RemoveRange(environments);
        _db.Applications.Remove(application);
    }

    private async Task<OrganizationApplication> RequireProductTrackedAsync(
        string organizationId,
        string applicationId,
        CancellationToken cancellationToken)
    {
        var application = await _db.Applications.FirstOrDefaultAsync(
                              x => x.OrganizationId == organizationId && x.Id == applicationId,
                              cancellationToken)
                          ?? throw new NotFoundException("Application not found.");
        if (application.IsAdministration)
        {
            throw new RequestValidationException("The administration application cannot be changed or deleted.");
        }

        return application;
    }

    private async Task<Dictionary<string, int>> AgentCountsAsync(string organizationId, CancellationToken cancellationToken) =>
        await _db.Agents.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .GroupBy(x => x.ApplicationId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

    private static ApplicationListItem ToItem(OrganizationApplication application, int agentCount) =>
        new(application.Id, application.Slug, application.Name, application.Kind, application.CreatedAt, agentCount);
}
