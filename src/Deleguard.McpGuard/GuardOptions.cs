using System.Text.Json;

namespace Deleguard.McpGuard;

public sealed class GuardOptions
{
    public const string SectionName = "Deleguard";

    public string Url { get; set; } = "http://127.0.0.1:5080";
    public string ApiKey { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    /// <summary>Required in --http mode. Cursor must send Authorization: Bearer.</summary>
    public string HttpToken { get; set; } = string.Empty;
}

public sealed class DownstreamMcpOptions
{
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "stdio";
    public string? Command { get; set; }
    public string[]? Arguments { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

public sealed class GuardConfigSnapshot
{
    public string AgentId { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public bool DefaultDenyUnmapped { get; init; } = true;
    public IReadOnlyList<GuardMapping> Mappings { get; init; } = [];
}

public sealed class GuardMapping
{
    public string Tool { get; init; } = string.Empty;
    public string McpServer { get; init; } = string.Empty;
    public string McpTool { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string Risk { get; init; } = string.Empty;
    public string SideEffect { get; init; } = string.Empty;
    public string ResourceIdPath { get; init; } = string.Empty;
    public Dictionary<string, string> AttributePaths { get; init; } = new(StringComparer.Ordinal);
}

public sealed record GuardOperation(string Id, string Hash);
public sealed record GuardExecution(string ExecutionId);
public sealed class PdpDecision
{
    public string? PermitId { get; init; }
    public GuardOperation? Operation { get; init; }
    public string? ResourceSnapshotHash { get; init; }
    public GuardExecution? Execution { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string DecisionId { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public string? Permit { get; init; }
    public string? ApprovalId { get; init; }
    public string? PolicyId { get; init; }
    public int? PolicyVersion { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class GuardListedTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonElement InputSchema { get; init; }
}

public sealed class GuardCallResult
{
    public required bool IsError { get; init; }
    public required string Text { get; init; }
    public required JsonElement Structured { get; init; }
    public bool Forwarded { get; init; }
}

public sealed class DownstreamTool
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public JsonElement InputSchema { get; init; }
}

public sealed class DownstreamCallResult
{
    public required bool IsError { get; init; }
    public required string Text { get; init; }
    public JsonElement? Structured { get; init; }
}

public interface IGuardConfigSource
{
    Task<GuardConfigSnapshot> GetAsync(CancellationToken cancellationToken);
    Task<GuardConfigSnapshot> GetFreshAsync(CancellationToken cancellationToken) => GetAsync(cancellationToken);
}

public interface IDeleguardPdp
{
    Task<GuardConfigSnapshot> GetConfigAsync(CancellationToken cancellationToken);
    Task<PdpDecision> AuthorizeAsync(
        string action,
        string resourceType,
        string resourceId,
        Dictionary<string, JsonElement> attributes,
        Dictionary<string, string> context,
        string idempotencyKey,
        string environment,
        CancellationToken cancellationToken);
    Task<PdpDecision> GetDecisionAsync(string decisionId, CancellationToken cancellationToken);
    Task<bool> ConsumeAsync(PdpDecision decision, CancellationToken cancellationToken) => Task.FromResult(false);
    Task ReportExecutionAsync(string decisionId, string status, string? providerReference, CancellationToken cancellationToken);
}

public interface IDownstreamMcp
{
    string Name { get; }
    string ConnectionHash { get; }
    Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken);
    Task<DownstreamCallResult> CallAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken);
}
