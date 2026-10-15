namespace Deleguard.Application.Identity;

public sealed record AdminMcpDoc(
    string Slug,
    string Title,
    string Summary,
    string SitePath,
    string SitePathEn,
    string Markdown);

public static class AdminMcpDocs
{
    public const string DefaultPublicUrl = "http://localhost:4321";
    public const string ResourcePrefix = "deleguard://docs/";

    public static readonly IReadOnlyList<AdminMcpDoc> All =
    [
        new(
            "environment-setup",
            "Environment setup",
            "Docker Compose ports, host vs Compose, and how to bring Deleguard up in this workspace.",
            "/start/environment/",
            "/en/start/environment/",
            """
            # Environment setup

            Deleguard is one API plus one worker. Postgres is the source of truth. Redis is optional cache.

            ## Surfaces in this workspace (Compose)

            | Surface | URL |
            | --- | --- |
            | API + console | http://localhost:8082 |
            | Health | http://localhost:8082/health |
            | Admin MCP | http://localhost:8082/mcp |
            | Docs | http://localhost:4321 |
            | Keycloak | http://localhost:8081 |
            | Mailpit | http://localhost:8025 |

            Host .NET (stores in Docker only): API is http://127.0.0.1:5080. Docs still bind to http://localhost:4321 when the `docs` Compose service is up.

            ## Bring the stack up

            Full Compose (API, worker, Keycloak, docs):

            ```
            docker compose -f deploy/docker-compose.yml up --build
            ```

            Docs only:

            ```
            docker compose -f deploy/docker-compose.yml up -d --build docs
            ```

            Data stores only (API on the host):

            ```
            docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.dev.yml up -d postgres redis
            dotnet run --project src/Deleguard.Api --no-launch-profile --urls http://127.0.0.1:5080
            ```

            ## Fail closed

            If Postgres or the PDP is down, authorize returns HTTP 503 and never ALLOW. Redis down must not change the decision.
            DENY is HTTP 200 with decision=DENY. Missing policy, missing attributes, or unknown action is DENY.

            Next: read `configuration`, then `configure-with-mcp`.
            """),
        new(
            "configuration",
            "Configuration and secrets",
            "Environment variables and secrets that must be set for the environment you are configuring.",
            "/reference/configuration/",
            "/en/reference/configuration/",
            """
            # Configuration and secrets

            Nested ASP.NET keys use `__` in the environment. Secrets come from the orchestrator, never the image.

            | Key | Local Compose | Change outside local |
            | --- | --- | --- |
            | ConnectionStrings__Postgres | Host=postgres;...User=deleguard | Provider string, TLS, least privilege |
            | Redis__ConnectionString | redis:6379 | Optional; correctness does not depend on Redis |
            | Permits__SigningKey | deleguard-dev-permit-hmac-key-change-in-prod-32 | ≥ 32 chars, rotate in KMS |
            | DemoAuth__ApiKeyPepper | deleguard-dev-pepper | Rotating re-hashes agent keys |
            | DemoAuth__ConsoleToken | dgc_demo_console_local_dev_only | Replace with SSO |
            | DemoAuth__DemoOrganizationId | org_demo | Do not reuse in prod |
            | Docs__PublicUrl | http://localhost:4321 | Public docs origin for this environment |
            | Oidc__Enabled | true in Compose | SSO for console humans |
            | Oidc__Authority | http://localhost:8081/realms/deleguard | Your IdP |
            | DELEGUARD_ENTRYPOINT | api or worker | Same image, two processes |

            Demo credentials (local only):

            - Agent: `dgk_demo_invoice_agent_local_dev_only` — calls POST /v1/authorize
            - Console: `dgc_demo_console_local_dev_only` — humans, never /v1/authorize
            - Admin MCP: `dgm_demo_admin_local_dev_only` — this server; cannot approve; cannot call authorize as an agent

            Production credentials of one environment must not authorize another (production vs staging).
            """),
        new(
            "tenants-environments",
            "Tenants and environments",
            "Organization isolation and why production and staging must not share API keys.",
            "/concepts/tenants-environments/",
            "/en/concepts/tenants-environments/",
            """
            # Tenants and environments

            Every decision, agent, policy, and audit event carries organization_id. A console token sees one tenant.

            Environments (production, staging, qa) hang off a product application. Call application_list first (or application_create). Then environment_create with application_id and a lowercase slug.
            Agents bind to one application and one environment at registration. A production API key must not authorize a staging agent.

            Rules:

            - Each application must keep at least one environment.
            - Delete fails closed if agents are still bound.
            - Rename keeps env_… id; authorize clients must send the new name.
            - Published policies evaluate in the authenticated agent's environment. Keep distinct policy sets if thresholds differ.

            Seed in this workspace creates product `default` with `production` (env_demo_production) and a hidden Administration application for admin-mcp. Add `staging` when you need isolation.
            """),
        new(
            "configure-with-mcp",
            "Configure this environment with Admin MCP",
            "Ordered playbook: docs → environment → agent → action → policy test → publish. Humans approve in the console.",
            "/start/environment/",
            "/en/start/environment/",
            """
            # Configure this environment with Admin MCP

            Use this order. Do not skip tests. There is no approve or deny tool.

            1. docs_list, then docs_get for `environment-setup`, `configuration`, and `tenants-environments`.
            2. application_list. Create a product app with application_create if needed (name + lowercase slug). Pass application_id (or slug) to later tools; omit to use the default product application. Never mix Administration.
            3. environment_list with that application_id. If the target slug is missing, environment_create with a lowercase name (staging, qa, production).
            4. agent_create with application_id and that environment name. Copy the dgk_ key once. Keys do not cross environments.
            5. action_create for each protected action (name such as invoice.cancel, resource_type, risk, agent_ids).
            6. policy_create with YAML and agent_ids (does not publish). Read deleguard://policy/{id}.
            7. policy_test with ALLOW, DENY, and REQUIRE_APPROVAL fixtures.
            8. policy_publish only after tests pass. Non-org-admin callers get REQUIRE_APPROVAL — tell the human to open /approvals/{id}.
            9. Point operators at Docs__PublicUrl (default http://localhost:4321) for console screenshots.
            10. Optional AI Gateway: use prompt `configure-ai-gateway` (gateway_status, enable, gateway_apply_openai_chat_pair). Provider keys stay on the PEP. Humans open /access/ai-gateway and /approvals/{id}.

            Never:

            - Invent an approve/deny tool
            - Call /v1/authorize with a console (dgc_) or MCP (dgm_) token
            - Share production keys with staging
            - Log prompts, chain-of-thought, or raw model output
            """),
        new(
            "console-environments",
            "Console: environments",
            "Create, rename, and delete isolation boundaries from the console or MCP tools.",
            "/console/environments/",
            "/en/console/environments/",
            """
            # Console environments

            Page: /environments (Configure). MCP: environment_list, environment_get, environment_create, environment_update, environment_delete.

            Create: lowercase slug. That string is agent.environment on authorize.
            Rename: HIGH risk; operators need org-admin approval. Clients must send the new name.
            Delete: blocked if agents are bound or it is the last environment. HIGH risk for operators.

            Permission: environments.read to list; environments.write to mutate.
            """),
        new(
            "agents-keys",
            "Agents and API keys",
            "Register a workload, bind it to an environment, and treat the dgk_ secret as one-time.",
            "/console/agents/",
            "/en/console/agents/",
            """
            # Agents and API keys

            MCP: agent_list, agent_create, agent_rotate_key, agent_disable, agent_enable.

            agent_create arguments:
            - application_id: product application id or slug (defaults to the default product application)
            - external_id: lowercase slug used as agent.id on authorize
            - display_name: human label
            - environment: environment name in that application (defaults to production). Create the environment first.

            The API key (dgk_) is shown once. Deleguard stores only a hash. Rotate immediately invalidates the previous key (HIGH risk).
            Disable fails closed for authorize (HIGH risk). Console (dgc_) and MCP (dgm_) tokens cannot call /v1/authorize.
            """),
        new(
            "policies-actions",
            "Actions and policies",
            "Register the action, author YAML, test fixtures, then publish. Default deny.",
            "/console/policies/",
            "/en/console/policies/",
            """
            # Actions and policies

            Unknown action is DENY. Register with action_create before agents call authorize.

            Policy YAML is the customer contract (not raw Rego). Precedence: explicit DENY > REQUIRE_APPROVAL > ALLOW > fallback DENY.
            Missing fields fail the condition (fail closed).

            MCP:
            - policy_create: unpublished draft, returns policy_id
            - policy_create_version: add a draft to an existing policy
            - policy_get: metadata; YAML is on deleguard://policy/{id}
            - policy_test: dry-run, no decision written
            - policy_publish: CRITICAL; operators need console approval

            Prompt `safe-publish-checklist` walks this path. Prompt `configure-environment` starts from environment setup.
            """),
        new(
            "ai-gateway",
            "AI Gateway catalog and budgets",
            "Connect agents with base URL + dgk_ key + a catalog alias. Configure spend, OpenAI chat pair, and routing. Policy still decides ALLOW/DENY.",
            "/console/ai-gateway/",
            "/en/console/ai-gateway/",
            """
            # AI Gateway catalog and budgets

            Console: /access/ai-gateway. Docs: /console/ai-gateway/. MCP: gateway_status, gateway_update_settings, gateway_apply_openai_chat_pair, gateway_upsert_route, gateway_delete_route. Prompt: configure-ai-gateway.

            Drop-in for agents: base URL http://127.0.0.1:5088/v1, Authorization: Bearer dgk_…, model alias such as chat.default. The PEP authorizes metadata only. Prompts and provider keys never reach the SaaS.

            Setup:

            1. Enable the gateway and set a monthly USD budget. Exhaustion is DENY.
            2. Add the OpenAI chat pair (chat.cheap / chat.strong / chat.default) in the same fallback group and region.
            3. Put Providers__openai__ApiKey on the PEP process. Open /access/ai-gateway for snippets and the spend meter.
            4. Routing (after ALLOW, inside the allowlist): always the requested alias; cheap unless tools/JSON schema/image or the prompt is long; or try cheap and escalate if needed (skip cheap for those tags or a very long prompt). Length is not treated as difficulty by itself.
            5. Optional advanced: output token caps (dispatch may use a lower TALE hint after reserving the worst case), exact-match cache TTL, prefix cache key, shadow would_route logs.

            Do not enable semantic cache for agent tool loops. Do not invent an approve tool. Pending spend is approved at /approvals/{id}.
            """)
    ];

    public static readonly string[] Slugs = All.Select(doc => doc.Slug).ToArray();

    public static AdminMcpDoc? Find(string slug) =>
        All.FirstOrDefault(doc => string.Equals(doc.Slug, slug, StringComparison.Ordinal));

    public static string Uri(string slug) => ResourcePrefix + slug;

    public static object List(string publicUrl)
    {
        var origin = NormalizeOrigin(publicUrl);
        return new
        {
            site_url = origin,
            items = All.Select(doc => new
            {
                slug = doc.Slug,
                title = doc.Title,
                summary = doc.Summary,
                resource_uri = Uri(doc.Slug),
                url = origin + doc.SitePath,
                url_en = origin + doc.SitePathEn
            }).ToList()
        };
    }

    public static object Get(string slug, string publicUrl)
    {
        var doc = Find(slug) ?? throw new RequestValidationException($"Unknown documentation slug '{slug}'. Call docs_list.");
        var origin = NormalizeOrigin(publicUrl);
        return new
        {
            slug = doc.Slug,
            title = doc.Title,
            summary = doc.Summary,
            site_url = origin,
            url = origin + doc.SitePath,
            url_en = origin + doc.SitePathEn,
            markdown = doc.Markdown
        };
    }

    public static string NormalizeOrigin(string? publicUrl)
    {
        var origin = string.IsNullOrWhiteSpace(publicUrl) ? DefaultPublicUrl : publicUrl.Trim();
        return origin.TrimEnd('/');
    }
}
