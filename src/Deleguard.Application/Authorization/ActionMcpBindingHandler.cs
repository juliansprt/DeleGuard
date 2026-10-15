using System.Text.Json;
using System.Text.RegularExpressions;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;

namespace Deleguard.Application.Authorization;

public sealed record CreateMcpBindingCommand(
    string OrganizationId,
    string ActionId,
    string McpServer,
    string McpTool,
    string ResourceIdPath,
    IReadOnlyDictionary<string, string>? AttributePaths);

public sealed class ActionMcpBindingHandler
{
    public const string ServerRules = "lowercase slug, start with a letter, a-z, 0-9, hyphen (e.g. invoice-demo)";
    public const string ToolRules = "start with a letter, A-Z a-z 0-9 _ - (e.g. cancel_invoice)";
    public const string PathRules = "dotted path from tool arguments (e.g. invoice_id or resource.id)";

    private static readonly Regex ServerPattern = new("^[a-z][a-z0-9-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex ToolPattern = new("^[A-Za-z][A-Za-z0-9_-]{0,127}$", RegexOptions.Compiled);
    private static readonly Regex PathPattern = new("^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.Compiled);

    private readonly IActionRegistry _actions;
    private readonly IGuardMappingStore _bindings;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public ActionMcpBindingHandler(
        IActionRegistry actions,
        IGuardMappingStore bindings,
        IClock clock,
        IUnitOfWork uow)
    {
        _actions = actions;
        _bindings = bindings;
        _clock = clock;
        _uow = uow;
    }

    public Task<IReadOnlyList<GuardMappingRecord>> ListAsync(string organizationId, CancellationToken cancellationToken) =>
        _bindings.ListAsync(organizationId, cancellationToken);

    public async Task<ActionMcpBinding> CreateAsync(CreateMcpBindingCommand command, CancellationToken cancellationToken)
    {
        var server = command.McpServer.Trim();
        var tool = command.McpTool.Trim();
        var resourceIdPath = command.ResourceIdPath.Trim();
        if (!ServerPattern.IsMatch(server))
        {
            throw new RequestValidationException($"mcp_server must be {ServerRules}.");
        }

        if (!ToolPattern.IsMatch(tool))
        {
            throw new RequestValidationException($"mcp_tool must be {ToolRules}.");
        }

        if (GuardToolNames.Reserved.Contains(tool) || GuardToolNames.Reserved.Contains(GuardToolNames.Compose(server, tool)))
        {
            throw new RequestValidationException("That MCP tool name is reserved by the Guard PEP.");
        }

        if (!PathPattern.IsMatch(resourceIdPath))
        {
            throw new RequestValidationException($"resource_id_path must be {PathRules}.");
        }

        var action = await _actions.GetAsync(command.OrganizationId, command.ActionId, cancellationToken)
                     ?? throw new NotFoundException("Action not found.");
        if (ActionAdminHandler.IsAdministration(action.Name))
        {
            throw new RequestValidationException("Administration actions cannot be bound to a client Guard.");
        }

        var existing = await _bindings.FindByToolAsync(command.OrganizationId, server, tool, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException("That MCP server/tool pair is already bound.");
        }

        var attributes = NormalizeAttributes(command.AttributePaths);
        var binding = new ActionMcpBinding
        {
            Id = IdGenerator.New("map"),
            OrganizationId = command.OrganizationId,
            ActionDefinitionId = action.Id,
            McpServer = server,
            McpTool = tool,
            ResourceIdPath = resourceIdPath,
            AttributePathsJson = JsonSerializer.Serialize(attributes),
            CreatedAt = _clock.UtcNow
        };
        await _bindings.AddAsync(binding, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return binding;
    }

    public async Task DeleteAsync(string organizationId, string bindingId, CancellationToken cancellationToken)
    {
        var binding = await _bindings.GetAsync(organizationId, bindingId, cancellationToken)
                      ?? throw new NotFoundException("MCP binding not found.");
        await _bindings.DeleteAsync(binding, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    private static Dictionary<string, string> NormalizeAttributes(IReadOnlyDictionary<string, string>? paths)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (paths is null)
        {
            return result;
        }

        if (paths.Count > 32)
        {
            throw new RequestValidationException("At most 32 attribute paths are allowed.");
        }

        foreach (var pair in paths)
        {
            var name = pair.Key.Trim();
            var path = pair.Value.Trim();
            if (string.IsNullOrEmpty(name) || !PathPattern.IsMatch(name.Replace('.', '_')))
            {
                throw new RequestValidationException($"Invalid attribute name '{pair.Key}'.");
            }

            if (!PathPattern.IsMatch(path))
            {
                throw new RequestValidationException($"attribute path for '{name}' must be {PathRules}.");
            }

            result[name] = path;
        }

        return result;
    }
}
