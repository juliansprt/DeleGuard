using System.Text.Json;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Approvals;
using Deleguard.Application.Audit;
using Deleguard.Application.Authorization;
using Deleguard.Application.Identity;
using Deleguard.Application.Notifications;
using Deleguard.Application.Policies;
using Deleguard.Domain.Approvals;
using Deleguard.Domain.Identity;
using Microsoft.Extensions.Configuration;

namespace Deleguard.Api.Mcp;

public sealed class AdminMcpDispatcher
{
    private readonly IAdminActionGateway _gateway;
    private readonly IMcpDirectory _mcpDirectory;
    private readonly UserAdminHandler _users;
    private readonly RoleAdminHandler _roles;
    private readonly McpAdminHandler _mcp;
    private readonly OrganizationAdminHandler _organizations;
    private readonly ApplicationAdminHandler _applications;
    private readonly AgentAdminHandler _agents;
    private readonly EnvironmentAdminHandler _environments;
    private readonly ActionAdminHandler _actions;
    private readonly PolicyAdminHandler _policies;
    private readonly ListApprovalsHandler _approvals;
    private readonly ListAuditEventsHandler _audit;
    private readonly GetDecisionHandler _decisions;
    private readonly WebhookAdminHandler _webhooks;
    private readonly GatewayAdminHandler _aiGateway;
    private readonly IConfiguration _configuration;

    public AdminMcpDispatcher(
        IAdminActionGateway gateway,
        IMcpDirectory mcpDirectory,
        UserAdminHandler users,
        RoleAdminHandler roles,
        McpAdminHandler mcp,
        OrganizationAdminHandler organizations,
        ApplicationAdminHandler applications,
        AgentAdminHandler agents,
        EnvironmentAdminHandler environments,
        ActionAdminHandler actions,
        PolicyAdminHandler policies,
        ListApprovalsHandler approvals,
        ListAuditEventsHandler audit,
        GetDecisionHandler decisions,
        WebhookAdminHandler webhooks,
        GatewayAdminHandler aiGateway,
        IConfiguration configuration)
    {
        _gateway = gateway;
        _mcpDirectory = mcpDirectory;
        _users = users;
        _roles = roles;
        _mcp = mcp;
        _organizations = organizations;
        _applications = applications;
        _agents = agents;
        _environments = environments;
        _actions = actions;
        _policies = policies;
        _approvals = approvals;
        _audit = audit;
        _decisions = decisions;
        _webhooks = webhooks;
        _aiGateway = aiGateway;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<AdminMcpToolSpec>> ListToolsAsync(McpCallerPrincipal caller, CancellationToken cancellationToken)
    {
        var settings = await _mcpDirectory.GetSettingsAsync(caller.OrganizationId, cancellationToken);
        if (!settings.Enabled)
        {
            return [];
        }

        var enabled = settings.EnabledTools.ToHashSet(StringComparer.Ordinal);
        var principal = ToConsole(caller);
        return AdminMcpCatalog.Tools
            .Where(tool => enabled.Contains(tool.ToolName) && principal.HasPermission(tool.Permission))
            .ToList();
    }

    public async Task<AdminMcpEnvelope> CallAsync(
        McpCallerPrincipal caller,
        string toolName,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var spec = AdminMcpCatalog.Find(toolName) ?? throw new RequestValidationException($"Unknown tool '{toolName}'.");
        await EnsureEnabledAsync(caller, spec, cancellationToken);
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(OperationSchema.Field, out _))
        {
            var clean = System.Text.Json.Nodes.JsonNode.Parse(args.GetRawText())!.AsObject();
            clean.Remove(OperationSchema.Field);
            args = JsonSerializer.SerializeToElement(clean);
        }
        AdminMcpSchema.Validate(spec, args);

        if (spec.ToolName == "webhook_recover")
            WebhookAdminHandler.ValidateRecoveryRange(RecoveryInstant(args, "since"), RecoveryInstant(args, "until"));
        var console = ToConsole(caller);
        var org = caller.OrganizationId;
        if (spec.ToolName is "agent_create" or "environment_create" or "action_create" or "policy_create" or "webhook_create")
        {
            var app = await ProductApp(org, args, cancellationToken);
            var normalized = System.Text.Json.Nodes.JsonNode.Parse(args.GetRawText())!.AsObject();
            normalized["application_id"] = app.Id;
            args = JsonSerializer.SerializeToElement(normalized);
        }
        var resourceId = ReadString(args, "id") ?? ReadString(args, "user_id") ?? ReadString(args, "policy_id") ??
                         ReadString(args, "agent_id") ?? ReadString(args, "environment_id") ??
                         ReadString(args, "application_id") ?? ReadString(args, "endpoint_id") ?? spec.ToolName;

        return spec.ToolName switch
        {
            "user_list" => await Run(console, spec, resourceId, args, async ct =>
            {
                var (limit, offset) = AdminMcpPaging.Read(args);
                var items = await _users.ListAsync(org, ct);
                return Page(items, limit, offset);
            }, cancellationToken),
            "user_create" => await Run(console, spec, ReadString(args, "email") ?? "user", args,
                ct => Box(_users.CreateAsync(org, Required(args, "email"), Required(args, "display_name"), ReadStringList(args, "roles"), ReadString(args, "password"), ct)), cancellationToken),
            "user_disable" => await Run(console, spec, Required(args, "user_id"), args, async ct =>
            {
                await _users.DisableAsync(org, Required(args, "user_id"), ct);
                return Map("disabled", true);
            }, cancellationToken),
            "role_list" => await Run(console, spec, resourceId, args, ct => Box(_roles.ListAsync(org, ct)), cancellationToken),
            "role_assign" => await Run(console, spec, Required(args, "user_id"), args, async ct =>
            {
                await _users.AssignRolesAsync(org, Required(args, "user_id"), ReadStringList(args, "roles"), ct);
                return Map("assigned", true);
            }, cancellationToken),
            "organization_get" => await Run(console, spec, org, args,
                ct => Box(_organizations.GetAsync(org, ct)), cancellationToken),
            "organization_update" => await Run(console, spec, org, args,
                ct => Box(_organizations.UpdateNameAsync(org, Required(args, "name"), ct)), cancellationToken),
            "application_list" => await Run(console, spec, resourceId, args, async ct =>
            {
                var (limit, offset) = AdminMcpPaging.Read(args);
                var items = await _applications.ListProductAsync(org, ct);
                return Page(items, limit, offset);
            }, cancellationToken),
            "application_create" => await Run(console, spec, ReadString(args, "slug") ?? "application", args,
                ct => Box(_applications.CreateAsync(org, Required(args, "name"), Required(args, "slug"), ct)), cancellationToken),
            "application_update" => await Run(console, spec, Required(args, "application_id"), args,
                ct => Box(_applications.UpdateAsync(org, Required(args, "application_id"), Required(args, "name"), Required(args, "slug"), ct)), cancellationToken),
            "application_delete" => await DeleteApplication(console, spec, org, args, cancellationToken),
            "agent_list" => await Run(console, spec, resourceId, args, async ct =>
            {
                var (limit, offset) = AdminMcpPaging.Read(args);
                var app = await ProductApp(org, args, ct);
                var items = await _agents.ListAsync(org, app.Id, ct);
                return Page(items, limit, offset);
            }, cancellationToken),
            "agent_create" => await Run(console, spec, ReadString(args, "external_id") ?? "agent", args, async ct =>
            {
                var app = await ProductApp(org, args, ct);
                return await Box(_agents.CreateAsync(new CreateAgentCommand(org, app.Id, Required(args, "external_id"), Required(args, "display_name"), ReadString(args, "environment") ?? "production"), ct));
            }, cancellationToken),
            "agent_rotate_key" => await Run(console, spec, Required(args, "agent_id"), args,
                ct => Box(_agents.RotateKeyAsync(org, Required(args, "agent_id"), ct)), cancellationToken),
            "agent_disable" => await SetAgentEnabled(console, spec, org, args, false, cancellationToken),
            "agent_enable" => await SetAgentEnabled(console, spec, org, args, true, cancellationToken),
            "environment_list" => await Run(console, spec, resourceId, args, async ct =>
            {
                var (limit, offset) = AdminMcpPaging.Read(args);
                var app = await ProductApp(org, args, ct);
                var items = await _environments.ListAsync(org, app.Id, ct);
                return Page(items, limit, offset);
            }, cancellationToken),
            "environment_get" => await Run(console, spec, Required(args, "environment_id"), args,
                ct => Box(_environments.GetAsync(org, Required(args, "environment_id"), ct)), cancellationToken),
            "environment_create" => await Run(console, spec, EnvironmentAdminHandler.NormalizeName(Required(args, "name")), args, async ct =>
            {
                var app = await ProductApp(org, args, ct);
                return await Box(_environments.CreateAsync(org, app.Id, EnvironmentAdminHandler.NormalizeName(Required(args, "name")), ct));
            }, cancellationToken),
            "environment_update" => await UpdateEnvironment(console, spec, org, args, cancellationToken),
            "environment_delete" => await DeleteEnvironment(console, spec, org, args, cancellationToken),
            "docs_list" => await Run(console, spec, "catalog", args,
                _ => Task.FromResult(AdminMcpDocs.List(DocsPublicUrl())), cancellationToken),
            "docs_get" => await Run(console, spec, Required(args, "slug"), args,
                _ => Task.FromResult(AdminMcpDocs.Get(Required(args, "slug"), DocsPublicUrl())), cancellationToken),
            "action_list" => await Run(console, spec, resourceId, args, async ct =>
            {
                var app = await ProductApp(org, args, ct);
                var items = await _actions.ListAsync(org, app.Id, ct);
                return items.Select(action => new
                {
                    id = action.Id,
                    application_id = action.ApplicationId,
                    name = action.Name,
                    resource_type = action.ResourceType,
                    risk = action.Risk.ToString().ToUpperInvariant()
                }).ToList();
            }, cancellationToken),
            "action_create" => await Run(console, spec, ReadString(args, "name") ?? "action", args, async ct =>
            {
                var app = await ProductApp(org, args, ct);
                return await Box(_actions.CreateAsync(new CreateActionCommand(org, app.Id, Required(args, "name"), Required(args, "resource_type"), ReadString(args, "risk") ?? "MEDIUM", ReadStringList(args, "agent_ids")), ct));
            }, cancellationToken),
            "action_update" => await UpdateAction(console, spec, org, args, cancellationToken),
            "action_delete" => await DeleteAction(console, spec, org, args, cancellationToken),
            "policy_list" => await Run(console, spec, resourceId, args, async ct =>
            {
                var (limit, offset) = AdminMcpPaging.Read(args);
                var items = (await ListPoliciesAsync(org, args, ct))
                    .Select(SummarizePolicy)
                    .ToList();
                return Page(items, limit, offset);
            }, cancellationToken),
            "policy_get" => await Run(console, spec, Required(args, "policy_id"), args, async ct =>
            {
                var (policy, versions) = await _policies.GetAsync(org, Required(args, "policy_id"), ct);
                return (object)new
                {
                    id = policy.Id,
                    slug = policy.Slug,
                    published_version_id = policy.PublishedVersionId,
                    created_at = policy.CreatedAt,
                    yaml_resource = AdminMcpResources.PolicyUri(policy.Id),
                    versions = versions.Select(version => new
                    {
                        id = version.Id,
                        version = version.Version,
                        action = version.Action,
                        resource_type = version.ResourceType,
                        priority = version.Priority,
                        published = version.Published,
                        created_at = version.CreatedAt,
                        published_at = version.PublishedAt
                    }).ToList()
                };
            }, cancellationToken),
            "policy_create" => await Run(console, spec, "policy", args, async ct =>
            {
                var app = await ProductApp(org, args, ct);
                var created = await _policies.CreateAsync(new CreatePolicyCommand(org, app.Id, Required(args, "yaml"), ReadStringList(args, "agent_ids")), ct);
                return (object)new
                {
                    policy_id = created.Policy.Id,
                    application_id = created.Policy.ApplicationId,
                    slug = created.Policy.Slug,
                    published = false,
                    version_id = created.Version.Id,
                    version = created.Version.Version,
                    action = created.Version.Action,
                    resource_type = created.Version.ResourceType,
                    yaml_resource = AdminMcpResources.PolicyUri(created.Policy.Id)
                };
            }, cancellationToken),
            "policy_create_version" => await Run(console, spec, Required(args, "policy_id"), args,
                ct => Box(_policies.AddVersionAsync(new AddPolicyVersionCommand(org, Required(args, "policy_id"), Required(args, "yaml")), ct)), cancellationToken),
            "policy_test" => await Run(console, spec, Required(args, "policy_id"), args,
                ct => Box(_policies.TestAsync(new PolicyTestCommand(org, Required(args, "policy_id"), ReadString(args, "yaml"), ReadString(args, "expected_effect"), ReadString(args, "agent_id"), ReadString(args, "environment"), ReadString(args, "action"), ReadString(args, "resource_type"), ReadString(args, "resource_id"), null, null), ct)), cancellationToken),
            "policy_publish" => await Run(console, spec, Required(args, "policy_id"), args,
                ct => Box(_policies.PublishAsync(new PublishPolicyCommand(org, Required(args, "policy_id"), Required(args, "version_id")), ct)), cancellationToken),
            "policy_delete" => await DeletePolicy(console, spec, org, args, cancellationToken),
            "mcp_status" => await Run(console, spec, "settings", args, ct => Box(_mcp.GetSettingsAsync(org, ct)), cancellationToken),
            "mcp_issue_token" => await Run(console, spec, Required(args, "user_id"), args,
                ct => Box(_mcp.IssueTokenAsync(org, Required(args, "user_id"), null, ct)), cancellationToken),
            "mcp_revoke_token" => await Run(console, spec, Required(args, "credential_id"), args, async ct =>
            {
                await _mcp.RevokeTokenAsync(org, Required(args, "credential_id"), ct);
                return Map("revoked", true);
            }, cancellationToken),
            "mcp_set_tools" => await Run(console, spec, "settings", args, async ct =>
            {
                var settings = await _mcpDirectory.GetSettingsAsync(org, ct);
                return await _mcp.UpdateSettingsAsync(org, ReadBool(args, "enabled") ?? settings.Enabled, ReadStringList(args, "tools"), ct);
            }, cancellationToken),
            "gateway_status" => await Run(console, spec, "settings", args, ct => Box(_aiGateway.GetSettingsAsync(org, ct)), cancellationToken),
            "gateway_update_settings" => await Run(console, spec, "settings", args, ct => Box(_aiGateway.UpdateSettingsAsync(org, new GatewaySettingsUpdate(
                ReadBool(args, "enabled") ?? false,
                Required(args, "routing_mode"),
                ReadDecimal(args, "monthly_budget_usd") ?? 0,
                Required(args, "default_token_budget_class"),
                ReadInt(args, "short_max_output_tokens"),
                ReadInt(args, "standard_max_output_tokens"),
                ReadInt(args, "deep_max_output_tokens"),
                ReadInt(args, "heuristic_cheap_max_tokens"),
                ReadInt(args, "cascade_skip_to_strong_tokens"),
                ReadInt(args, "cascade_max_attempts"),
                ReadDecimal(args, "cascade_max_usd"),
                ReadInt(args, "exact_cache_ttl_seconds"),
                ReadBool(args, "prefix_cache_enabled"),
                ReadBool(args, "shadow_router_enabled")), ct)), cancellationToken),
            "gateway_apply_openai_chat_pair" => await Run(console, spec, GatewayRecipes.OpenAiChatPairId, args,
                ct => Box(_aiGateway.ApplyOpenAiChatPairAsync(org, ct)), cancellationToken),
            "gateway_upsert_route" => await Run(console, spec, ReadString(args, "route_id") ?? Required(args, "alias"), args, ct => Box(_aiGateway.UpsertRouteAsync(
                org,
                new GatewayRouteWrite(
                    ReadString(args, "route_id"),
                    Required(args, "alias"),
                    Required(args, "provider"),
                    Required(args, "model"),
                    Required(args, "tier"),
                    Required(args, "region"),
                    Required(args, "fallback_group"),
                    ReadDecimal(args, "input_price_per_million") ?? 0,
                    ReadDecimal(args, "output_price_per_million") ?? 0,
                    ReadInt(args, "sort_order") ?? 0,
                    ReadBool(args, "enabled") ?? true),
                ct)), cancellationToken),
            "gateway_delete_route" => await Run(console, spec, Required(args, "route_id"), args, ct => Box(
                _aiGateway.DeleteRouteAsync(org, Required(args, "route_id"), ct)), cancellationToken),
            "webhook_list" => await Run(console, spec, resourceId, args, async ct =>
            {
                var (limit, offset) = AdminMcpPaging.Read(args);
                var items = (await _webhooks.ListAsync(org, (await ProductApp(org, args, ct)).Id, ct)).Select(WebhookJson).ToList();
                return Page(items, limit, offset);
            }, cancellationToken),
            "webhook_get" => await Run(console, spec, Required(args, "endpoint_id"), args, async ct =>
                WebhookJson(await _webhooks.GetAsync(org, Required(args, "endpoint_id"), ct)), cancellationToken),
            "webhook_create" => await Run(console, spec, ReadString(args, "url") ?? "webhook", args, async ct =>
            {
                var created = await _webhooks.CreateAsync(new CreateWebhookCommand(
                    org,
                    (await ProductApp(org, args, ct)).Id,
                    Required(args, "description"),
                    Required(args, "url"),
                    ReadStringList(args, "event_types")), ct);
                return (object)new
                {
                    endpoint_id = created.EndpointId,
                    secret = created.Secret,
                    secret_prefix = created.SecretPrefix,
                    message = "Store this signing secret; it is shown only once."
                };
            }, cancellationToken),
            "webhook_update" => await Run(console, spec, Required(args, "endpoint_id"), args, async ct =>
            {
                var updated = await _webhooks.UpdateAsync(new UpdateWebhookCommand(
                    org,
                    Required(args, "endpoint_id"),
                    ReadString(args, "description"),
                    ReadString(args, "url"),
                    ReadOptionalStringList(args, "event_types"),
                    ReadString(args, "status")), ct);
                return WebhookJson(updated);
            }, cancellationToken),
            "webhook_rotate_secret" => await Run(console, spec, Required(args, "endpoint_id"), args, async ct =>
            {
                var rotated = await _webhooks.RotateSecretAsync(org, Required(args, "endpoint_id"), ct);
                return (object)new
                {
                    endpoint_id = rotated.EndpointId,
                    secret = rotated.Secret,
                    secret_prefix = rotated.SecretPrefix,
                    message = "Store this signing secret; it is shown only once. The previous secret remains valid until the next rotation."
                };
            }, cancellationToken),
            "webhook_ping" => await Run(console, spec, Required(args, "endpoint_id"), args, async ct =>
            {
                await _webhooks.PingAsync(org, Required(args, "endpoint_id"), ct);
                return Map("pinged", true);
            }, cancellationToken),
            "webhook_attempts" => await Run(console, spec, Required(args, "endpoint_id"), args, async ct =>
            {
                var items = await _webhooks.ListAttemptsAsync(org, Required(args, "endpoint_id"), ct);
                return items.Select(attempt => new
                {
                    attempt_id = attempt.Id,
                    message_id = attempt.MessageId,
                    event_type = attempt.EventType,
                    attempt_number = attempt.AttemptNumber,
                    http_status = attempt.HttpStatus,
                    error = attempt.Error,
                    response_body = attempt.ResponseBody,
                    duration_ms = attempt.DurationMs,
                    success = attempt.Success,
                    finished_at = attempt.FinishedAt
                }).ToList();
            }, cancellationToken),
            "webhook_resend" => await Run(console, spec, Required(args, "endpoint_id"), args, async ct =>
            {
                await _webhooks.ResendAsync(org, Required(args, "endpoint_id"), Required(args, "message_id"), ct);
                return Map("resent", true);
            }, cancellationToken),
            "webhook_recover" => await Run(console, spec, Required(args, "endpoint_id"), args, async ct =>
            {
                var since = RecoveryInstant(args, "since");
                var until = RecoveryInstant(args, "until");
                var count = await _webhooks.RecoverAsync(org, Required(args, "endpoint_id"), since, until, ct);
                return (object)new { recovered = count, since, until };
            }, cancellationToken),
            "approval_list" => await Run(console, spec, resourceId, args, async ct =>
            {
                var (limit, offset) = AdminMcpPaging.Read(args);
                var items = (await _approvals.HandleAsync(org, ct)).Select(SummarizeApproval).ToList();
                return Page(items, limit, offset);
            }, cancellationToken),
            "audit_list" => await Run(console, spec, resourceId, args, async ct =>
            {
                var (limit, offset) = AdminMcpPaging.Read(args);
                var take = Math.Min(offset + limit + 1, 500);
                var window = await _audit.HandleAsync(org, take, ct);
                var page = window.Skip(offset).Take(limit).Select(item => new
                {
                    id = item.Id,
                    event_type = item.EventType,
                    decision_id = item.DecisionId,
                    created_at = item.CreatedAt,
                    decision_resource = string.IsNullOrWhiteSpace(item.DecisionId) ? null : AdminMcpResources.DecisionUri(item.DecisionId)
                }).ToList();
                var next = window.Count > offset + limit ? AdminMcpPaging.Encode(offset + limit) : null;
                return (object)new { items = page, next_cursor = next, limit };
            }, cancellationToken),
            _ => throw new RequestValidationException($"Unknown tool '{toolName}'.")
        };
    }

    public object ListPrompts() => new
    {
        prompts = AdminMcpPrompts.All.Select(prompt => new
        {
            name = prompt.Name,
            title = prompt.Title,
            description = prompt.Description,
            arguments = prompt.Arguments.Select(argument => new
            {
                name = argument.Name,
                description = argument.Description,
                required = argument.Required
            })
        })
    };

    public object GetPrompt(string name, JsonElement arguments)
    {
        var spec = AdminMcpPrompts.Find(name) ?? throw new RequestValidationException($"Unknown prompt '{name}'.");
        var text = AdminMcpPrompts.Render(spec, arguments);
        return new
        {
            description = spec.Description,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new { type = "text", text }
                }
            }
        };
    }

    public object ListResourceTemplates() => new
    {
        resourceTemplates = new object[]
        {
            new { uriTemplate = "deleguard://policy/{id}", name = "Policy", description = "Policy YAML and version metadata.", mimeType = "application/json" },
            new { uriTemplate = "deleguard://decision/{id}", name = "Decision", description = "Persisted authorization decision facts. Never includes permits.", mimeType = "application/json" },
            new { uriTemplate = "deleguard://approval/{id}", name = "Approval", description = "Pending or resolved approval facts. Approve in the console.", mimeType = "application/json" },
            new { uriTemplate = "deleguard://docs/{slug}", name = "Docs", description = "Product documentation for configuring this Deleguard environment.", mimeType = "application/json" }
        }
    };

    public async Task<object> ListResourcesAsync(McpCallerPrincipal caller, CancellationToken cancellationToken)
    {
        var settings = await _mcpDirectory.GetSettingsAsync(caller.OrganizationId, cancellationToken);
        if (!settings.Enabled)
        {
            return new { resources = Array.Empty<object>() };
        }

        var enabled = settings.EnabledTools.ToHashSet(StringComparer.Ordinal);
        var console = ToConsole(caller);
        var resources = new List<object>();
        await AddIfAllowed(console, enabled, "policy_list", cancellationToken, async ct =>
        {
            var items = await _policies.ListAsync(caller.OrganizationId, (await _applications.GetDefaultProductAsync(caller.OrganizationId, ct)).Id, ct);
            foreach (var item in items.Take(AdminMcpPaging.MaxLimit))
            {
                resources.Add(new
                {
                    uri = AdminMcpResources.PolicyUri(item.Id),
                    name = item.Slug,
                    description = "Policy YAML and versions.",
                    mimeType = "application/json"
                });
            }
        });
        await AddIfAllowed(console, enabled, "docs_list", cancellationToken, _ =>
        {
            foreach (var doc in AdminMcpDocs.All)
            {
                resources.Add(new
                {
                    uri = AdminMcpResources.DocsUri(doc.Slug),
                    name = doc.Title,
                    description = doc.Summary,
                    mimeType = "application/json"
                });
            }

            return Task.CompletedTask;
        });
        await AddIfAllowed(console, enabled, "approval_list", cancellationToken, async ct =>
        {
            var items = await _approvals.HandleAsync(caller.OrganizationId, ct);
            foreach (var item in items.Take(AdminMcpPaging.MaxLimit))
            {
                resources.Add(new
                {
                    uri = AdminMcpResources.ApprovalUri(item.Id),
                    name = item.Id,
                    description = "Approval canonical facts. Approve in the console.",
                    mimeType = "application/json"
                });
            }
        });
        return new { resources };
    }

    public async Task<object> ReadResourceAsync(McpCallerPrincipal caller, string uri, CancellationToken cancellationToken)
    {
        var parsed = AdminMcpResources.Parse(uri);
        var specName = parsed.Kind switch
        {
            "policy" => "policy_get",
            "approval" => "approval_list",
            "decision" => "audit_list",
            "docs" => "docs_get",
            _ => throw new RequestValidationException("Unsupported resource URI.")
        };
        var spec = AdminMcpCatalog.Find(specName) ?? throw new RequestValidationException("Unsupported resource URI.");
        await EnsureEnabledAsync(caller, spec, cancellationToken);
        var console = ToConsole(caller);
        var empty = JsonSerializer.SerializeToElement(new { });
        var envelope = parsed.Kind switch
        {
            "policy" => await Run(console, spec, parsed.Id, empty, async ct =>
            {
                var (policy, versions) = await _policies.GetAsync(caller.OrganizationId, parsed.Id, ct);
                return (object)new
                {
                    policy_id = policy.Id,
                    slug = policy.Slug,
                    published_version_id = policy.PublishedVersionId,
                    versions = versions.Select(version => new
                    {
                        id = version.Id,
                        version = version.Version,
                        published = version.Published,
                        action = version.Action,
                        resource_type = version.ResourceType,
                        yaml = version.Yaml
                    }).ToList()
                };
            }, cancellationToken),
            "approval" => await Run(console, spec, parsed.Id, empty, async ct =>
            {
                var approval = await _approvals.GetAsync(caller.OrganizationId, parsed.Id, ct);
                return (object)new
                {
                    approval_id = approval.Id,
                    decision_id = approval.DecisionId,
                    status = approval.Status.ToString(),
                    expires_at = approval.ExpiresAt,
                    created_at = approval.CreatedAt,
                    resolved_at = approval.ResolvedAt,
                    canonical_facts = AdminMcpResources.RedactSecrets(TryParseJson(approval.CanonicalFactsJson)),
                    console_path = $"/approvals/{approval.Id}"
                };
            }, cancellationToken),
            "docs" => await Run(console, spec, parsed.Id, empty,
                _ => Task.FromResult(AdminMcpDocs.Get(parsed.Id, DocsPublicUrl())), cancellationToken),
            _ => await Run(console, spec, parsed.Id, empty, async ct =>
            {
                var facts = await _decisions.GetOrganizationFactsAsync(caller.OrganizationId, parsed.Id, ct);
                return (object)facts;
            }, cancellationToken)
        };

        if (!string.Equals(envelope.ResultType, "allow", StringComparison.Ordinal) || envelope.Data is null)
        {
            throw new NotFoundException("Resource not available.");
        }

        var text = AdminMcpJson.Element(envelope.Data).GetRawText();
        return new
        {
            contents = new[]
            {
                new { uri, mimeType = "application/json", text }
            }
        };
    }

    public async Task<object> CompleteAsync(McpCallerPrincipal caller, JsonElement parameters, CancellationToken cancellationToken)
    {
        var argumentName = string.Empty;
        var prefix = string.Empty;
        if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("argument", out var argument) &&
            argument.ValueKind == JsonValueKind.Object)
        {
            argumentName = argument.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? string.Empty : string.Empty;
            prefix = argument.TryGetProperty("value", out var valueNode) ? valueNode.GetString() ?? string.Empty : string.Empty;
        }

        var console = ToConsole(caller);
        IReadOnlyList<string> values = argumentName switch
        {
            "user_id" when console.HasPermission(PermissionCatalog.UsersRead) =>
                (await _users.ListAsync(caller.OrganizationId, cancellationToken)).Select(item => item.Id).ToList(),
            "agent_id" when console.HasPermission(PermissionCatalog.AgentsRead) =>
                (await _agents.ListAsync(caller.OrganizationId, null, cancellationToken)).Select(item => item.Id).ToList(),
            "environment_id" when console.HasPermission(PermissionCatalog.EnvironmentsRead) =>
                (await _environments.ListAsync(caller.OrganizationId, (await _applications.GetDefaultProductAsync(caller.OrganizationId, cancellationToken)).Id, cancellationToken)).Select(item => item.Id).ToList(),
            "policy_id" when console.HasPermission(PermissionCatalog.PoliciesRead) =>
                (await _policies.ListAsync(caller.OrganizationId, null, cancellationToken)).Select(item => item.Id).ToList(),
            "application_id" when console.HasPermission(PermissionCatalog.ApplicationsRead) =>
                (await _applications.ListProductAsync(caller.OrganizationId, cancellationToken)).SelectMany(item => new[] { item.Id, item.Slug }).ToList(),
            "slug" when console.HasPermission(PermissionCatalog.EnvironmentsRead) =>
                AdminMcpDocs.Slugs,
            "credential_id" when console.HasPermission(PermissionCatalog.McpRead) =>
                (await _mcp.ListCredentialsAsync(caller.OrganizationId, cancellationToken)).Select(item => item.Id).ToList(),
            "endpoint_id" when console.HasPermission(PermissionCatalog.WebhooksRead) =>
                (await _webhooks.ListAsync(caller.OrganizationId, null, cancellationToken)).Select(item => item.Id).ToList(),
            _ => []
        };

        var matches = values
            .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();
        return new { completion = new { values = matches, hasMore = false } };
    }

    private async Task AddIfAllowed(
        ConsoleUserPrincipal console,
        HashSet<string> enabled,
        string toolName,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> onAllow)
    {
        var spec = AdminMcpCatalog.Find(toolName);
        if (spec is null || !enabled.Contains(toolName) || !console.HasPermission(spec.Permission))
        {
            return;
        }

        var empty = JsonSerializer.SerializeToElement(new { });
        var envelope = await Run(console, spec, toolName, empty, async ct =>
        {
            await onAllow(ct);
            return Map("listed", true);
        }, cancellationToken);
        if (!string.Equals(envelope.ResultType, "allow", StringComparison.Ordinal))
        {
            return;
        }
    }

    private async Task EnsureEnabledAsync(McpCallerPrincipal caller, AdminMcpToolSpec spec, CancellationToken cancellationToken)
    {
        var settings = await _mcpDirectory.GetSettingsAsync(caller.OrganizationId, cancellationToken);
        if (!settings.Enabled)
        {
            throw new ForbiddenException("Administration MCP is disabled.");
        }

        if (!settings.EnabledTools.Contains(spec.ToolName, StringComparer.Ordinal))
        {
            throw new ForbiddenException($"Tool '{spec.ToolName}' is not enabled.");
        }
    }

    private async Task<AdminMcpEnvelope> UpdateAction(
        ConsoleUserPrincipal caller,
        AdminMcpToolSpec spec,
        string organizationId,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var actionId = Required(args, "action_id");
        await _actions.EnsureCanUpdateAsync(organizationId, actionId, cancellationToken);
        return await Run(caller, spec, actionId, args,
            ct => Box(_actions.UpdateAsync(new UpdateActionCommand(
                organizationId,
                actionId,
                Required(args, "resource_type"),
                Required(args, "risk"),
                args.ValueKind == JsonValueKind.Object && args.TryGetProperty("agent_ids", out _)
                    ? ReadStringList(args, "agent_ids")
                    : null), ct)),
            cancellationToken);
    }

    private async Task<AdminMcpEnvelope> DeleteAction(
        ConsoleUserPrincipal caller,
        AdminMcpToolSpec spec,
        string organizationId,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var actionId = Required(args, "action_id");
        await _actions.EnsureCanDeleteAsync(organizationId, actionId, cancellationToken);
        return await Run(caller, spec, actionId, args, async ct =>
        {
            await _actions.DeleteAsync(organizationId, actionId, ct);
            return Map("deleted", true);
        }, cancellationToken);
    }

    private async Task<AdminMcpEnvelope> DeletePolicy(
        ConsoleUserPrincipal caller,
        AdminMcpToolSpec spec,
        string organizationId,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var policyId = Required(args, "policy_id");
        await _policies.EnsureCanDeleteAsync(organizationId, policyId, cancellationToken);
        return await Run(caller, spec, policyId, args, async ct =>
        {
            await _policies.DeleteAsync(organizationId, policyId, ct);
            return Map("deleted", true);
        }, cancellationToken);
    }

    private async Task<AdminMcpEnvelope> UpdateEnvironment(
        ConsoleUserPrincipal caller,
        AdminMcpToolSpec spec,
        string organizationId,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var environmentId = Required(args, "environment_id");
        var name = EnvironmentAdminHandler.NormalizeName(Required(args, "name"));
        await _environments.GetAsync(organizationId, environmentId, cancellationToken);
        return await Run(caller, spec, environmentId, args,
            ct => Box(_environments.UpdateAsync(organizationId, environmentId, name, ct)),
            cancellationToken);
    }

    private async Task<AdminMcpEnvelope> DeleteEnvironment(
        ConsoleUserPrincipal caller,
        AdminMcpToolSpec spec,
        string organizationId,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var environmentId = Required(args, "environment_id");
        await _environments.EnsureCanDeleteAsync(organizationId, environmentId, cancellationToken);
        return await Run(caller, spec, environmentId, args, async ct =>
        {
            await _environments.DeleteAsync(organizationId, environmentId, ct);
            return Map("deleted", true);
        }, cancellationToken);
    }

    private async Task<AdminMcpEnvelope> DeleteApplication(
        ConsoleUserPrincipal caller,
        AdminMcpToolSpec spec,
        string organizationId,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var applicationId = Required(args, "application_id");
        await _applications.EnsureCanDeleteAsync(organizationId, applicationId, cancellationToken);
        return await Run(caller, spec, applicationId, args, async ct =>
        {
            await _applications.DeleteAsync(organizationId, applicationId, ct);
            return Map("deleted", true);
        }, cancellationToken);
    }

    private async Task<AdminMcpEnvelope> SetAgentEnabled(
        ConsoleUserPrincipal caller,
        AdminMcpToolSpec spec,
        string organizationId,
        JsonElement args,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var agentId = Required(args, "agent_id");
        await _agents.EnsureCanSetEnabledAsync(organizationId, agentId, enabled, cancellationToken);
        return await Run(caller, spec, agentId, args, async ct =>
        {
            await _agents.SetEnabledAsync(organizationId, agentId, enabled, ct);
            return Map(enabled ? "enabled" : "disabled", true);
        }, cancellationToken);
    }

    private async Task<AdminMcpEnvelope> Run<T>(
        ConsoleUserPrincipal caller,
        AdminMcpToolSpec spec,
        string resourceId,
        JsonElement args,
        Func<CancellationToken, Task<T>> execute,
        CancellationToken cancellationToken)
    {
        var outcome = await _gateway.RunAsync(
            caller,
            spec.Action,
            spec.ResourceType,
            resourceId,
            ToAttributes(args),
            spec.Permission,
            execute,
            cancellationToken);
        return AdminMcpEnvelope.FromOutcome(outcome);
    }

    private string DocsPublicUrl() =>
        AdminMcpDocs.NormalizeOrigin(_configuration["Docs:PublicUrl"] ?? AdminMcpDocs.DefaultPublicUrl);

    private static ConsoleUserPrincipal ToConsole(McpCallerPrincipal caller) =>
        new(caller.OrganizationId, caller.UserId, caller.Email, caller.DisplayName, caller.Roles, caller.Permissions, false);

    private static async Task<object> Box<T>(Task<T> task) => (await task)!;

    private static object Map(string key, object value) => new Dictionary<string, object> { [key] = value };

    private static object Page<T>(IReadOnlyList<T> items, int limit, int offset) =>
        AdminMcpPaging.AsData(AdminMcpPaging.Slice(items, limit, offset));

    private async Task<IReadOnlyList<PolicySummary>> ListPoliciesAsync(string organizationId, JsonElement args, CancellationToken cancellationToken)
    {
        var kind = ReadString(args, "kind");
        if (string.Equals(kind, "administration", StringComparison.OrdinalIgnoreCase))
        {
            var admin = await _applications.GetAdministrationAsync(organizationId, cancellationToken);
            return await _policies.ListAsync(organizationId, admin.Id, cancellationToken);
        }

        if (string.Equals(kind, "all", StringComparison.OrdinalIgnoreCase))
        {
            return await _policies.ListAsync(organizationId, null, cancellationToken);
        }

        var app = await ProductApp(organizationId, args, cancellationToken);
        return await _policies.ListAsync(organizationId, app.Id, cancellationToken);
    }

    private Task<ApplicationListItem> ProductApp(string organizationId, JsonElement args, CancellationToken cancellationToken) =>
        _applications.ResolveProductAsync(organizationId, ReadString(args, "application_id"), cancellationToken);

    private static object SummarizePolicy(PolicySummary item) => new
    {
        policy_id = item.Id,
        slug = item.Slug,
        published = !string.IsNullOrWhiteSpace(item.PublishedVersionId),
        published_version_id = item.PublishedVersionId,
        published_version = item.PublishedVersionNumber,
        version_count = item.VersionCount,
        action = item.Action,
        resource_type = item.ResourceType,
        yaml_resource = AdminMcpResources.PolicyUri(item.Id)
    };

    private static object SummarizeApproval(ApprovalRequest approval) => new
    {
        approval_id = approval.Id,
        decision_id = approval.DecisionId,
        status = approval.Status.ToString(),
        expires_at = approval.ExpiresAt,
        created_at = approval.CreatedAt,
        resource_uri = AdminMcpResources.ApprovalUri(approval.Id),
        console_path = $"/approvals/{approval.Id}"
    };

    private static string Required(JsonElement args, string name) =>
        ReadString(args, name) ?? throw new RequestValidationException($"{name} is required.");

    private static string? ReadString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool? ReadBool(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => bool.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
    }

    private static int? ReadInt(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.TryGetDecimal(out var decimalNumber))
            {
                return (int)decimalNumber;
            }
        }

        return int.TryParse(value.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ReadDecimal(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(value.ToString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<string> ReadStringList(JsonElement args, string name) =>
        ReadOptionalStringList(args, name) ?? [];

    private static IReadOnlyList<string>? ReadOptionalStringList(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new RequestValidationException($"{name} must be an array of strings.");
        }

        return value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToList();
    }

    private static DateTimeOffset RecoveryInstant(JsonElement args, string name) =>
        WebhookAdminHandler.ParseRecoveryInstant(Required(args, name), name);

    private static DateTimeOffset ReadSince(JsonElement args, DateTimeOffset fallback)
    {
        var raw = ReadString(args, "since");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new RequestValidationException("since must be an ISO-8601 timestamp.");
        }

        return parsed;
    }

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

    private static Dictionary<string, JsonElement>? ToAttributes(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(args.GetRawText()) : null;

    private static object? TryParseJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
