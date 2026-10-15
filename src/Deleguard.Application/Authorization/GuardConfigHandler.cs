using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Authorization;

namespace Deleguard.Application.Authorization;

public sealed record GuardMappingDto(
    string BindingId,
    string Tool,
    string McpServer,
    string McpTool,
    string Action,
    string ResourceType,
    string Risk,
    string SideEffect,
    string ResourceIdPath,
    IReadOnlyDictionary<string, string> AttributePaths);

public sealed record GuardConfigDto(
    string AgentId,
    string Environment,
    bool DefaultDenyUnmapped,
    IReadOnlyList<GuardMappingDto> Mappings);

public static class GuardToolNames
{
    public const string GetDecision = "get_decision";
    public const string Separator = "__";

    public static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        GetDecision,
        "approve",
        "deny",
        "approve_approval",
        "deny_approval"
    };

    public static string Compose(string mcpServer, string mcpTool) => mcpServer + Separator + mcpTool;

    public static bool TryParse(string toolName, out string mcpServer, out string mcpTool)
    {
        mcpServer = string.Empty;
        mcpTool = string.Empty;
        var index = toolName.IndexOf(Separator, StringComparison.Ordinal);
        if (index <= 0 || index + Separator.Length >= toolName.Length)
        {
            return false;
        }

        mcpServer = toolName[..index];
        mcpTool = toolName[(index + Separator.Length)..];
        return mcpServer.Length > 0 && mcpTool.Length > 0;
    }
}

public sealed class GuardConfigHandler
{
    private readonly IGuardMappingStore _mappings;
    private readonly IActionRegistry _actions;
    private readonly IActionGrantStore? _grants;

    public GuardConfigHandler(IGuardMappingStore mappings, IActionRegistry actions, IActionGrantStore? grants = null)
    {
        _mappings = mappings;
        _actions = actions; _grants = grants;
    }

    public async Task<GuardConfigDto> HandleAsync(AgentPrincipal principal, CancellationToken cancellationToken)
    {
        var rows = await _mappings.ListAsync(principal.OrganizationId, cancellationToken);
        var actions = await _actions.ListAsync(principal.OrganizationId, principal.ApplicationId, cancellationToken);
        var allowed = actions.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (_grants is not null)
            foreach (var id in allowed.ToList()) if (!await _grants.HasAsync(principal.OrganizationId, principal.AgentId, id, cancellationToken)) allowed.Remove(id);
        var mappings = rows
            .Where(row => allowed.Contains(row.ActionId) && !ActionAdminHandler.IsAdministration(row.Action))
            .Select(ToDto)
            .ToList();
        return new GuardConfigDto(principal.AgentExternalId, principal.EnvironmentName, true, mappings);
    }

    public static IReadOnlyDictionary<string, string> ParseAttributePaths(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                         ?? new Dictionary<string, string>(StringComparer.Ordinal);
            return new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static GuardMappingDto ToDto(GuardMappingRecord row) =>
        new(
            row.BindingId,
            GuardToolNames.Compose(row.McpServer, row.McpTool),
            row.McpServer,
            row.McpTool,
            row.Action,
            row.ResourceType,
            row.Risk.ToString().ToUpperInvariant(),
            GatewaySideEffects.FromRisk(row.Risk),
            row.ResourceIdPath,
            ParseAttributePaths(row.AttributePathsJson));
}
