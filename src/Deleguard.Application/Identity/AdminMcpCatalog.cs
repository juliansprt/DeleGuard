using System.Text.Json;
using System.Text.Json.Nodes;
using Deleguard.Domain.Common;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Notifications;

namespace Deleguard.Application.Identity;

public sealed record AdminMcpToolSpec(
    string ToolName,
    string Title,
    string Action,
    string ResourceType,
    string Permission,
    RiskLevel Risk,
    string Description,
    JsonElement InputSchema,
    JsonElement OutputSchema,
    bool ReadOnlyHint,
    bool DestructiveHint,
    bool IdempotentHint,
    bool Paged = false)
{
    public bool OpenWorldHint => false;
}

public static class AdminMcpCatalog
{
    public static readonly IReadOnlyList<AdminMcpToolSpec> Tools =
    [
        Tool("user_list", "List users", "admin.user.list", "user", PermissionCatalog.UsersRead, RiskLevel.Low,
            "List organization users as a page of ids, emails, roles, and enabled flags.",
            AdminMcpSchema.Page("Optional page of users. Omit arguments for the first page."), readOnly: true, paged: true),
        Tool("user_create", "Create user", "admin.user.create", "user", PermissionCatalog.UsersWrite, RiskLevel.High,
            "Create a console user. HIGH risk: operators need approval in the console, not via a tool.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["email"] = AdminMcpSchema.String("User email."),
                ["display_name"] = AdminMcpSchema.String("Display name."),
                ["roles"] = AdminMcpSchema.StringArray("Role slugs such as org-admin, operator, auditor."),
                ["password"] = AdminMcpSchema.String("Optional password. A temporary password is issued when omitted.")
            }, ["email", "display_name"])),
        Tool("user_disable", "Disable user", "admin.user.disable", "user", PermissionCatalog.UsersWrite, RiskLevel.High,
            "Disable a console user and revoke that user's Administration MCP tokens.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["user_id"] = AdminMcpSchema.String("User id (usr_…).")
            }, ["user_id"]), destructive: true, idempotent: true),
        Tool("role_list", "List roles", "admin.role.list", "role", PermissionCatalog.RolesRead, RiskLevel.Low,
            "List roles and the permissions attached to each role.",
            AdminMcpSchema.Empty("No arguments."), readOnly: true, idempotent: true),
        Tool("role_assign", "Assign roles", "admin.role.assign", "role", PermissionCatalog.UsersWrite, RiskLevel.High,
            "Replace the roles assigned to a user.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["user_id"] = AdminMcpSchema.String("User id (usr_…)."),
                ["roles"] = AdminMcpSchema.StringArray("Replacement role slugs.")
            }, ["user_id", "roles"]), idempotent: true),
        Tool("organization_get", "Get organization", "admin.organization.get", "organization", PermissionCatalog.ApplicationsRead, RiskLevel.Low,
            "Read the current tenant id and name. Rename with organization_update.",
            AdminMcpSchema.Empty("No arguments."), readOnly: true, idempotent: true),
        Tool("organization_update", "Rename organization", "admin.organization.update", "organization", PermissionCatalog.OrganizationWrite, RiskLevel.Medium,
            "Rename this tenant. The org_ id does not change.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["name"] = AdminMcpSchema.String("Organization display name, 3–80 characters.")
            }, ["name"]), idempotent: true),
        Tool("application_list", "List applications", "admin.application.list", "application", PermissionCatalog.ApplicationsRead, RiskLevel.Low,
            "List product applications. The hidden Administration application is omitted.",
            AdminMcpSchema.Page("Optional page of product applications.", includeApplication: false), readOnly: true, paged: true),
        Tool("application_create", "Create application", "admin.application.create", "application", PermissionCatalog.ApplicationsWrite, RiskLevel.Medium,
            "Create a product application with an empty production environment.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["name"] = AdminMcpSchema.String("Display name, 1–80 characters."),
                ["slug"] = AdminMcpSchema.String("Lowercase id. The administration slug is reserved.")
            }, ["name", "slug"])),
        Tool("application_update", "Update application", "admin.application.update", "application", PermissionCatalog.ApplicationsWrite, RiskLevel.Medium,
            "Rename a product application or change its slug. The Administration application cannot be changed.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["application_id"] = AdminMcpSchema.String("Application id (app_…)."),
                ["name"] = AdminMcpSchema.String("Replacement display name."),
                ["slug"] = AdminMcpSchema.String("Replacement lowercase id.")
            }, ["application_id", "name", "slug"]), idempotent: true),
        Tool("application_delete", "Delete application", "admin.application.delete", "application", PermissionCatalog.ApplicationsWrite, RiskLevel.High,
            "Delete an empty product application. Fails closed if agents remain, it is Administration, or it is the last product application. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["application_id"] = AdminMcpSchema.String("Application id (app_…).")
            }, ["application_id"]), destructive: true),
        Tool("agent_list", "List agents", "admin.agent.list", "agent", PermissionCatalog.AgentsRead, RiskLevel.Low,
            "List registered agents. Does not return API secrets. Defaults to the default product application.",
            AdminMcpSchema.Page("Optional page of agents.", includeApplication: true), readOnly: true, paged: true),
        Tool("agent_create", "Create agent", "admin.agent.create", "agent", PermissionCatalog.AgentsWrite, RiskLevel.Medium,
            "Register an agent and issue its first API key.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["application_id"] = AdminMcpSchema.ApplicationRef(),
                ["external_id"] = AdminMcpSchema.String("Stable agent slug, lowercase."),
                ["display_name"] = AdminMcpSchema.String("Display name."),
                ["environment"] = AdminMcpSchema.String("Environment name in this application. Defaults to production.")
            }, ["external_id", "display_name"])),
        Tool("agent_rotate_key", "Rotate agent key", "admin.agent.rotate_key", "agent", PermissionCatalog.AgentsWrite, RiskLevel.High,
            "Rotate an agent API key. The previous key stops working. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["agent_id"] = AdminMcpSchema.String("Agent id (agt_…).")
            }, ["agent_id"]), destructive: true),
        Tool("agent_disable", "Disable agent", "admin.agent.disable", "agent", PermissionCatalog.AgentsWrite, RiskLevel.High,
            "Disable an agent so authorize calls fail closed. Key hashes are kept. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["agent_id"] = AdminMcpSchema.String("Agent id (agt_…).")
            }, ["agent_id"]), destructive: true, idempotent: true),
        Tool("agent_enable", "Enable agent", "admin.agent.enable", "agent", PermissionCatalog.AgentsWrite, RiskLevel.High,
            "Re-enable a disabled agent so it can call /v1/authorize with its current key. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["agent_id"] = AdminMcpSchema.String("Agent id (agt_…).")
            }, ["agent_id"]), idempotent: true),
        Tool("environment_list", "List environments", "admin.environment.list", "environment", PermissionCatalog.EnvironmentsRead, RiskLevel.Low,
            "List environments for a product application and how many agents are bound to each.",
            AdminMcpSchema.Page("Optional page of environments.", includeApplication: true), readOnly: true, paged: true),
        Tool("environment_get", "Get environment", "admin.environment.get", "environment", PermissionCatalog.EnvironmentsRead, RiskLevel.Low,
            "Get one environment by id.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["environment_id"] = AdminMcpSchema.String("Environment id (env_…).")
            }, ["environment_id"]), readOnly: true, idempotent: true),
        Tool("environment_create", "Create environment", "admin.environment.create", "environment", PermissionCatalog.EnvironmentsWrite, RiskLevel.Medium,
            "Create an environment slug such as staging. Agents pick a name at registration; keys do not cross environments.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["application_id"] = AdminMcpSchema.ApplicationRef(),
                ["name"] = AdminMcpSchema.String("Lowercase environment slug such as staging.")
            }, ["name"])),
        Tool("environment_update", "Rename environment", "admin.environment.update", "environment", PermissionCatalog.EnvironmentsWrite, RiskLevel.High,
            "Rename an environment. Authorize clients must send the new name. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["environment_id"] = AdminMcpSchema.String("Environment id (env_…)."),
                ["name"] = AdminMcpSchema.String("Replacement lowercase slug.")
            }, ["environment_id", "name"]), idempotent: true),
        Tool("environment_delete", "Delete environment", "admin.environment.delete", "environment", PermissionCatalog.EnvironmentsWrite, RiskLevel.High,
            "Delete an empty environment. Fails closed if agents are still bound or it is the last environment. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["environment_id"] = AdminMcpSchema.String("Environment id (env_…).")
            }, ["environment_id"]), destructive: true),
        Tool("docs_list", "List setup docs", "admin.docs.list", "doc", PermissionCatalog.EnvironmentsRead, RiskLevel.Low,
            "List product documentation for configuring this Deleguard environment. Full site is at Docs__PublicUrl (default http://localhost:4321).",
            AdminMcpSchema.Empty("No arguments."), readOnly: true, idempotent: true),
        Tool("docs_get", "Get setup doc", "admin.docs.get", "doc", PermissionCatalog.EnvironmentsRead, RiskLevel.Low,
            "Read one setup guide (environment, secrets, isolation, MCP playbook, agents, policies). Call docs_list for slugs.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["slug"] = AdminMcpSchema.StringEnum("Documentation slug from docs_list.", AdminMcpDocs.Slugs)
            }, ["slug"]), readOnly: true, idempotent: true),
        Tool("action_list", "List actions", "admin.action.list", "action", PermissionCatalog.ActionsRead, RiskLevel.Low,
            "List protected actions (name, resource type, risk) for a product application.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["application_id"] = AdminMcpSchema.ApplicationRef()
            }, null, "Optional application filter. Defaults to the default product application."), readOnly: true, idempotent: true),
        Tool("action_create", "Create action", "admin.action.create", "action", PermissionCatalog.ActionsWrite, RiskLevel.Medium,
            "Register a protected action that authorize must know about.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["application_id"] = AdminMcpSchema.ApplicationRef(),
                ["name"] = AdminMcpSchema.String("Action name such as invoice.cancel."),
                ["resource_type"] = AdminMcpSchema.String("Resource type such as invoice."),
                ["risk"] = AdminMcpSchema.StringEnum("Risk level.", "LOW", "MEDIUM", "HIGH", "CRITICAL"),
                ["agent_ids"] = AdminMcpSchema.AgentIds()
            }, ["name", "resource_type", "agent_ids"])),
        Tool("action_update", "Update action", "admin.action.update", "action", PermissionCatalog.ActionsWrite, RiskLevel.Medium,
            "Update resource_type and risk. The action name cannot change. Administration actions (admin.*) are rejected.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["action_id"] = AdminMcpSchema.String("Action id (act_…)."),
                ["resource_type"] = AdminMcpSchema.String("Resource type such as invoice."),
                ["risk"] = AdminMcpSchema.StringEnum("Risk level.", "LOW", "MEDIUM", "HIGH", "CRITICAL"),
                ["agent_ids"] = AdminMcpSchema.AgentIds()
            }, ["action_id", "resource_type", "risk"])),
        Tool("action_delete", "Delete action", "admin.action.delete", "action", PermissionCatalog.ActionsWrite, RiskLevel.High,
            "Delete a protected action and its Guard MCP bindings. Authorize then DENYs that name. Administration actions cannot be deleted. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["action_id"] = AdminMcpSchema.String("Action id (act_…).")
            }, ["action_id"]), destructive: true),
        Tool("policy_list", "List policies", "admin.policy.list", "policy", PermissionCatalog.PoliciesRead, RiskLevel.Low,
            "List policies. YAML is not included; read deleguard://policy/{id} for the document. kind=product hides admin-* catalog policies.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["limit"] = AdminMcpSchema.Integer("Page size from 1 to 50.", AdminMcpPaging.MinLimit, AdminMcpPaging.MaxLimit),
                ["cursor"] = AdminMcpSchema.String("Opaque next_cursor from the previous page."),
                ["kind"] = AdminMcpSchema.StringEnum("Which policies to list.", "all", "product", "administration"),
                ["application_id"] = AdminMcpSchema.ApplicationRef()
            }, null, "Optional page of policy summaries."), readOnly: true, paged: true),
        Tool("policy_get", "Get policy", "admin.policy.get", "policy", PermissionCatalog.PoliciesRead, RiskLevel.Low,
            "Get policy metadata and version numbers. YAML lives on resource deleguard://policy/{id}.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["policy_id"] = AdminMcpSchema.String("Policy id (pol_…).")
            }, ["policy_id"]), readOnly: true, idempotent: true),
        Tool("policy_create", "Create policy", "admin.policy.create", "policy", PermissionCatalog.PoliciesWrite, RiskLevel.Medium,
            "Create a new unpublished policy from YAML. Returns policy_id for policy_test and policy_publish. Register the action with action_create first if it is missing. Does not publish.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["application_id"] = AdminMcpSchema.ApplicationRef(),
                ["yaml"] = AdminMcpSchema.String("Policy YAML document. The YAML id becomes the policy slug."),
                ["agent_ids"] = AdminMcpSchema.AgentIds()
            }, ["yaml", "agent_ids"])),
        Tool("policy_create_version", "Create policy version", "admin.policy.create_version", "policy", PermissionCatalog.PoliciesWrite, RiskLevel.Medium,
            "Add a draft policy version from YAML. Does not publish. Requires an existing policy_id from policy_create or policy_list.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["policy_id"] = AdminMcpSchema.String("Policy id (pol_…)."),
                ["yaml"] = AdminMcpSchema.String("Policy YAML document. YAML id must match the existing policy slug.")
            }, ["policy_id", "yaml"])),
        Tool("policy_test", "Test policy", "admin.policy.test", "policy", PermissionCatalog.PoliciesRead, RiskLevel.Low,
            "Dry-run a policy fixture without writing a decision.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["policy_id"] = AdminMcpSchema.String("Policy id (pol_…)."),
                ["yaml"] = AdminMcpSchema.String("Optional YAML override. Latest version is used when omitted."),
                ["expected_effect"] = AdminMcpSchema.StringEnum("Expected effect when asserting a fixture.", "ALLOW", "DENY", "REQUIRE_APPROVAL"),
                ["agent_id"] = AdminMcpSchema.String("Agent external id for the fixture."),
                ["environment"] = AdminMcpSchema.String("Environment name."),
                ["action"] = AdminMcpSchema.String("Action name."),
                ["resource_type"] = AdminMcpSchema.String("Resource type."),
                ["resource_id"] = AdminMcpSchema.String("Resource id.")
            }, ["policy_id"]), readOnly: true, idempotent: true),
        Tool("policy_publish", "Publish policy", "admin.policy.publish", "policy", PermissionCatalog.PoliciesPublish, RiskLevel.Critical,
            "Publish a policy version. CRITICAL: operators need console approval. Test first with policy_test.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["policy_id"] = AdminMcpSchema.String("Policy id (pol_…)."),
                ["version_id"] = AdminMcpSchema.String("Immutable version id (pver_…).")
            }, ["policy_id", "version_id"]), idempotent: true),
        Tool("policy_delete", "Delete policy", "admin.policy.delete", "policy", PermissionCatalog.PoliciesWrite, RiskLevel.High,
            "Delete a product policy and all of its versions. Authorize then DENYs that action if no other published policy remains. Administration catalog policies cannot be deleted. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["policy_id"] = AdminMcpSchema.String("Policy id (pol_…).")
            }, ["policy_id"]), destructive: true),
        Tool("mcp_status", "MCP status", "admin.mcp.status", "mcp", PermissionCatalog.McpRead, RiskLevel.Low,
            "Show Administration MCP enabled flag and tool list.",
            AdminMcpSchema.Empty("No arguments."), readOnly: true, idempotent: true),
        Tool("mcp_issue_token", "Issue MCP token", "admin.mcp.issue_token", "mcp", PermissionCatalog.McpWrite, RiskLevel.High,
            "Issue a dgm_ token for a user. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["user_id"] = AdminMcpSchema.String("User id (usr_…).")
            }, ["user_id"])),
        Tool("mcp_revoke_token", "Revoke MCP token", "admin.mcp.revoke_token", "mcp", PermissionCatalog.McpWrite, RiskLevel.High,
            "Revoke an Administration MCP token.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["credential_id"] = AdminMcpSchema.String("Credential id (dgm credential row id).")
            }, ["credential_id"]), destructive: true, idempotent: true),
        Tool("mcp_set_tools", "Set MCP tools", "admin.mcp.set_tools", "mcp", PermissionCatalog.McpWrite, RiskLevel.High,
            "Enable or disable the Administration MCP and the tools it exposes.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["enabled"] = AdminMcpSchema.Boolean("Whether the Administration MCP is enabled."),
                ["tools"] = AdminMcpSchema.StringArray("Enabled tool names from this catalog.")
            }, ["enabled", "tools"]), idempotent: true),
        Tool("gateway_status", "AI Gateway status", "admin.gateway.status", "gateway", PermissionCatalog.GatewayRead, RiskLevel.Low,
            "Show whether the optional AI Gateway PEP is enabled, routing mode, remaining budget, token classes, cache knobs, and model catalog. Prompts never leave the PEP.",
            AdminMcpSchema.Empty("No arguments."), readOnly: true, idempotent: true),
        Tool("gateway_update_settings", "Update AI Gateway", "admin.gateway.update_settings", "gateway", PermissionCatalog.GatewayWrite, RiskLevel.High,
            "Enable the AI Gateway PEP and set routing, monthly USD budget, TALE token classes, heuristic/cascade ceilings, and exact/prefix cache. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["enabled"] = AdminMcpSchema.Boolean("Whether agents may call the AI Gateway PEP."),
                ["routing_mode"] = AdminMcpSchema.StringEnum("How the PEP picks a model inside the authorized allowlist. heuristic/cascade skip cheap for tools, JSON schema, or images; length is a fallback.", "static", "heuristic", "cascade"),
                ["monthly_budget_usd"] = AdminMcpSchema.Number("Hard monthly USD ceiling. Exhaustion is DENY, never ALLOW."),
                ["default_token_budget_class"] = AdminMcpSchema.StringEnum("Default output token class.", "short", "standard", "deep"),
                ["short_max_output_tokens"] = AdminMcpSchema.Integer("TALE short class max output tokens.", 1, 128000),
                ["standard_max_output_tokens"] = AdminMcpSchema.Integer("TALE standard class max output tokens.", 1, 128000),
                ["deep_max_output_tokens"] = AdminMcpSchema.Integer("TALE deep class max output tokens.", 1, 128000),
                ["heuristic_cheap_max_tokens"] = AdminMcpSchema.Integer("Estimated input tokens at or below this use the cheap tier when the prompt has no tools, JSON schema, or images.", 1, 128000),
                ["cascade_skip_to_strong_tokens"] = AdminMcpSchema.Integer("Skip cheap and go straight to strong at or above this size (also skipped for tools, JSON schema, or images).", 1, 128000),
                ["cascade_max_attempts"] = AdminMcpSchema.Integer("Hard cap on cascade LLM calls. FrugalGPT-style bound; max 2.", 1, 2),
                ["cascade_max_usd"] = AdminMcpSchema.Number("Do not plan a cheap-then-strong cascade above this USD."),
                ["exact_cache_ttl_seconds"] = AdminMcpSchema.Integer("Exact-match cache TTL after ALLOW. Zero disables cache. Never used for tool loops.", 0, 86400),
                ["prefix_cache_enabled"] = AdminMcpSchema.Boolean("Send a prefix cache key from system messages only."),
                ["shadow_router_enabled"] = AdminMcpSchema.Boolean("Log RouteLLM-style would_route. Never used for ALLOW/DENY.")
            }, ["enabled", "routing_mode", "monthly_budget_usd", "default_token_budget_class"]), idempotent: true),
        Tool("gateway_upsert_route", "Upsert catalog route", "admin.gateway.upsert_route", "gateway", PermissionCatalog.GatewayWrite, RiskLevel.High,
            "Create or update a model alias in the org catalog (provider, prices, tier, region, fallback). HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["route_id"] = AdminMcpSchema.String("Existing route id (rte_…). Omit to create or match by alias."),
                ["alias"] = AdminMcpSchema.String("Stable client model name, e.g. chat.cheap."),
                ["provider"] = AdminMcpSchema.String("PEP provider key, e.g. openai."),
                ["model"] = AdminMcpSchema.String("Upstream model id."),
                ["tier"] = AdminMcpSchema.StringEnum("cheap, strong, or embed.", "cheap", "strong", "embed"),
                ["region"] = AdminMcpSchema.String("Region slug. Failover stays in this region."),
                ["fallback_group"] = AdminMcpSchema.String("Allowlist group, e.g. chat or embed."),
                ["input_price_per_million"] = AdminMcpSchema.Number("USD per million input tokens."),
                ["output_price_per_million"] = AdminMcpSchema.Number("USD per million output tokens."),
                ["sort_order"] = AdminMcpSchema.Integer("Order inside the fallback group.", 0, 9999),
                ["enabled"] = AdminMcpSchema.Boolean("When false the PEP omits the route from the catalog.")
            }, ["alias", "provider", "model", "tier", "region", "fallback_group"]), idempotent: true),
        Tool("gateway_apply_openai_chat_pair", "Add OpenAI chat pair", "admin.gateway.apply_openai_chat_pair", "gateway", PermissionCatalog.GatewayWrite, RiskLevel.High,
            "Upsert chat.cheap, chat.strong, and chat.default (same fallback group and region, seed prices). HIGH risk: operators need console approval. Provider keys stay on the PEP.",
            AdminMcpSchema.Empty("No arguments."), idempotent: true),
        Tool("gateway_delete_route", "Delete catalog route", "admin.gateway.delete_route", "gateway", PermissionCatalog.GatewayWrite, RiskLevel.High,
            "Remove a model alias from the org catalog. HIGH risk: operators need console approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["route_id"] = AdminMcpSchema.String("Route id (rte_…).")
            }, ["route_id"]), destructive: true, idempotent: true),
        Tool("webhook_list", "List webhooks", "admin.webhook.list", "webhook", PermissionCatalog.WebhooksRead, RiskLevel.Low,
            "List webhook endpoints. Signing secrets are never returned; only secret_prefix. Defaults to the default product application.",
            AdminMcpSchema.Page("Optional page of webhook endpoints.", includeApplication: true), readOnly: true, paged: true),
        Tool("webhook_get", "Get webhook", "admin.webhook.get", "webhook", PermissionCatalog.WebhooksRead, RiskLevel.Low,
            "Get one webhook endpoint by id. Does not return the signing secret.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["endpoint_id"] = AdminMcpSchema.String("Webhook endpoint id (whk_…).")
            }, ["endpoint_id"]), readOnly: true, idempotent: true),
        Tool("webhook_create", "Create webhook", "admin.webhook.create", "webhook", PermissionCatalog.WebhooksWrite, RiskLevel.Medium,
            "Create a webhook endpoint and issue its signing secret once. Private, loopback, and metadata URLs are rejected.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["application_id"] = AdminMcpSchema.ApplicationRef(),
                ["description"] = AdminMcpSchema.String("Human label, 200 characters or fewer."),
                ["url"] = AdminMcpSchema.String("Public HTTPS callback URL."),
                ["event_types"] = AdminMcpSchema.StringEnumArray(
                    "Event types to subscribe. Omit or pass [] for every event.",
                    [.. WebhookEventTypes.Subscribable])
            }, ["description", "url"])),
        Tool("webhook_update", "Update webhook", "admin.webhook.update", "webhook", PermissionCatalog.WebhooksWrite, RiskLevel.High,
            "Change URL, events, description, or enabled/disabled. HIGH risk: operators need console approval. Changing URL redirects signed events.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["endpoint_id"] = AdminMcpSchema.String("Webhook endpoint id (whk_…)."),
                ["description"] = AdminMcpSchema.String("Replacement label."),
                ["url"] = AdminMcpSchema.String("Replacement public HTTPS URL."),
                ["event_types"] = AdminMcpSchema.StringEnumArray(
                    "Replacement event types. Pass [] for every event.",
                    [.. WebhookEventTypes.Subscribable]),
                ["status"] = AdminMcpSchema.StringEnum("Enable or disable delivery.", "enabled", "disabled")
            }, ["endpoint_id"]), idempotent: true),
        Tool("webhook_rotate_secret", "Rotate webhook secret", "admin.webhook.rotate_secret", "webhook", PermissionCatalog.WebhooksWrite, RiskLevel.High,
            "Issue a new signing secret. The previous secret stays valid until the next rotation. HIGH risk: operators need console approval. Shown once.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["endpoint_id"] = AdminMcpSchema.String("Webhook endpoint id (whk_…).")
            }, ["endpoint_id"]), destructive: true),
        Tool("webhook_ping", "Ping webhook", "admin.webhook.ping", "webhook", PermissionCatalog.WebhooksWrite, RiskLevel.Medium,
            "Queue a webhook.ping and deliver it now. Fails if the endpoint is disabled.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["endpoint_id"] = AdminMcpSchema.String("Webhook endpoint id (whk_…).")
            }, ["endpoint_id"])),
        Tool("webhook_attempts", "List webhook attempts", "admin.webhook.attempts", "webhook", PermissionCatalog.WebhooksRead, RiskLevel.Low,
            "List recent delivery attempts for one endpoint.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["endpoint_id"] = AdminMcpSchema.String("Webhook endpoint id (whk_…).")
            }, ["endpoint_id"]), readOnly: true, idempotent: true),
        Tool("webhook_resend", "Resend webhook message", "admin.webhook.resend", "webhook", PermissionCatalog.WebhooksWrite, RiskLevel.Medium,
            "Requeue one webhook message and deliver it now.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["endpoint_id"] = AdminMcpSchema.String("Webhook endpoint id (whk_…)."),
                ["message_id"] = AdminMcpSchema.String("Webhook message id (msg_…).")
            }, ["endpoint_id", "message_id"])),
        Tool("webhook_recover", "Recover webhook deliveries", "admin.webhook.recover", "webhook", PermissionCatalog.WebhooksWrite, RiskLevel.Medium,
            "Requeue currently failed messages created within the fixed inclusive since/until interval. Preserve both instants while awaiting approval.",
            AdminMcpSchema.Object(new JsonObject
            {
                ["endpoint_id"] = AdminMcpSchema.String("Webhook endpoint id (whk_…)."),
                ["since"] = AdminMcpSchema.String("Explicit ISO-8601 start instant."),
                ["until"] = AdminMcpSchema.String("Fixed ISO-8601 cutoff, not later than now.")
            }, ["endpoint_id", "since", "until"])),
        Tool("approval_list", "List approvals", "admin.approval.list", "approval", PermissionCatalog.ApprovalsRead, RiskLevel.Low,
            "List pending approvals. Canonical facts are on deleguard://approval/{id}. There is no approve tool.",
            AdminMcpSchema.Page("Optional page of pending approvals."), readOnly: true, paged: true),
        Tool("audit_list", "List audit events", "admin.audit.list", "audit_event", PermissionCatalog.AuditRead, RiskLevel.Low,
            "List recent audit events. Decision details are on deleguard://decision/{id}.",
            AdminMcpSchema.Page("Optional page of audit events."), readOnly: true, paged: true)
    ];

    public static IReadOnlyList<string> ToolNames { get; } = Tools.Select(tool => tool.ToolName).ToList();

    public static AdminMcpToolSpec? Find(string toolName) =>
        Tools.FirstOrDefault(tool => string.Equals(tool.ToolName, toolName, StringComparison.Ordinal));

    public static IReadOnlyList<string> InheritEnabledTools(IReadOnlyList<string> enabled)
    {
        var set = enabled.ToHashSet(StringComparer.Ordinal);
        if (set.Contains("policy_create_version"))
        {
            set.Add("policy_create");
        }

        if (set.Contains("gateway_upsert_route"))
        {
            set.Add("gateway_apply_openai_chat_pair");
        }

        if (set.Contains("environment_list") || set.Contains("agent_list") || set.Contains("environment_create"))
        {
            set.Add("organization_get");
            set.Add("application_list");
        }

        if (set.Contains("environment_create") || set.Contains("agent_create"))
        {
            set.Add("application_create");
        }

        if (set.Count > 0)
        {
            set.Add("docs_list");
            set.Add("docs_get");
        }

        return ToolNames.Where(set.Contains).ToList();
    }

    private static AdminMcpToolSpec Tool(
        string name,
        string title,
        string action,
        string resourceType,
        string permission,
        RiskLevel risk,
        string description,
        JsonObject input,
        bool readOnly = false,
        bool destructive = false,
        bool idempotent = false,
        bool paged = false) =>
        new(
            name,
            title,
            action,
            resourceType,
            permission,
            risk,
            description,
            AdminMcpSchema.Element(input),
            AdminMcpSchema.Envelope,
            readOnly,
            destructive && !readOnly,
            idempotent || readOnly,
            paged);
}
