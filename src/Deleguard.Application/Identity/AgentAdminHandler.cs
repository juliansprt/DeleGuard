using System.Text.RegularExpressions;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;

namespace Deleguard.Application.Identity;

public sealed record CreateAgentCommand(
    string OrganizationId,
    string ApplicationId,
    string ExternalId,
    string DisplayName,
    string EnvironmentName);

public sealed class AgentAdminHandler
{
    private static readonly Regex ExternalIdPattern = new("^[a-z][a-z0-9_-]{1,63}$", RegexOptions.Compiled);
    private readonly IAgentDirectory _agents;
    private readonly IUnitOfWork _uow;

    public AgentAdminHandler(IAgentDirectory agents, IUnitOfWork uow)
    {
        _agents = agents;
        _uow = uow;
    }

    public Task<IReadOnlyList<AgentListItem>> ListAsync(
        string organizationId,
        string? applicationId,
        CancellationToken cancellationToken) =>
        _agents.ListAsync(organizationId, applicationId, cancellationToken);

    public Task<IReadOnlyList<EnvironmentInfo>> ListEnvironmentsAsync(
        string organizationId,
        string applicationId,
        CancellationToken cancellationToken) =>
        _agents.ListEnvironmentsAsync(organizationId, applicationId, cancellationToken);

    public async Task<CreatedAgentSecret> CreateAsync(CreateAgentCommand command, CancellationToken cancellationToken)
    {
        if (!ExternalIdPattern.IsMatch(command.ExternalId))
        {
            throw new RequestValidationException("external_id must be a lowercase slug (letters, digits, _ or -).");
        }

        if (string.Equals(command.ExternalId.Trim(), AccessControlIds.AdminMcpExternalId, StringComparison.Ordinal))
        {
            throw new RequestValidationException("That agent id is reserved for Administration MCP.");
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 200)
        {
            throw new RequestValidationException("display_name is required.");
        }

        var environment = string.IsNullOrWhiteSpace(command.EnvironmentName) ? "production" : command.EnvironmentName.Trim();
        var created = await _agents.CreateAsync(
            command.OrganizationId,
            command.ApplicationId,
            command.ExternalId.Trim(),
            command.DisplayName.Trim(),
            environment,
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return created;
    }

    public Task<CreatedAgentSecret> RotateKeyAsync(string organizationId, string agentId, CancellationToken cancellationToken) =>
        _agents.ExecuteLockedAsync(organizationId, agentId,
            () => RotateAndSaveAsync(organizationId, agentId, cancellationToken), cancellationToken);

    public Task<CreatedAgentSecret> RotateKeyAsync(string organizationId, string applicationId, string agentId,
        string? expectedKeyPrefix, CancellationToken cancellationToken) =>
        _agents.ExecuteLockedAsync(organizationId, agentId, async () =>
        {
            // Application, enabled state and displayed key are checked under the same lock as rotation.
            var agent = (await _agents.ListAsync(organizationId, applicationId, cancellationToken)).SingleOrDefault(a => a.Id == agentId)
                ?? throw new NotFoundException("Agent not found.");
            if (!agent.Enabled) throw new RequestValidationException("This project's agent is disabled. Enable it before generating an API key.");
            if (!string.Equals(agent.ActiveKeyPrefix ?? "", expectedKeyPrefix ?? "", StringComparison.Ordinal))
                throw new ConflictException("The API key has changed since this page was opened. Review the current key before generating another.");
            return await RotateAndSaveAsync(organizationId, agentId, cancellationToken);
        }, cancellationToken);

    private async Task<CreatedAgentSecret> RotateAndSaveAsync(string organizationId, string agentId, CancellationToken ct)
    {
        var rotated = await _agents.RotateKeyAsync(organizationId, agentId, ct);
        await _uow.SaveChangesAsync(ct);
        return rotated;
    }

    public async Task SetEnabledAsync(string organizationId, string agentId, bool enabled, CancellationToken cancellationToken)
    {
        await EnsureCanSetEnabledAsync(organizationId, agentId, enabled, cancellationToken);
        await _agents.SetEnabledAsync(organizationId, agentId, enabled, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureCanSetEnabledAsync(string organizationId, string agentId, bool enabled, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new RequestValidationException("agent_id is required.");
        }

        var agent = await _agents.GetAsync(organizationId, agentId, cancellationToken)
                    ?? throw new NotFoundException("Agent not found.");
        if (!enabled && string.Equals(agent.ExternalId, AccessControlIds.AdminMcpExternalId, StringComparison.Ordinal))
        {
            throw new RequestValidationException("The Administration MCP agent cannot be disabled.");
        }
    }
}
