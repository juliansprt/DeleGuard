using System.Security.Cryptography;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Infrastructure.Persistence;
using Deleguard.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Deleguard.Infrastructure.Identity;

public sealed class DemoAuthOptions
{
    public string ApiKeyPepper { get; set; } = "deleguard-dev-pepper";
    public string ConsoleToken { get; set; } = "dgc_demo_console_local_dev_only";
    public string DemoOrganizationId { get; set; } = "org_demo";
    public string DemoAdminEmail { get; set; } = "admin@demo.local";
    public string DemoAdminPassword { get; set; } = "DeleguardDemo!23";
    public bool AllowEmergencyConsoleToken { get; set; } = true;
}

public sealed class OidcOptions
{
    public const string Section = "Oidc";
    public bool Enabled { get; set; }
    public string Authority { get; set; } = "";
    public string? MetadataAddress { get; set; }
    public string? BackchannelAuthority { get; set; }
    public string ClientId { get; set; } = "deleguard-console";
    public string ClientSecret { get; set; } = "";
    public string? KeycloakAdminUrl { get; set; } = "http://localhost:8081/admin";
    public bool GoogleEnabled { get; set; }
    public bool MicrosoftEnabled { get; set; }
    /// <summary>
    /// When true, JIT provisioning waits for Keycloak email_verified. Local compose sets false
    /// so a Keycloak session becomes a console user without a Mailpit round-trip.
    /// </summary>
    public bool RequireVerifiedEmail { get; set; } = true;
}

public sealed class AgentDirectory : IAgentDirectory
{
    private readonly DeleguardDbContext _db;
    private readonly DemoAuthOptions _options;

    public AgentDirectory(DeleguardDbContext db, IOptions<DemoAuthOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<AgentPrincipal?> AuthenticateAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var hash = ApiKeyHasher.Hash(apiKey, _options.ApiKeyPepper);
        var credential = await _db.AgentCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.KeyHash == hash && x.RevokedAt == null, cancellationToken);
        if (credential is null)
        {
            return null;
        }

        var agent = await _db.Agents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == credential.AgentId && x.Enabled, cancellationToken);
        return await ToPrincipalAsync(agent, cancellationToken);
    }

    public Task<Agent?> GetAsync(string organizationId, string agentId, CancellationToken cancellationToken) =>
        _db.Agents.AsNoTracking().FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == agentId, cancellationToken);

    public async Task<AgentPrincipal?> GetPrincipalAsync(string organizationId, string agentId, CancellationToken cancellationToken)
    {
        var agent = await _db.Agents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == agentId && x.Enabled, cancellationToken);
        return await ToPrincipalAsync(agent, cancellationToken);
    }

    public async Task<AgentPrincipal?> GetPrincipalByExternalIdAsync(string organizationId, string externalId, CancellationToken cancellationToken)
    {
        var agent = await _db.Agents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.ExternalId == externalId && x.Enabled, cancellationToken);
        return await ToPrincipalAsync(agent, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentListItem>> ListAsync(
        string organizationId,
        string? applicationId,
        CancellationToken cancellationToken)
    {
        var agentsQuery = _db.Agents.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            agentsQuery = agentsQuery.Where(x => x.ApplicationId == applicationId);
        }

        var agents = await agentsQuery.OrderBy(x => x.ExternalId).ToListAsync(cancellationToken);
        var environments = await _db.Environments.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var credentials = await _db.AgentCredentials.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var prefixes = credentials
            .GroupBy(x => x.AgentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.CreatedAt).First().KeyPrefix);

        return agents.Select(agent => new AgentListItem(
            agent.Id,
            agent.ExternalId,
            agent.DisplayName,
            agent.ApplicationId,
            agent.EnvironmentId,
            environments.TryGetValue(agent.EnvironmentId, out var env) ? env.Name : agent.EnvironmentId,
            agent.Enabled,
            prefixes.GetValueOrDefault(agent.Id))).ToList();
    }

    public async Task<IReadOnlyList<EnvironmentInfo>> ListEnvironmentsAsync(
        string organizationId,
        string applicationId,
        CancellationToken cancellationToken)
    {
        var environments = await _db.Environments.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ApplicationId == applicationId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        return environments.Select(x => new EnvironmentInfo(x.Id, x.Name, x.ApplicationId)).ToList();
    }

    public async Task<CreatedAgentSecret> CreateAsync(
        string organizationId,
        string applicationId,
        string externalId,
        string displayName,
        string environmentName,
        CancellationToken cancellationToken)
    {
        var environment = await _db.Environments
                              .FirstOrDefaultAsync(
                                  x => x.OrganizationId == organizationId
                                       && x.ApplicationId == applicationId
                                       && x.Name == environmentName,
                                  cancellationToken)
                          ?? throw new RequestValidationException($"Unknown environment '{environmentName}'.");
        var duplicate = await _db.Agents.AnyAsync(
            x => x.ApplicationId == applicationId && x.EnvironmentId == environment.Id && x.ExternalId == externalId,
            cancellationToken);
        if (duplicate)
        {
            throw new ConflictException("An agent with that id already exists in this environment.");
        }

        var key = NewApiKey();
        var agent = new Agent
        {
            Id = IdGenerator.New("agt"),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            EnvironmentId = environment.Id,
            ExternalId = externalId,
            DisplayName = displayName,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _db.Agents.AddAsync(agent, cancellationToken);
        await _db.AgentCredentials.AddAsync(new AgentCredential
        {
            Id = IdGenerator.New("crd"),
            OrganizationId = organizationId,
            AgentId = agent.Id,
            KeyPrefix = ApiKeyHasher.Prefix(key),
            KeyHash = ApiKeyHasher.Hash(key, _options.ApiKeyPepper),
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        return new CreatedAgentSecret(agent.Id, agent.ExternalId, key, ApiKeyHasher.Prefix(key));
    }

    public async Task<T> ExecuteLockedAsync<T>(string organizationId, string agentId, Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var locked = await _db.Agents.FromSqlInterpolated($"""
                SELECT * FROM agents WHERE "OrganizationId" = {organizationId} AND "Id" = {agentId} FOR UPDATE
                """).AsNoTracking().ToListAsync(ct);
            if (locked.Count != 1) throw new NotFoundException("Agent not found.");
            foreach (var entry in _db.ChangeTracker.Entries().Where(e => KeyEntity(e.Entity, organizationId, agentId)).ToArray())
            {
                if (entry.State is not (EntityState.Unchanged or EntityState.Detached))
                    throw new InvalidOperationException("Save pending agent changes before acquiring its credential lock.");
                entry.State = EntityState.Detached;
            }
            var result = await action();
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            try { if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None); }
            finally
            {
                // Failed mutations must not be flushed by the gateway's subsequent evidence save.
                foreach (var entry in _db.ChangeTracker.Entries().Where(e => KeyEntity(e.Entity, organizationId, agentId)).ToArray())
                    entry.State = EntityState.Detached;
            }
            throw;
        }
    }

    private static bool KeyEntity(object entity, string org, string id) => entity switch
    {
        Agent agent => agent.OrganizationId == org && agent.Id == id,
        AgentCredential credential => credential.OrganizationId == org && credential.AgentId == id,
        _ => false
    };

    public async Task<CreatedAgentSecret> RotateKeyAsync(string organizationId, string agentId, CancellationToken cancellationToken)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == agentId, cancellationToken)
                    ?? throw new NotFoundException("Agent not found.");
        var active = await _db.AgentCredentials
            .Where(x => x.OrganizationId == organizationId && x.AgentId == agentId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var credential in active)
        {
            credential.RevokedAt = now;
        }

        var key = NewApiKey();
        await _db.AgentCredentials.AddAsync(new AgentCredential
        {
            Id = IdGenerator.New("crd"),
            OrganizationId = organizationId,
            AgentId = agent.Id,
            KeyPrefix = ApiKeyHasher.Prefix(key),
            KeyHash = ApiKeyHasher.Hash(key, _options.ApiKeyPepper),
            CreatedAt = now
        }, cancellationToken);
        return new CreatedAgentSecret(agent.Id, agent.ExternalId, key, ApiKeyHasher.Prefix(key));
    }

    public async Task SetEnabledAsync(string organizationId, string agentId, bool enabled, CancellationToken cancellationToken)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == agentId, cancellationToken)
                    ?? throw new NotFoundException("Agent not found.");
        if (!enabled && string.Equals(agent.ExternalId, AccessControlIds.AdminMcpExternalId, StringComparison.Ordinal))
        {
            throw new RequestValidationException("The Administration MCP agent cannot be disabled.");
        }

        agent.Enabled = enabled;
    }

    private async Task<AgentPrincipal?> ToPrincipalAsync(Agent? agent, CancellationToken cancellationToken)
    {
        if (agent is null)
        {
            return null;
        }

        var environment = await _db.Environments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == agent.EnvironmentId, cancellationToken);
        if (environment is null)
        {
            return null;
        }

        return new AgentPrincipal(
            agent.OrganizationId,
            agent.ApplicationId,
            environment.Id,
            environment.Name,
            agent.Id,
            agent.ExternalId);
    }

    private static string NewApiKey()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return "dgk_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
