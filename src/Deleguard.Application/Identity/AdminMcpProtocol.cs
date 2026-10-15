using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Deleguard.Application.Identity;

public sealed record AdminMcpPromptSpec(
    string Name,
    string Title,
    string Description,
    IReadOnlyList<AdminMcpPromptArgument> Arguments,
    string Template);

public sealed record AdminMcpPromptArgument(string Name, string Description, bool Required);

public static class AdminMcpPrompts
{
    public static readonly IReadOnlyList<AdminMcpPromptSpec> All =
    [
        new(
            "review-pending-approvals",
            "Review pending approvals",
            "Summarize the approval queue. Humans approve in the console; this server has no approve tool.",
            [],
            """
            Call approval_list. Summarize each pending item with approval_id, decision_id, status, and expiry.
            For canonical facts, read resource deleguard://approval/{id}.
            Instruct the human to open /approvals/{id} in the Deleguard console.
            Never claim you can approve or deny. Never invent an approve/deny tool.
            """),
        new(
            "explain-last-deny",
            "Explain a DENY",
            "Explain a DENY using persisted decision facts. Do not suggest bypassing policy.",
            [new AdminMcpPromptArgument("decision_id", "Decision id (dec_…) when known.", false)],
            """
            Explain a DENY from policy, not from speculation.
            If decision_id is {{decision_id}}, read resource deleguard://decision/{{decision_id}}.
            Otherwise call audit_list and look for DENY events, then read deleguard://decision/{id}.
            Quote the stored reason and matched policy. Do not fabricate ALLOW or recommend skipping the PDP.
            """),
        new(
            "safe-publish-checklist",
            "Safe policy publish checklist",
            "Walk through get, YAML review, and policy_test before policy_publish.",
            [new AdminMcpPromptArgument("policy_id", "Policy id (pol_…).", true)],
            """
            If you do not have a policy_id, call policy_create with the YAML. That creates an unpublished draft and returns policy_id plus version_id.
            For policy {{policy_id}}:
            1. Call policy_get for metadata (no YAML).
            2. Read deleguard://policy/{{policy_id}} for the YAML.
            3. Call policy_test with the fixtures you care about.
            4. Only then consider policy_publish. Non-org-admin callers receive REQUIRE_APPROVAL; tell them to use /approvals/{id}.
            Do not skip tests. Do not call an approve tool. policy_create does not publish.
            """),
        new(
            "configure-environment",
            "Configure a Deleguard environment",
            "Read setup docs, then create or verify the environment, agent, actions, and policies for this workspace.",
            [new AdminMcpPromptArgument("environment_name", "Lowercase environment slug such as staging. Defaults to production.", false)],
            """
            Configure Deleguard for environment {{environment_name}}.
            1. Call docs_get with slug configure-with-mcp, then environment-setup, configuration, and tenants-environments.
            2. Call application_list (or application_create). Pass application_id to later tools; omit to use the default product application.
            3. Call environment_list. Create the slug with environment_create if it is missing (lowercase).
            4. Bind new agents with agent_create to that application and environment. Keys do not cross environments.
            5. Register missing actions with action_create (include agent_ids), then policy_create + policy_test before policy_publish.
            5. Send the operator to the docs site URL from docs_list (site_url, default http://localhost:4321).
            Never invent an approve/deny tool. Never use dgc_ or dgm_ tokens on /v1/authorize.
            """),
        new(
            "configure-ai-gateway",
            "Configure the AI Gateway",
            "Enable the optional OpenAI-compatible PEP, add the OpenAI chat pair, and send the human to BYOK plus the console. No approve tool.",
            [],
            """
            Configure the optional AI Gateway. Policy still decides ALLOW, DENY, and REQUIRE_APPROVAL. Prompts never reach Deleguard. There is no approve tool.
            1. Call docs_get with slug ai-gateway.
            2. Call gateway_status. If enabled is false, call gateway_update_settings with enabled true, routing_mode static, a monthly_budget_usd, and default_token_budget_class standard.
            3. If chat.cheap, chat.strong, or chat.default are missing, call gateway_apply_openai_chat_pair (or three gateway_upsert_route calls in the same fallback_group=chat and region=global).
            4. Tell the human to put the provider key on the PEP process only: Providers__openai__ApiKey. Never ask them to paste sk- keys into this MCP or the SaaS.
            5. Tell them to open /access/ai-gateway for the spend meter and copy-paste snippets (base URL http://127.0.0.1:5088/v1 plus Authorization: Bearer dgk_…). Routing after ALLOW: static alias, or cheap unless tools/JSON schema/image/long, or cascade with a bounded escalate. Pending spend is approved at /approvals/{id}.
            Never invent an approve/deny tool. Never send prompts to Deleguard. Never put provider API keys in gateway_update_settings.
            """)
    ];

    public static AdminMcpPromptSpec? Find(string name) =>
        All.FirstOrDefault(prompt => string.Equals(prompt.Name, name, StringComparison.Ordinal));

    public static string Render(AdminMcpPromptSpec spec, JsonElement arguments)
    {
        var text = spec.Template;
        foreach (var argument in spec.Arguments)
        {
            var value = Read(arguments, argument.Name);
            if (argument.Required && string.IsNullOrWhiteSpace(value))
            {
                throw new RequestValidationException($"{argument.Name} is required.");
            }

            text = text.Replace("{{" + argument.Name + "}}", string.IsNullOrWhiteSpace(value) ? "(not provided)" : value, StringComparison.Ordinal);
        }

        return text;
    }

    private static string? Read(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}

public sealed record AdminMcpResourceRef(string Kind, string Id);

public static class AdminMcpResources
{
    public const string PolicyPrefix = "deleguard://policy/";
    public const string DecisionPrefix = "deleguard://decision/";
    public const string ApprovalPrefix = "deleguard://approval/";
    public const string DocsPrefix = AdminMcpDocs.ResourcePrefix;

    private static readonly Regex UriPattern = new(
        "^deleguard://(policy|decision|approval|docs)/([^/?#]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static AdminMcpResourceRef Parse(string uri)
    {
        var match = UriPattern.Match(uri ?? string.Empty);
        if (!match.Success)
        {
            throw new RequestValidationException("Unsupported resource URI. Expected deleguard://policy|decision|approval|docs/{id}.");
        }

        return new AdminMcpResourceRef(match.Groups[1].Value, Uri.UnescapeDataString(match.Groups[2].Value));
    }

    public static string PolicyUri(string id) => PolicyPrefix + id;
    public static string DecisionUri(string id) => DecisionPrefix + id;
    public static string ApprovalUri(string id) => ApprovalPrefix + id;
    public static string DocsUri(string slug) => AdminMcpDocs.Uri(slug);

    private static readonly HashSet<string> SecretNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "temporary_password", "api_key", "token", "secret", "key_hash", "credential"
    };

    public static object? RedactSecrets(object? value) =>
        value is JsonElement element ? Redact(element) : value;

    private static JsonNode? Redact(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => RedactObject(element),
        JsonValueKind.Array => RedactArray(element),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => JsonNode.Parse(element.GetRawText()),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    private static JsonObject RedactObject(JsonElement element)
    {
        var obj = new JsonObject();
        foreach (var property in element.EnumerateObject())
        {
            obj[property.Name] = SecretNames.Contains(property.Name)
                ? "[redacted]"
                : Redact(property.Value);
        }

        return obj;
    }

    private static JsonArray RedactArray(JsonElement element)
    {
        var array = new JsonArray();
        foreach (var item in element.EnumerateArray())
        {
            array.Add(Redact(item));
        }

        return array;
    }
}
