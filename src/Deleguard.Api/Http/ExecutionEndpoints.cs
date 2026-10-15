using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;

namespace Deleguard.Api.Http;

public static class ExecutionEndpoints
{
    public static void MapExecutionApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/operations/{operationId}", async (HttpContext h, IOperationLookup operations,
            GetDecisionHandler decisions, string operationId, CancellationToken ct) =>
        {
            var a = Agent(h);
            var id = await operations.FindDecisionAsync(a, operationId, ct);
            return id is null ? Results.NotFound() : Results.Ok(await decisions.HandleAsync(a, id, ct));
        });
        app.MapPost("/v1/permits/{permitId}/consume", async (HttpContext h, IExecutionControl execution,
            string permitId, ConsumePermitRequest body, CancellationToken ct) =>
            Results.Ok(await execution.ConsumeAsync(Agent(h), permitId, body, ct)));
        app.MapPost("/v1/evaluations", async (HttpContext h, AuthorizationEvaluator evaluator,
            IStateCoordinator state, AuthorizeRequestDto body, CancellationToken ct) =>
        {
            var a = Agent(h);
            await state.EnsureActiveAsync(a, ct);
            var result = await evaluator.EvaluateAsync(new(a, body.Agent?.Id ?? a.AgentExternalId, body.Agent?.Environment,
                body.Actor?.Id, body.Actor?.Type, body.Actor?.DelegationId, body.Action, body.Resource.Type,
                body.Resource.Id, body.Resource.Attributes, body.Resource.SnapshotHash, body.Context, "evaluation-only"), ct);
            return Results.Ok(new { decision = DecisionNames.ToApi(result.Effect), result.Reason, result.PolicyId, result.PolicyVersion, simulation = true });
        });
        app.MapGet("/v1/incidents", async (HttpContext h, IExecutionControl e, IApplicationDirectory apps, bool? administration, CancellationToken ct) =>
        {
            h.EnsurePermission(PermissionCatalog.AuditRead);
            var appId = administration == true ? (await apps.GetAdministrationAsync(h.RequireOrganizationId(), ct)).Id : h.RequireProductApplicationId();
            return Results.Ok(await e.IncidentsAsync(h.RequireOrganizationId(), appId, ct));
        });
        app.MapPost("/v1/incidents/{executionId}", async (HttpContext h, IExecutionControl e,
            string executionId, IncidentUpdate body, IApplicationDirectory apps, bool? administration, CancellationToken ct) =>
        {
            h.EnsurePermission(PermissionCatalog.ApprovalsResolve);
            var appId = administration == true ? (await apps.GetAdministrationAsync(h.RequireOrganizationId(), ct)).Id : h.RequireProductApplicationId();
            await e.ResolveIncidentAsync(h.RequireOrganizationId(), appId, executionId,
                h.RequireConsoleUser().Email, body.AssignedTo, body.Resolution, ct);
            return Results.NoContent();
        });
        app.MapPost("/v1/application/suspension", async (HttpContext h, IExecutionControl e, SuspensionUpdate body, CancellationToken ct) =>
        {
            h.EnsurePermission(PermissionCatalog.AgentsWrite);
            await e.SuspendApplicationAsync(h.RequireOrganizationId(), h.RequireProductApplicationId(), body.Suspended, h.RequireConsoleUser().Email, ct);
            return Results.Ok(new { body.Suspended, scope = "new_executions", cancels_provider_requests = false });
        });
    }
    private static AgentPrincipal Agent(HttpContext h) => h.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Agent authentication required.");
    public sealed record IncidentUpdate(string? AssignedTo, string? Resolution);
    public sealed record SuspensionUpdate(bool Suspended);
}
