using System.Text.Json;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Ide;
using Deleguard.Policy;
namespace Deleguard.Application.Abstractions;

public record IdeProjectInput(string Id, string Name, string AgentId, bool CaseSensitive, bool ShareDiff, string[] ProtectedPaths, string[] DeniedPaths);
public record IdeSessionInput(string Id, string ProjectId, string Adapter, string Version, string Platform, Dictionary<string,string> Capabilities);
public record IdeGrantInput(string ProjectId, string Directory, string[] Actions, int Minutes = 15);
public record IdeConsumeInput(string DecisionId, string OperationId, string SnapshotHash);
public record IdeConsumeResult(bool Acquired, bool AlreadyConsumed);
public record IdeProgress(string ProjectId, DateTimeOffset? LastConnection, DateTimeOffset? LastDecision, string? DecisionId, DateTimeOffset? LastReportedResult);
public interface IIdeControl
{
    Task<PolicyEvaluation> EvaluateAsync(AuthorizeCommand command, PolicyEvaluation evaluation, CancellationToken ct);
    Task<IReadOnlyList<IdeProject>> ProjectsAsync(string org, string app, CancellationToken ct);
    Task<IdeProject> SaveProjectAsync(string org, string app, IdeProjectInput input, string actor, CancellationToken ct);
    Task<IdeProject> ConfigAsync(AgentPrincipal agent, string project, CancellationToken ct);
    Task RegisterSessionAsync(AgentPrincipal agent, IdeSessionInput input, CancellationToken ct);
    Task<IReadOnlyList<IdeSession>> SessionsAsync(string org, string app, CancellationToken ct);
    Task<IdeProgress> ProgressAsync(string org, string app, string project, CancellationToken ct);
    Task<IReadOnlyList<IdeGrant>> GrantsAsync(string org, string app, CancellationToken ct);
    Task<IdeGrant> GrantAsync(string org, string app, IdeGrantInput input, string actor, CancellationToken ct);
    Task RevokeAsync(string org, string app, string id, string actor, CancellationToken ct);
    Task<bool> CanReportAsync(AgentPrincipal agent, string decisionId, string snapshot, DateTimeOffset permitExpiresAt, CancellationToken ct);
    Task<IdeConsumeResult> ConsumeAsync(AgentPrincipal agent, IdeConsumeInput input, CancellationToken ct);
}
