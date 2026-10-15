using System.Text.Json;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Approvals;
using Deleguard.Application.Audit;
using Deleguard.Application.Authorization;
using Deleguard.Application.Evidence;
using Deleguard.Application.Identity;
using Deleguard.Application.Notifications;
using Deleguard.Application.Policies;
using Deleguard.Domain.Authorization;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Deleguard.Api.Http;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapDeleguardApi(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapPost("/authorize", AuthorizeAsync).WithName("Authorize").WithTags("Authorization");
        v1.MapGet("/decisions/{decisionId}", GetDecisionAsync).WithName("GetDecision").WithTags("Authorization");
        v1.MapPost("/approvals/{approvalId}/approve", ApproveAsync).WithName("Approve").WithTags("Approvals");
        v1.MapPost("/approvals/{approvalId}/deny", DenyAsync).WithName("DenyApproval").WithTags("Approvals");
        v1.MapPost("/executions", ReportExecutionAsync).WithName("ReportExecution").WithTags("Evidence");
        v1.MapGet("/audit/events", ListAuditAsync).WithName("ListAudit").WithTags("Audit");
        v1.MapGet("/approvals", ListApprovalsAsync).WithName("ListApprovals").WithTags("Approvals");
        v1.MapGet("/agents", ListAgentsAsync).WithName("ListAgents").WithTags("Admin");
        v1.MapPost("/agents", CreateAgentAsync).WithName("CreateAgent").WithTags("Admin");
        v1.MapPost("/agents/{agentId}/rotate-key", RotateAgentKeyAsync).WithName("RotateAgentKey").WithTags("Admin");
        v1.MapPost("/agents/{agentId}/disable", DisableAgentAsync).WithName("DisableAgent").WithTags("Admin");
        v1.MapPost("/agents/{agentId}/enable", EnableAgentAsync).WithName("EnableAgent").WithTags("Admin");
        v1.MapGet("/environments", ListEnvironmentsAsync).WithName("ListEnvironments").WithTags("Admin");
        v1.MapPost("/environments", CreateEnvironmentAsync).WithName("CreateEnvironment").WithTags("Admin");
        v1.MapGet("/environments/{environmentId}", GetEnvironmentAsync).WithName("GetEnvironment").WithTags("Admin");
        v1.MapPatch("/environments/{environmentId}", UpdateEnvironmentAsync).WithName("UpdateEnvironment").WithTags("Admin");
        v1.MapDelete("/environments/{environmentId}", DeleteEnvironmentAsync).WithName("DeleteEnvironment").WithTags("Admin");
        v1.MapGet("/actions", ListActionsAsync).WithName("ListActions").WithTags("Admin");
        v1.MapPost("/actions", CreateActionAsync).WithName("CreateAction").WithTags("Admin");
        v1.MapPatch("/actions/{actionId}", UpdateActionAsync).WithName("UpdateAction").WithTags("Admin");
        v1.MapDelete("/actions/{actionId}", DeleteActionAsync).WithName("DeleteAction").WithTags("Admin");
        v1.MapGet("/actions/mcp-bindings", ListMcpBindingsAsync).WithName("ListMcpBindings").WithTags("Admin");
        v1.MapPost("/actions/{actionId}/mcp-bindings", CreateMcpBindingAsync).WithName("CreateMcpBinding").WithTags("Admin");
        v1.MapDelete("/actions/mcp-bindings/{bindingId}", DeleteMcpBindingAsync).WithName("DeleteMcpBinding").WithTags("Admin");
        v1.MapGet("/guard/config", GetGuardConfigAsync).WithName("GetGuardConfig").WithTags("Authorization");
        v1.MapGet("/gateway/config", GetGatewayConfigAsync).WithName("GetGatewayConfig").WithTags("Authorization");
        v1.MapPost("/gateway/spend/reserve", ReserveGatewaySpendAsync).WithName("ReserveGatewaySpend").WithTags("Authorization");
        v1.MapPost("/gateway/spend/settle", SettleGatewaySpendAsync).WithName("SettleGatewaySpend").WithTags("Authorization");
        v1.MapPost("/gateway/spend/release", ReleaseGatewaySpendAsync).WithName("ReleaseGatewaySpend").WithTags("Authorization");
        v1.MapGet("/gateway/settings", GetGatewaySettingsAsync).WithName("GetGatewaySettings").WithTags("Admin");
        v1.MapPost("/gateway/settings", UpdateGatewaySettingsAsync).WithName("UpdateGatewaySettings").WithTags("Admin");
        v1.MapPost("/gateway/routes", UpsertGatewayRouteAsync).WithName("UpsertGatewayRoute").WithTags("Admin");
        v1.MapDelete("/gateway/routes/{routeId}", DeleteGatewayRouteAsync).WithName("DeleteGatewayRoute").WithTags("Admin");
        v1.MapGet("/policies", ListPoliciesAsync).WithName("ListPolicies").WithTags("Admin");
        v1.MapPost("/policies", CreatePolicyAsync).WithName("CreatePolicy").WithTags("Admin");
        v1.MapGet("/policies/{policyId}", GetPolicyAsync).WithName("GetPolicy").WithTags("Admin");
        v1.MapPost("/policies/{policyId}/versions", AddPolicyVersionAsync).WithName("AddPolicyVersion").WithTags("Admin");
        v1.MapPost("/policies/{policyId}/publish", PublishPolicyAsync).WithName("PublishPolicy").WithTags("Admin");
        v1.MapPost("/policies/{policyId}/test", TestPolicyAsync).WithName("TestPolicy").WithTags("Admin");
        v1.MapDelete("/policies/{policyId}", DeletePolicyAsync).WithName("DeletePolicy").WithTags("Admin");
        v1.MapGet("/users", ListUsersAsync).WithName("ListUsers").WithTags("Admin");
        v1.MapPost("/users", CreateUserAsync).WithName("CreateUser").WithTags("Admin");
        v1.MapPost("/users/{userId}/disable", DisableUserAsync).WithName("DisableUser").WithTags("Admin");
        v1.MapPost("/users/{userId}/roles", AssignUserRolesAsync).WithName("AssignUserRoles").WithTags("Admin");
        v1.MapGet("/roles", ListRolesAsync).WithName("ListRoles").WithTags("Admin");
        v1.MapPost("/roles/{roleId}/permissions", SetRolePermissionsAsync).WithName("SetRolePermissions").WithTags("Admin");
        v1.MapGet("/mcp", GetMcpSettingsAsync).WithName("GetMcpSettings").WithTags("Admin");
        v1.MapPost("/mcp/settings", UpdateMcpSettingsAsync).WithName("UpdateMcpSettings").WithTags("Admin");
        v1.MapGet("/mcp/tokens", ListMcpTokensAsync).WithName("ListMcpTokens").WithTags("Admin");
        v1.MapPost("/mcp/tokens", IssueMcpTokenAsync).WithName("IssueMcpToken").WithTags("Admin");
        v1.MapPost("/mcp/tokens/{credentialId}/revoke", RevokeMcpTokenAsync).WithName("RevokeMcpToken").WithTags("Admin");
        v1.MapGet("/webhooks", ListWebhooksAsync).WithName("ListWebhooks").WithTags("Webhooks");
        v1.MapPost("/webhooks", CreateWebhookAsync).WithName("CreateWebhook").WithTags("Webhooks");
        v1.MapGet("/webhooks/{endpointId}", GetWebhookAsync).WithName("GetWebhook").WithTags("Webhooks");
        v1.MapPatch("/webhooks/{endpointId}", UpdateWebhookAsync).WithName("UpdateWebhook").WithTags("Webhooks");
        v1.MapPost("/webhooks/{endpointId}/rotate-secret", RotateWebhookSecretAsync).WithName("RotateWebhookSecret").WithTags("Webhooks");
        v1.MapPost("/webhooks/{endpointId}/ping", PingWebhookAsync).WithName("PingWebhook").WithTags("Webhooks");
        v1.MapGet("/webhooks/{endpointId}/attempts", ListWebhookAttemptsAsync).WithName("ListWebhookAttempts").WithTags("Webhooks");
        v1.MapPost("/webhooks/{endpointId}/messages/{messageId}/resend", ResendWebhookMessageAsync).WithName("ResendWebhookMessage").WithTags("Webhooks");
        v1.MapPost("/webhooks/{endpointId}/recover", RecoverWebhookAsync).WithName("RecoverWebhook").WithTags("Webhooks");
        return app;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext http,
        AuthorizeHandler handler,
        [FromBody] AuthorizeRequestDto body,
        CancellationToken cancellationToken)
    {
        var principal = http.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Agent authentication required.");
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var key) || string.IsNullOrWhiteSpace(key))
        {
            throw new RequestValidationException("Idempotency-Key header is required.");
        }

        // Legacy IDE attributes are normalized and validated by the same handler.
        if (body.Operation is null && AuthorizeHandler.IsIdeAction(body.Action) && body.Resource.Attributes is { } legacy &&
            legacy.TryGetValue("operation_id", out var legacyId) && legacyId.ValueKind == JsonValueKind.String &&
            legacy.TryGetValue("operation_hash", out var legacyHash) && legacyHash.ValueKind == JsonValueKind.String)
            body.Operation = new OperationRef(legacyId.GetString()!, legacyHash.GetString()!);
        if (body.Operation is null)
            throw new RequestValidationException("operation.id and operation.hash are required. Preserve the identity across retries.");

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = await handler.HandleAsync(new AuthorizeCommand(
            principal,
            body.Agent?.Id ?? principal.AgentExternalId,
            body.Agent?.Environment,
            body.Actor?.Id,
            body.Actor?.Type,
            body.Actor?.DelegationId,
            body.Action,
            body.Resource.Type,
            body.Resource.Id,
            body.Resource.Attributes,
            body.Resource.SnapshotHash,
            body.Context,
            key.ToString(), Operation: body.Operation), cancellationToken);
        var milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        http.Response.Headers["Server-Timing"] = "authorization;dur=" + milliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetDecisionAsync(
        HttpContext http,
        GetDecisionHandler handler,
        string decisionId,
        CancellationToken cancellationToken)
    {
        var principal = http.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Agent authentication required.");
        return Results.Ok(await handler.HandleAsync(principal, decisionId, cancellationToken));
    }

    private static async Task<IResult> ApproveAsync(
        HttpContext http,
        ResolveApprovalHandler handler,
        string approvalId,
        [FromBody] ApproveRequestDto? body,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.ApprovalsResolve);
        var user = http.RequireConsoleUser();
        var result = await handler.HandleAsync(new ResolveApprovalCommand(
            http.RequireConsoleUser().OrganizationId,
            approvalId,
            user.Email,
            user.Roles,
            user.Permissions,
            true,
            body?.ExpiresAt,
            body?.Note), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> DenyAsync(
        HttpContext http,
        ResolveApprovalHandler handler,
        string approvalId,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.ApprovalsResolve);
        var user = http.RequireConsoleUser();
        var result = await handler.HandleAsync(new ResolveApprovalCommand(
            http.RequireConsoleUser().OrganizationId,
            approvalId,
            user.Email,
            user.Roles,
            user.Permissions,
            false,
            null,
            null), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> ReportExecutionAsync(
        HttpContext http,
        ReportExecutionHandler handler,
        [FromBody] ExecutionRequestDto body,
        CancellationToken cancellationToken)
    {
        var principal = http.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Agent authentication required.");
        await handler.HandleAsync(new ReportExecutionCommand(
            principal,
            body.DecisionId,
            body.PermitId,
            body.Status,
            body.ProviderReference,
            body.ResultHash,
            body.ResourceSnapshotHash,
            body.Metadata, ExecutionId: body.ExecutionId), cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> ListAuditAsync(
        HttpContext http,
        ListAuditEventsHandler handler,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.AuditRead);
        var events = await handler.HandleAsync(http.RequireConsoleUser().OrganizationId, 100, cancellationToken);
        return Results.Ok(events.Select(e => new
        {
            event_id = e.Id,
            event_type = e.EventType,
            decision_id = e.DecisionId,
            payload = JsonSerializer.Deserialize<JsonElement>(e.PayloadJson),
            previous_event_hash = e.PreviousEventHash,
            event_hash = e.EventHash,
            created_at = e.CreatedAt
        }));
    }

    private static async Task<IResult> ListApprovalsAsync(
        HttpContext http,
        ListApprovalsHandler handler,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.ApprovalsRead);
        var items = await handler.HandleAsync(http.RequireConsoleUser().OrganizationId, cancellationToken);
        return Results.Ok(items.Select(a => new
        {
            approval_id = a.Id,
            decision_id = a.DecisionId,
            status = a.Status.ToString().ToUpperInvariant(),
            expires_at = a.ExpiresAt,
            facts = JsonSerializer.Deserialize<JsonElement>(a.CanonicalFactsJson)
        }));
    }

    private static async Task<IResult> ListAgentsAsync(
        HttpContext http,
        AgentAdminHandler handler,
        ApplicationAdminHandler applications,
        [FromQuery(Name = "application_id")] string? applicationId,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.AgentsRead);
        var app = await ResolveProductApplicationAsync(http, applications, applicationId, cancellationToken);
        var items = await handler.ListAsync(http.RequireConsoleUser().OrganizationId, app.Id, cancellationToken);
        return Results.Ok(items.Select(a => new
        {
            agent_id = a.Id,
            application_id = a.ApplicationId,
            external_id = a.ExternalId,
            display_name = a.DisplayName,
            environment = a.EnvironmentName,
            enabled = a.Enabled,
            key_prefix = a.ActiveKeyPrefix
        }));
    }

    private static async Task<IResult> CreateAgentAsync(
        HttpContext http,
        AgentAdminHandler handler,
        ApplicationAdminHandler applications,
        IAdminActionGateway gateway,
        [FromBody] CreateAgentDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.AgentsWrite);
        var app = await ResolveProductApplicationAsync(http, applications, body.ApplicationId, cancellationToken);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.agent.create",
            "agent",
            body.ExternalId,
            null,
            PermissionCatalog.AgentsWrite,
            ct => handler.CreateAsync(new CreateAgentCommand(
                http.RequireConsoleUser().OrganizationId,
                app.Id,
                body.ExternalId,
                body.DisplayName,
                body.Environment ?? "production"), ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return Results.Ok(new
            {
                decision = outcome.Decision,
                decision_id = outcome.DecisionId,
                approval_id = outcome.ApprovalId,
                reason = outcome.Reason
            });
        }

        var created = outcome.Value!;
        return Results.Created($"/v1/agents/{created.AgentId}", new
        {
            agent_id = created.AgentId,
            external_id = created.ExternalId,
            api_key = created.ApiKey,
            key_prefix = created.KeyPrefix,
            message = "Store this API key; it is shown only once."
        });
    }

    private static async Task<IResult> RotateAgentKeyAsync(
        HttpContext http,
        AgentAdminHandler handler,
        IAdminActionGateway gateway,
        string agentId,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.AgentsWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.agent.rotate_key",
            "agent",
            agentId,
            null,
            PermissionCatalog.AgentsWrite,
            ct => handler.RotateKeyAsync(http.RequireConsoleUser().OrganizationId, agentId, ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return Results.Ok(new
            {
                decision = outcome.Decision,
                decision_id = outcome.DecisionId,
                approval_id = outcome.ApprovalId,
                reason = outcome.Reason
            });
        }

        var rotated = outcome.Value!;
        return Results.Ok(new
        {
            agent_id = rotated.AgentId,
            external_id = rotated.ExternalId,
            api_key = rotated.ApiKey,
            key_prefix = rotated.KeyPrefix,
            message = "Store this API key; it is shown only once."
        });
    }

    private static Task<IResult> DisableAgentAsync(
        HttpContext http,
        AgentAdminHandler handler,
        IAdminActionGateway gateway,
        string agentId,
        CancellationToken cancellationToken) =>
        SetAgentEnabledAsync(http, handler, gateway, agentId, false, cancellationToken);

    private static Task<IResult> EnableAgentAsync(
        HttpContext http,
        AgentAdminHandler handler,
        IAdminActionGateway gateway,
        string agentId,
        CancellationToken cancellationToken) =>
        SetAgentEnabledAsync(http, handler, gateway, agentId, true, cancellationToken);

    private static async Task<IResult> SetAgentEnabledAsync(
        HttpContext http,
        AgentAdminHandler handler,
        IAdminActionGateway gateway,
        string agentId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.AgentsWrite);
        await handler.EnsureCanSetEnabledAsync(http.RequireConsoleUser().OrganizationId, agentId, enabled, cancellationToken);
        var outcome = await gateway.RunAsync(
            caller,
            enabled ? "admin.agent.enable" : "admin.agent.disable",
            "agent",
            agentId,
            null,
            PermissionCatalog.AgentsWrite,
            async ct =>
            {
                await handler.SetEnabledAsync(http.RequireConsoleUser().OrganizationId, agentId, enabled, ct);
                return true;
            },
            cancellationToken);
        return Results.Ok(new
        {
            decision = outcome.Decision,
            decision_id = outcome.DecisionId,
            approval_id = outcome.ApprovalId,
            reason = outcome.Reason,
            enabled = outcome.Allowed ? enabled : (bool?)null
        });
    }

    private static async Task<IResult> ListEnvironmentsAsync(
        HttpContext http,
        EnvironmentAdminHandler handler,
        ApplicationAdminHandler applications,
        [FromQuery(Name = "application_id")] string? applicationId,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.EnvironmentsRead);
        var app = await ResolveProductApplicationAsync(http, applications, applicationId, cancellationToken);
        var items = await handler.ListAsync(http.RequireConsoleUser().OrganizationId, app.Id, cancellationToken);
        return Results.Ok(items.Select(EnvironmentJson));
    }

    private static async Task<IResult> GetEnvironmentAsync(
        HttpContext http,
        EnvironmentAdminHandler handler,
        string environmentId,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.EnvironmentsRead);
        var item = await handler.GetAsync(http.RequireConsoleUser().OrganizationId, environmentId, cancellationToken);
        return Results.Ok(EnvironmentJson(item));
    }

    private static async Task<IResult> CreateEnvironmentAsync(
        HttpContext http,
        EnvironmentAdminHandler handler,
        ApplicationAdminHandler applications,
        IAdminActionGateway gateway,
        [FromBody] CreateEnvironmentDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.EnvironmentsWrite);
        var name = EnvironmentAdminHandler.NormalizeName(body.Name);
        var app = await ResolveProductApplicationAsync(http, applications, body.ApplicationId, cancellationToken);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.environment.create",
            "environment",
            name,
            null,
            PermissionCatalog.EnvironmentsWrite,
            ct => handler.CreateAsync(http.RequireConsoleUser().OrganizationId, app.Id, name, ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return PendingAdminResult(outcome);
        }

        return Results.Created($"/v1/environments/{outcome.Value!.Id}", EnvironmentJson(outcome.Value));
    }

    private static async Task<IResult> UpdateEnvironmentAsync(
        HttpContext http,
        EnvironmentAdminHandler handler,
        IAdminActionGateway gateway,
        string environmentId,
        [FromBody] UpdateEnvironmentDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.EnvironmentsWrite);
        await handler.GetAsync(http.RequireConsoleUser().OrganizationId, environmentId, cancellationToken);
        var name = EnvironmentAdminHandler.NormalizeName(body.Name);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.environment.update",
            "environment",
            environmentId,
            null,
            PermissionCatalog.EnvironmentsWrite,
            ct => handler.UpdateAsync(http.RequireConsoleUser().OrganizationId, environmentId, name, ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return PendingAdminResult(outcome);
        }

        return Results.Ok(EnvironmentJson(outcome.Value!));
    }

    private static async Task<IResult> DeleteEnvironmentAsync(
        HttpContext http,
        EnvironmentAdminHandler handler,
        IAdminActionGateway gateway,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.EnvironmentsWrite);
        await handler.EnsureCanDeleteAsync(http.RequireConsoleUser().OrganizationId, environmentId, cancellationToken);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.environment.delete",
            "environment",
            environmentId,
            null,
            PermissionCatalog.EnvironmentsWrite,
            async ct =>
            {
                await handler.DeleteAsync(http.RequireConsoleUser().OrganizationId, environmentId, ct);
                return true;
            },
            cancellationToken);
        return Results.Ok(new
        {
            decision = outcome.Decision,
            decision_id = outcome.DecisionId,
            approval_id = outcome.ApprovalId,
            reason = outcome.Reason,
            deleted = outcome.Allowed ? true : (bool?)null
        });
    }

    private static object EnvironmentJson(EnvironmentListItem item) => new
    {
        environment_id = item.Id,
        application_id = item.ApplicationId,
        name = item.Name,
        agent_count = item.AgentCount,
        created_at = item.CreatedAt
    };

    private static async Task<ApplicationListItem> ResolveProductApplicationAsync(
        HttpContext http,
        ApplicationAdminHandler applications,
        string? applicationId,
        CancellationToken cancellationToken)
    {
        var app = await applications.ResolveProductAsync(http.RequireConsoleUser().OrganizationId, applicationId, cancellationToken);
        http.Items["deleguard.admin.target_application"] = app.Id;
        return app;
    }

    private static IResult PendingAdminResult<T>(AdminActionOutcome<T> outcome) =>
        Results.Ok(new
        {
            decision = outcome.Decision,
            decision_id = outcome.DecisionId,
            approval_id = outcome.ApprovalId,
            reason = outcome.Reason
        });

    private static async Task<IResult> ListActionsAsync(
        HttpContext http,
        ActionAdminHandler handler,
        ApplicationAdminHandler applications,
        [FromQuery(Name = "application_id")] string? applicationId,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.ActionsRead);
        var app = await ResolveProductApplicationAsync(http, applications, applicationId, cancellationToken);
        var items = await handler.ListAsync(http.RequireConsoleUser().OrganizationId, app.Id, cancellationToken);
        return Results.Ok(items.Select(a => new
        {
            action_id = a.Id,
            application_id = a.ApplicationId,
            name = a.Name,
            resource_type = a.ResourceType,
            risk = a.Risk.ToString().ToUpperInvariant(),
            administration = ActionAdminHandler.IsAdministration(a.Name)
        }));
    }

    private static async Task<IResult> CreateActionAsync(
        HttpContext http,
        ActionAdminHandler handler,
        ApplicationAdminHandler applications,
        IAdminActionGateway gateway,
        [FromBody] CreateActionDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.ActionsWrite);
        var app = await ResolveProductApplicationAsync(http, applications, body.ApplicationId, cancellationToken);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.action.create",
            "action",
            body.Name,
            null,
            PermissionCatalog.ActionsWrite,
            ct => handler.CreateAsync(new CreateActionCommand(
                http.RequireConsoleUser().OrganizationId,
                app.Id,
                body.Name,
                body.ResourceType,
                body.Risk,
                body.AgentIds ?? []), ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return Results.Ok(new
            {
                decision = outcome.Decision,
                decision_id = outcome.DecisionId,
                approval_id = outcome.ApprovalId,
                reason = outcome.Reason
            });
        }

        var created = outcome.Value!;
        return Results.Created($"/v1/actions/{created.Id}", new
        {
            action_id = created.Id,
            name = created.Name,
            resource_type = created.ResourceType,
            risk = created.Risk.ToString().ToUpperInvariant()
        });
    }

    private static async Task<IResult> UpdateActionAsync(
        HttpContext http,
        ActionAdminHandler handler,
        IAdminActionGateway gateway,
        string actionId,
        [FromBody] UpdateActionDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.ActionsWrite);
        await handler.EnsureCanUpdateAsync(http.RequireConsoleUser().OrganizationId, actionId, cancellationToken);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.action.update",
            "action",
            actionId,
            null,
            PermissionCatalog.ActionsWrite,
            ct => handler.UpdateAsync(new UpdateActionCommand(
                http.RequireConsoleUser().OrganizationId,
                actionId,
                body.ResourceType,
                body.Risk,
                body.AgentIds), ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return PendingAdminResult(outcome);
        }

        var updated = outcome.Value!;
        return Results.Ok(new
        {
            action_id = updated.Id,
            name = updated.Name,
            resource_type = updated.ResourceType,
            risk = updated.Risk.ToString().ToUpperInvariant()
        });
    }

    private static async Task<IResult> DeleteActionAsync(
        HttpContext http,
        ActionAdminHandler handler,
        IAdminActionGateway gateway,
        string actionId,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.ActionsWrite);
        await handler.EnsureCanDeleteAsync(http.RequireConsoleUser().OrganizationId, actionId, cancellationToken);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.action.delete",
            "action",
            actionId,
            null,
            PermissionCatalog.ActionsWrite,
            async ct =>
            {
                await handler.DeleteAsync(http.RequireConsoleUser().OrganizationId, actionId, ct);
                return true;
            },
            cancellationToken);
        return Results.Ok(new
        {
            decision = outcome.Decision,
            decision_id = outcome.DecisionId,
            approval_id = outcome.ApprovalId,
            reason = outcome.Reason,
            deleted = outcome.Allowed ? true : (bool?)null
        });
    }

    private static async Task<IResult> GetGuardConfigAsync(
        HttpContext http,
        GuardConfigHandler handler,
        CancellationToken cancellationToken)
    {
        var principal = http.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Agent authentication required.");
        return Results.Ok(await handler.HandleAsync(principal, cancellationToken));
    }

    private static async Task<IResult> GetGatewayConfigAsync(
        HttpContext http,
        GatewayConfigHandler handler,
        CancellationToken cancellationToken)
    {
        var principal = http.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Agent authentication required.");
        return Results.Ok(await handler.HandleAsync(principal, cancellationToken));
    }

    private static async Task<IResult> ReserveGatewaySpendAsync(
        HttpContext http,
        GatewaySpendHandler handler,
        [FromBody] GatewaySpendReserveDto body,
        CancellationToken cancellationToken)
    {
        var principal = http.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Agent authentication required.");
        var reserved = await handler.ReserveAsync(new ReserveGatewaySpendCommand(principal, body.DecisionId, body.EstimatedCostUsd), cancellationToken);
        return Results.Ok(new { id = reserved.Id, decision_id = reserved.DecisionId, reserved_usd = reserved.ReservedUsd, status = reserved.Status });
    }

    private static async Task<IResult> SettleGatewaySpendAsync(
        HttpContext http,
        GatewaySpendHandler handler,
        [FromBody] GatewaySpendSettleDto body,
        CancellationToken cancellationToken)
    {
        var principal = http.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Agent authentication required.");
        var settled = await handler.SettleAsync(new SettleGatewaySpendCommand(principal, body.DecisionId, body.ActualCostUsd), cancellationToken);
        return Results.Ok(new { id = settled.Id, decision_id = settled.DecisionId, settled_usd = settled.SettledUsd, status = settled.Status });
    }

    private static async Task<IResult> ReleaseGatewaySpendAsync(
        HttpContext http,
        GatewaySpendHandler handler,
        [FromBody] GatewaySpendReleaseDto body,
        CancellationToken cancellationToken)
    {
        var principal = http.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Agent authentication required.");
        await handler.ReleaseAsync(principal, body.DecisionId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetGatewaySettingsAsync(HttpContext http, GatewayAdminHandler handler, CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.GatewayRead);
        return Results.Ok(await handler.GetSettingsAsync(http.RequireConsoleUser().OrganizationId, cancellationToken));
    }

    private static async Task<IResult> UpdateGatewaySettingsAsync(
        HttpContext http,
        GatewayAdminHandler handler,
        IAdminActionGateway gateway,
        [FromBody] UpdateGatewaySettingsDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.GatewayWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.gateway.update_settings",
            "gateway",
            "settings",
            null,
            PermissionCatalog.GatewayWrite,
            ct => handler.UpdateSettingsAsync(
                http.RequireConsoleUser().OrganizationId,
                body.ToUpdate(),
                ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return Results.Ok(new { decision = outcome.Decision, decision_id = outcome.DecisionId, approval_id = outcome.ApprovalId, reason = outcome.Reason });
        }

        return Results.Ok(outcome.Value);
    }

    private static async Task<IResult> UpsertGatewayRouteAsync(
        HttpContext http,
        GatewayAdminHandler handler,
        IAdminActionGateway gateway,
        [FromBody] UpsertGatewayRouteDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.GatewayWrite);
        var resourceId = string.IsNullOrWhiteSpace(body.Id) ? body.Alias : body.Id;
        var outcome = await gateway.RunAsync(
            caller,
            "admin.gateway.upsert_route",
            "gateway",
            resourceId,
            null,
            PermissionCatalog.GatewayWrite,
            ct => handler.UpsertRouteAsync(
                http.RequireConsoleUser().OrganizationId,
                body.ToWrite(),
                ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return Results.Ok(new { decision = outcome.Decision, decision_id = outcome.DecisionId, approval_id = outcome.ApprovalId, reason = outcome.Reason });
        }

        return Results.Ok(outcome.Value);
    }

    private static async Task<IResult> DeleteGatewayRouteAsync(
        HttpContext http,
        GatewayAdminHandler handler,
        IAdminActionGateway gateway,
        string routeId,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.GatewayWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.gateway.delete_route",
            "gateway",
            routeId,
            null,
            PermissionCatalog.GatewayWrite,
            ct => handler.DeleteRouteAsync(http.RequireConsoleUser().OrganizationId, routeId, ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return Results.Ok(new { decision = outcome.Decision, decision_id = outcome.DecisionId, approval_id = outcome.ApprovalId, reason = outcome.Reason });
        }

        return Results.Ok(outcome.Value);
    }

    private static async Task<IResult> ListMcpBindingsAsync(
        HttpContext http,
        ActionMcpBindingHandler handler,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.ActionsRead);
        var items = await handler.ListAsync(http.RequireConsoleUser().OrganizationId, cancellationToken);
        return Results.Ok(items.Select(BindingJson));
    }

    private static async Task<IResult> CreateMcpBindingAsync(
        HttpContext http,
        ActionMcpBindingHandler handler,
        string actionId,
        [FromBody] CreateMcpBindingDto body,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.ActionsWrite);
        var created = await handler.CreateAsync(new CreateMcpBindingCommand(
            http.RequireConsoleUser().OrganizationId,
            actionId,
            body.McpServer,
            body.McpTool,
            body.ResourceIdPath,
            body.AttributePaths), cancellationToken);
        var listed = await handler.ListAsync(http.RequireConsoleUser().OrganizationId, cancellationToken);
        var row = listed.First(item => item.BindingId == created.Id);
        return Results.Created($"/v1/actions/mcp-bindings/{created.Id}", BindingJson(row));
    }

    private static async Task<IResult> DeleteMcpBindingAsync(
        HttpContext http,
        ActionMcpBindingHandler handler,
        string bindingId,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.ActionsWrite);
        await handler.DeleteAsync(http.RequireConsoleUser().OrganizationId, bindingId, cancellationToken);
        return Results.NoContent();
    }

    private static object BindingJson(GuardMappingRecord item) => new
    {
        binding_id = item.BindingId,
        action_id = item.ActionId,
        action = item.Action,
        resource_type = item.ResourceType,
        mcp_server = item.McpServer,
        mcp_tool = item.McpTool,
        tool = GuardToolNames.Compose(item.McpServer, item.McpTool),
        resource_id_path = item.ResourceIdPath,
        attribute_paths = GuardConfigHandler.ParseAttributePaths(item.AttributePathsJson)
    };

    private static async Task<IResult> ListPoliciesAsync(
        HttpContext http,
        PolicyAdminHandler handler,
        ApplicationAdminHandler applications,
        [FromQuery(Name = "application_id")] string? applicationId,
        CancellationToken cancellationToken)
    {
        http.EnsureConsole();
        var app = await ResolveProductApplicationAsync(http, applications, applicationId, cancellationToken);
        var items = await handler.ListAsync(http.RequireConsoleUser().OrganizationId, app.Id, cancellationToken);
        return Results.Ok(items.Select(p => new
        {
            policy_id = p.Id,
            application_id = p.ApplicationId,
            slug = p.Slug,
            published_version_id = p.PublishedVersionId,
            published_version = p.PublishedVersionNumber,
            version_count = p.VersionCount,
            action = p.Action,
            resource_type = p.ResourceType
        }));
    }

    private static async Task<IResult> CreatePolicyAsync(
        HttpContext http,
        PolicyAdminHandler handler,
        ApplicationAdminHandler applications,
        [FromBody] PolicyYamlDto body,
        CancellationToken cancellationToken)
    {
        http.EnsureConsole();
        var app = await ResolveProductApplicationAsync(http, applications, body.ApplicationId, cancellationToken);
        var created = await handler.CreateAsync(new CreatePolicyCommand(
            http.RequireConsoleUser().OrganizationId,
            app.Id,
            body.Yaml,
            body.AgentIds ?? []), cancellationToken);
        return Results.Created($"/v1/policies/{created.Policy.Id}", new
        {
            policy_id = created.Policy.Id,
            slug = created.Policy.Slug,
            version_id = created.Version.Id,
            published = false
        });
    }

    private static async Task<IResult> GetPolicyAsync(
        HttpContext http,
        PolicyAdminHandler handler,
        string policyId,
        CancellationToken cancellationToken)
    {
        http.EnsureConsole();
        var (policy, versions) = await handler.GetAsync(http.RequireConsoleUser().OrganizationId, policyId, cancellationToken);
        return Results.Ok(new
        {
            policy_id = policy.Id,
            slug = policy.Slug,
            published_version_id = policy.PublishedVersionId,
            versions = versions.Select(v => new
            {
                version_id = v.Id,
                version = v.Version,
                action = v.Action,
                resource_type = v.ResourceType,
                published = v.Published,
                yaml = v.Yaml,
                created_at = v.CreatedAt,
                published_at = v.PublishedAt
            })
        });
    }

    private static async Task<IResult> AddPolicyVersionAsync(
        HttpContext http,
        PolicyAdminHandler handler,
        string policyId,
        [FromBody] PolicyYamlDto body,
        CancellationToken cancellationToken)
    {
        http.EnsureConsole();
        var version = await handler.AddVersionAsync(new AddPolicyVersionCommand(http.RequireConsoleUser().OrganizationId, policyId, body.Yaml), cancellationToken);
        return Results.Created($"/v1/policies/{policyId}/versions/{version.Id}", new
        {
            version_id = version.Id,
            version = version.Version,
            published = version.Published
        });
    }

    private static async Task<IResult> PublishPolicyAsync(
        HttpContext http,
        PolicyAdminHandler handler,
        IAdminActionGateway gateway,
        string policyId,
        [FromBody] PublishPolicyDto? body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body?.VersionId)) throw new RequestValidationException("version_id is required.");
        var caller = http.EnsurePermission(PermissionCatalog.PoliciesPublish);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.policy.publish",
            "policy",
            policyId,
            null,
            PermissionCatalog.PoliciesPublish,
            ct => handler.PublishAsync(new PublishPolicyCommand(http.RequireConsoleUser().OrganizationId, policyId, body?.VersionId), ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return Results.Ok(new
            {
                decision = outcome.Decision,
                decision_id = outcome.DecisionId,
                approval_id = outcome.ApprovalId,
                reason = outcome.Reason
            });
        }

        var version = outcome.Value!;
        return Results.Ok(new
        {
            policy_id = policyId,
            version_id = version.Id,
            version = version.Version,
            published = true
        });
    }

    private static async Task<IResult> TestPolicyAsync(
        HttpContext http,
        PolicyAdminHandler handler,
        string policyId,
        [FromBody] PolicyTestDto body,
        CancellationToken cancellationToken)
    {
        http.EnsureConsole();
        var result = await handler.TestAsync(new PolicyTestCommand(
            http.RequireConsoleUser().OrganizationId,
            policyId,
            body.Yaml,
            body.ExpectedEffect,
            body.Request?.AgentId,
            body.Request?.Environment,
            body.Request?.Action,
            body.Request?.ResourceType,
            body.Request?.ResourceId,
            body.Request?.Attributes,
            body.Request?.Context), cancellationToken);
        return Results.Ok(new
        {
            passed = result.Passed,
            actual_effect = result.ActualEffect,
            expected_effect = result.ExpectedEffect,
            reason = result.Reason,
            policy_id = result.PolicyId,
            policy_version = result.PolicyVersion,
            matched_rule = result.MatchedRule
        });
    }

    private static async Task<IResult> DeletePolicyAsync(
        HttpContext http,
        PolicyAdminHandler handler,
        IAdminActionGateway gateway,
        string policyId,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.PoliciesWrite);
        await handler.EnsureCanDeleteAsync(http.RequireConsoleUser().OrganizationId, policyId, cancellationToken);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.policy.delete",
            "policy",
            policyId,
            null,
            PermissionCatalog.PoliciesWrite,
            async ct =>
            {
                await handler.DeleteAsync(http.RequireConsoleUser().OrganizationId, policyId, ct);
                return true;
            },
            cancellationToken);
        return Results.Ok(new
        {
            decision = outcome.Decision,
            decision_id = outcome.DecisionId,
            approval_id = outcome.ApprovalId,
            reason = outcome.Reason,
            deleted = outcome.Allowed ? true : (bool?)null
        });
    }

    private static async Task<IResult> ListUsersAsync(HttpContext http, UserAdminHandler handler,
 CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.UsersRead);
        var items = await handler.ListAsync(http.RequireConsoleUser().OrganizationId, cancellationToken);
        return Results.Ok(items.Select(u => new
        {
            user_id = u.Id,
            email = u.Email,
            display_name = u.DisplayName,
            enabled = u.Enabled,
            roles = u.Roles
        }));
    }

    private static async Task<IResult> CreateUserAsync(
        HttpContext http,
        UserAdminHandler handler,
        IAdminActionGateway gateway,
        [FromBody] CreateUserDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.UsersWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.user.create",
            "user",
            body.Email,
            null,
            PermissionCatalog.UsersWrite,
            ct => handler.CreateAsync(http.RequireConsoleUser().OrganizationId, body.Email, body.DisplayName, body.Roles ?? [], body.Password, ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return Results.Ok(new { decision = outcome.Decision, decision_id = outcome.DecisionId, approval_id = outcome.ApprovalId, reason = outcome.Reason });
        }

        return Results.Created($"/v1/users/{outcome.Value!.UserId}", new
        {
            user_id = outcome.Value.UserId,
            email = outcome.Value.Email,
            temporary_password = outcome.Value.TemporaryPassword,
            message = outcome.Value.TemporaryPassword is null
                ? "Invite created. They must sign in with SSO using this verified email."
                : "Store this password; it is shown only once."
        });
    }

    private static async Task<IResult> DisableUserAsync(
        HttpContext http,
        UserAdminHandler handler,
        IAdminActionGateway gateway,
        string userId,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.UsersWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.user.disable",
            "user",
            userId,
            null,
            PermissionCatalog.UsersWrite,
            async ct =>
            {
                await handler.DisableAsync(http.RequireConsoleUser().OrganizationId, userId, ct);
                return true;
            },
            cancellationToken);
        return Results.Ok(new { decision = outcome.Decision, decision_id = outcome.DecisionId, approval_id = outcome.ApprovalId, reason = outcome.Reason });
    }

    private static async Task<IResult> AssignUserRolesAsync(
        HttpContext http,
        UserAdminHandler handler,
        IAdminActionGateway gateway,
        string userId,
        [FromBody] AssignRolesDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.UsersWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.role.assign",
            "role",
            userId,
            null,
            PermissionCatalog.UsersWrite,
            async ct =>
            {
                await handler.AssignRolesAsync(http.RequireConsoleUser().OrganizationId, userId, body.Roles ?? [], ct);
                return true;
            },
            cancellationToken);
        return Results.Ok(new { decision = outcome.Decision, decision_id = outcome.DecisionId, approval_id = outcome.ApprovalId, reason = outcome.Reason });
    }

    private static async Task<IResult> ListRolesAsync(HttpContext http, RoleAdminHandler handler,
 CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.RolesRead);
        var items = await handler.ListAsync(http.RequireConsoleUser().OrganizationId, cancellationToken);
        return Results.Ok(items.Select(r => new
        {
            role_id = r.Id,
            slug = r.Slug,
            display_name = r.DisplayName,
            permissions = r.Permissions
        }));
    }

    private static async Task<IResult> SetRolePermissionsAsync(
        HttpContext http,
        RoleAdminHandler handler,
        IAdminActionGateway gateway,
        string roleId,
        [FromBody] SetPermissionsDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.RolesWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.role.assign",
            "role",
            roleId,
            null,
            PermissionCatalog.RolesWrite,
            async ct =>
            {
                await handler.SetPermissionsAsync(http.RequireConsoleUser().OrganizationId, roleId, body.Permissions ?? [], ct);
                return true;
            },
            cancellationToken);
        return Results.Ok(new { decision = outcome.Decision, decision_id = outcome.DecisionId, approval_id = outcome.ApprovalId, reason = outcome.Reason });
    }

    private static async Task<IResult> GetMcpSettingsAsync(HttpContext http, McpAdminHandler handler,
 CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.McpRead);
        var settings = await handler.GetSettingsAsync(http.RequireConsoleUser().OrganizationId, cancellationToken);
        return Results.Ok(new
        {
            enabled = settings.Enabled,
            agent_id = settings.AgentId,
            agent_external_id = settings.AgentExternalId,
            enabled_tools = settings.EnabledTools,
            available_tools = settings.AvailableTools
        });
    }

    private static async Task<IResult> UpdateMcpSettingsAsync(
        HttpContext http,
        McpAdminHandler handler,
        IAdminActionGateway gateway,
        [FromBody] UpdateMcpSettingsDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.McpWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.mcp.set_tools",
            "mcp",
            "settings",
            null,
            PermissionCatalog.McpWrite,
            ct => handler.UpdateSettingsAsync(http.RequireConsoleUser().OrganizationId, body.Enabled, body.Tools ?? [], ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return Results.Ok(new { decision = outcome.Decision, decision_id = outcome.DecisionId, approval_id = outcome.ApprovalId, reason = outcome.Reason });
        }

        var settings = outcome.Value!;
        return Results.Ok(new { enabled = settings.Enabled, enabled_tools = settings.EnabledTools });
    }

    private static async Task<IResult> ListMcpTokensAsync(HttpContext http, McpAdminHandler handler,
 CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.McpRead);
        var items = await handler.ListCredentialsAsync(http.RequireConsoleUser().OrganizationId, cancellationToken);
        return Results.Ok(items.Select(t => new
        {
            credential_id = t.Id,
            user_id = t.UserId,
            user_email = t.UserEmail,
            key_prefix = t.KeyPrefix,
            created_at = t.CreatedAt,
            expires_at = t.ExpiresAt,
            revoked_at = t.RevokedAt
        }));
    }

    private static async Task<IResult> IssueMcpTokenAsync(
        HttpContext http,
        McpAdminHandler handler,
        IAdminActionGateway gateway,
        [FromBody] IssueMcpTokenDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.McpWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.mcp.issue_token",
            "mcp",
            body.UserId,
            null,
            PermissionCatalog.McpWrite,
            ct => handler.IssueTokenAsync(http.RequireConsoleUser().OrganizationId, body.UserId, body.ExpiresAt, ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return Results.Ok(new { decision = outcome.Decision, decision_id = outcome.DecisionId, approval_id = outcome.ApprovalId, reason = outcome.Reason });
        }

        return Results.Ok(new
        {
            credential_id = outcome.Value!.CredentialId,
            user_id = outcome.Value.UserId,
            token = outcome.Value.Token,
            key_prefix = outcome.Value.KeyPrefix,
            message = "Store this MCP token; it is shown only once."
        });
    }

    private static async Task<IResult> RevokeMcpTokenAsync(
        HttpContext http,
        McpAdminHandler handler,
        IAdminActionGateway gateway,
        string credentialId,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.McpWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.mcp.revoke_token",
            "mcp",
            credentialId,
            null,
            PermissionCatalog.McpWrite,
            async ct =>
            {
                await handler.RevokeTokenAsync(http.RequireConsoleUser().OrganizationId, credentialId, ct);
                return true;
            },
            cancellationToken);
        return Results.Ok(new { decision = outcome.Decision, decision_id = outcome.DecisionId, approval_id = outcome.ApprovalId, reason = outcome.Reason });
    }

    private static async Task<IResult> ListWebhooksAsync(
        HttpContext http,
        WebhookAdminHandler handler,
        ApplicationAdminHandler applications,
        [FromQuery(Name = "application_id")] string? applicationId,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.WebhooksRead);
        var app = await ResolveProductApplicationAsync(http, applications, applicationId, cancellationToken);
        var items = await handler.ListAsync(http.RequireConsoleUser().OrganizationId, app.Id, cancellationToken);
        return Results.Ok(items.Select(WebhookJson));
    }

    private static async Task<IResult> CreateWebhookAsync(
        HttpContext http,
        WebhookAdminHandler handler,
        ApplicationAdminHandler applications,
        IAdminActionGateway gateway,
        [FromBody] CreateWebhookDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.WebhooksWrite);
        var app = await ResolveProductApplicationAsync(http, applications, body.ApplicationId, cancellationToken);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.webhook.create",
            "webhook",
            body.Url,
            null,
            PermissionCatalog.WebhooksWrite,
            ct => handler.CreateAsync(new CreateWebhookCommand(
                http.RequireConsoleUser().OrganizationId,
                app.Id,
                body.Description,
                body.Url,
                body.EventTypes ?? []), ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return AdminPendingOrDeny(outcome);
        }

        var created = outcome.Value!;
        return Results.Created($"/v1/webhooks/{created.EndpointId}", new
        {
            endpoint_id = created.EndpointId,
            secret = created.Secret,
            secret_prefix = created.SecretPrefix,
            message = "Store this signing secret; it is shown only once. Verify Standard Webhooks signatures with webhook-id, webhook-timestamp, and webhook-signature."
        });
    }

    private static async Task<IResult> GetWebhookAsync(
        HttpContext http,
        WebhookAdminHandler handler,
        string endpointId,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.WebhooksRead);
        return Results.Ok(WebhookJson(await handler.GetAsync(http.RequireConsoleUser().OrganizationId, endpointId, cancellationToken)));
    }

    private static async Task<IResult> UpdateWebhookAsync(
        HttpContext http,
        WebhookAdminHandler handler,
        IAdminActionGateway gateway,
        string endpointId,
        [FromBody] UpdateWebhookDto body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.WebhooksWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.webhook.update",
            "webhook",
            endpointId,
            null,
            PermissionCatalog.WebhooksWrite,
            ct => handler.UpdateAsync(new UpdateWebhookCommand(
                http.RequireConsoleUser().OrganizationId,
                endpointId,
                body.Description,
                body.Url,
                body.EventTypes,
                body.Status), ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return AdminPendingOrDeny(outcome);
        }

        return Results.Ok(WebhookJson(outcome.Value!));
    }

    private static async Task<IResult> RotateWebhookSecretAsync(
        HttpContext http,
        WebhookAdminHandler handler,
        IAdminActionGateway gateway,
        string endpointId,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.WebhooksWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.webhook.rotate_secret",
            "webhook",
            endpointId,
            null,
            PermissionCatalog.WebhooksWrite,
            ct => handler.RotateSecretAsync(http.RequireConsoleUser().OrganizationId, endpointId, ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return AdminPendingOrDeny(outcome);
        }

        var rotated = outcome.Value!;
        return Results.Ok(new
        {
            endpoint_id = rotated.EndpointId,
            secret = rotated.Secret,
            secret_prefix = rotated.SecretPrefix,
            message = "Store this signing secret; it is shown only once. The previous secret remains valid until the next rotation."
        });
    }

    private static async Task<IResult> PingWebhookAsync(
        HttpContext http,
        WebhookAdminHandler handler,
        IAdminActionGateway gateway,
        string endpointId,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.WebhooksWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.webhook.ping",
            "webhook",
            endpointId,
            null,
            PermissionCatalog.WebhooksWrite,
            async ct =>
            {
                await handler.PingAsync(http.RequireConsoleUser().OrganizationId, endpointId, ct);
                return true;
            },
            cancellationToken);
        return outcome.Allowed ? Results.Accepted() : AdminPendingOrDeny(outcome);
    }

    private static async Task<IResult> ListWebhookAttemptsAsync(
        HttpContext http,
        WebhookAdminHandler handler,
        string endpointId,
        CancellationToken cancellationToken)
    {
        http.EnsurePermission(PermissionCatalog.WebhooksRead);
        var items = await handler.ListAttemptsAsync(http.RequireConsoleUser().OrganizationId, endpointId, cancellationToken);
        return Results.Ok(items.Select(a => new
        {
            attempt_id = a.Id,
            message_id = a.MessageId,
            event_type = a.EventType,
            attempt_number = a.AttemptNumber,
            http_status = a.HttpStatus,
            error = a.Error,
            response_body = a.ResponseBody,
            duration_ms = a.DurationMs,
            success = a.Success,
            finished_at = a.FinishedAt
        }));
    }

    private static async Task<IResult> ResendWebhookMessageAsync(
        HttpContext http,
        WebhookAdminHandler handler,
        IAdminActionGateway gateway,
        string endpointId,
        string messageId,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.WebhooksWrite);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.webhook.resend",
            "webhook",
            endpointId,
            null,
            PermissionCatalog.WebhooksWrite,
            async ct =>
            {
                await handler.ResendAsync(http.RequireConsoleUser().OrganizationId, endpointId, messageId, ct);
                return true;
            },
            cancellationToken);
        return outcome.Allowed ? Results.Accepted() : AdminPendingOrDeny(outcome);
    }

    private static async Task<IResult> RecoverWebhookAsync(
        HttpContext http,
        WebhookAdminHandler handler,
        IAdminActionGateway gateway,
        string endpointId,
        [FromBody] RecoverWebhookDto? body,
        CancellationToken cancellationToken)
    {
        var caller = http.EnsurePermission(PermissionCatalog.WebhooksWrite);
        var since = WebhookAdminHandler.ParseRecoveryInstant(body?.Since, "since");
        var until = WebhookAdminHandler.ParseRecoveryInstant(body?.Until, "until");
        WebhookAdminHandler.ValidateRecoveryRange(since, until);
        var outcome = await gateway.RunAsync(
            caller,
            "admin.webhook.recover",
            "webhook",
            endpointId,
            null,
            PermissionCatalog.WebhooksWrite,
            ct => handler.RecoverAsync(http.RequireConsoleUser().OrganizationId, endpointId, since, until, ct),
            cancellationToken);
        if (!outcome.Allowed)
        {
            return AdminPendingOrDeny(outcome);
        }

        return Results.Ok(new { recovered = outcome.Value, since, until });
    }

    private static IResult AdminPendingOrDeny<T>(AdminActionOutcome<T> outcome) =>
        Results.Ok(new
        {
            decision = outcome.Decision,
            decision_id = outcome.DecisionId,
            approval_id = outcome.ApprovalId,
            reason = outcome.Reason
        });

    private static object WebhookJson(WebhookEndpointListItem item) => new
    {
        endpoint_id = item.Id,
        description = item.Description,
        url = item.Url,
        status = item.Status,
        event_types = item.EventTypes,
        consecutive_failures = item.ConsecutiveFailures,
        secret_prefix = item.SecretPrefix,
        created_at = item.CreatedAt,
        updated_at = item.UpdatedAt
    };
}

public sealed class AuthorizeRequestDto
{
    public OperationRef? Operation { get; set; }
    public AgentRefDto? Agent { get; set; }
    public ActorRefDto? Actor { get; set; }
    public string Action { get; set; } = string.Empty;
    public ResourceDto Resource { get; set; } = new();
    public Dictionary<string, JsonElement>? Context { get; set; }
}

public sealed class AgentRefDto
{
    public string Id { get; set; } = string.Empty;
    public string? Environment { get; set; }
}

public sealed class ActorRefDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "human";
    public string? DelegationId { get; set; }
}

public sealed class ResourceDto
{
    public string Type { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, JsonElement>? Attributes { get; set; }
    public string? SnapshotHash { get; set; }
}

public sealed class ApproveRequestDto
{
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Note { get; set; }
}

public sealed class ExecutionRequestDto
{
    public string? ExecutionId { get; set; }
    public string DecisionId { get; set; } = string.Empty;
    public string? PermitId { get; set; }
    public string Status { get; set; } = "SUCCESS";
    public string? ProviderReference { get; set; }
    public string? ResultHash { get; set; }
    public string? ResourceSnapshotHash { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

public sealed class CreateAgentDto
{
    public string? ApplicationId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Environment { get; set; }
}

public sealed class CreateEnvironmentDto
{
    public string? ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class UpdateEnvironmentDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class CreateActionDto
{
    public string? ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string Risk { get; set; } = "MEDIUM";
    public List<string>? AgentIds { get; set; }
}

public sealed class UpdateActionDto
{
    public string ResourceType { get; set; } = string.Empty;
    public string Risk { get; set; } = "MEDIUM";
    public List<string>? AgentIds { get; set; }
}

public sealed class CreateMcpBindingDto
{
    public string McpServer { get; set; } = string.Empty;
    public string McpTool { get; set; } = string.Empty;
    public string ResourceIdPath { get; set; } = string.Empty;
    public Dictionary<string, string>? AttributePaths { get; set; }
}

public sealed class PolicyYamlDto
{
    public string? ApplicationId { get; set; }
    public string Yaml { get; set; } = string.Empty;
    public List<string>? AgentIds { get; set; }
}

public sealed class PublishPolicyDto
{
    public string? VersionId { get; set; }
}

public sealed class PolicyTestDto
{
    public string? Yaml { get; set; }
    public string? ExpectedEffect { get; set; }
    public PolicyTestRequestDto? Request { get; set; }
}

public sealed class PolicyTestRequestDto
{
    public string? AgentId { get; set; }
    public string? Environment { get; set; }
    public string? Action { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public Dictionary<string, JsonElement>? Attributes { get; set; }
    public Dictionary<string, JsonElement>? Context { get; set; }
}

public sealed class CreateWebhookDto
{
    public string? ApplicationId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<string>? EventTypes { get; set; }
}

public sealed class UpdateWebhookDto
{
    public string? Description { get; set; }
    public string? Url { get; set; }
    public List<string>? EventTypes { get; set; }
    public string? Status { get; set; }
}

public sealed class RecoverWebhookDto
{
    public string? Since { get; set; }
    public string? Until { get; set; }
}

public sealed class CreateUserDto
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string>? Roles { get; set; }
    public string? Password { get; set; }
}

public sealed class AssignRolesDto
{
    public List<string>? Roles { get; set; }
}

public sealed class SetPermissionsDto
{
    public List<string>? Permissions { get; set; }
}

public sealed class UpdateMcpSettingsDto
{
    public bool Enabled { get; set; }
    public List<string>? Tools { get; set; }
}

public sealed class UpdateGatewaySettingsDto
{
    public bool Enabled { get; set; }
    public string RoutingMode { get; set; } = "static";
    public decimal MonthlyBudgetUsd { get; set; }
    public string DefaultTokenBudgetClass { get; set; } = "standard";
    public int? ShortMaxOutputTokens { get; set; }
    public int? StandardMaxOutputTokens { get; set; }
    public int? DeepMaxOutputTokens { get; set; }
    public int? HeuristicCheapMaxTokens { get; set; }
    public int? CascadeSkipToStrongTokens { get; set; }
    public int? CascadeMaxAttempts { get; set; }
    public decimal? CascadeMaxUsd { get; set; }
    public int? ExactCacheTtlSeconds { get; set; }
    public bool? PrefixCacheEnabled { get; set; }
    public bool? ShadowRouterEnabled { get; set; }
    public bool? SemanticCacheEnabled { get; set; }

    public GatewaySettingsUpdate ToUpdate() => new(
        Enabled,
        RoutingMode,
        MonthlyBudgetUsd,
        DefaultTokenBudgetClass,
        ShortMaxOutputTokens,
        StandardMaxOutputTokens,
        DeepMaxOutputTokens,
        HeuristicCheapMaxTokens,
        CascadeSkipToStrongTokens,
        CascadeMaxAttempts,
        CascadeMaxUsd,
        ExactCacheTtlSeconds,
        PrefixCacheEnabled,
        ShadowRouterEnabled,
        SemanticCacheEnabled);
}

public sealed class UpsertGatewayRouteDto
{
    public string? Id { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Tier { get; set; } = GatewayModelTiers.Cheap;
    public string Region { get; set; } = "global";
    public string FallbackGroup { get; set; } = "chat";
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;

    public GatewayRouteWrite ToWrite() => new(
        Id,
        Alias,
        Provider,
        Model,
        Tier,
        Region,
        FallbackGroup,
        InputPricePerMillion,
        OutputPricePerMillion,
        SortOrder,
        Enabled);
}

public sealed class GatewaySpendReserveDto
{
    public string DecisionId { get; set; } = string.Empty;
    public decimal EstimatedCostUsd { get; set; }
}

public sealed class GatewaySpendSettleDto
{
    public string DecisionId { get; set; } = string.Empty;
    public decimal ActualCostUsd { get; set; }
}

public sealed class GatewaySpendReleaseDto
{
    public string DecisionId { get; set; } = string.Empty;
}

public sealed class IssueMcpTokenDto
{
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}
