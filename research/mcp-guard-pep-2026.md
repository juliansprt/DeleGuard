# MCP Guard PEP research (2026)

Deep research used to pin Guard transport, bypass, approval UX, and credential separation. Product decisions in the implementation plan stay closed: client-side PEP, HTTP PDP, `dgk_` only, no approve tools.

- Parallel run: `trun_c7cf07aa276049ce9274a712651f3000`
- Status: completed
- Result: https://platform.parallel.ai/play/deep-research/trun_c7cf07aa276049ce9274a712651f3000

## Product mapping

| Research recommendation | Deleguard choice |
| --- | --- |
| Guard is the only host-visible MCP server | Documented bypass: replace downstream MCP entries |
| Streamable HTTP canonical, stdio as desktop adapter | `Deleguard.McpGuard` stdio default, `--http` sidecar |
| Three-state PDP, fail closed | `POST /v1/authorize`; 503 does not forward |
| Default deny unmapped tools | `GET /v1/guard/config`; miss is DENY |
| REQUIRE_APPROVAL via poll, not elicitation | `get_decision` tool; humans approve in the console |
| Separate admin vs agent credentials | `dgm_` admin MCP vs `dgk_` Guard |
| Do not host customer MCP secrets in the PDP API | Client binary PEP, not a hosted proxy |

## Executive Insights

- **Single Enforcement Choke Point**: Make the Guard the only MCP server registered with Cursor or Claude Desktop. A gateway cannot protect a downstream server that the host can call directly, so remove direct downstream registrations rather than relying on model instructions.
- **PEP-PDP Separation**: Put protocol enforcement, downstream forwarding, approval state, and evidence capture in the Guard; put policy evaluation in a remote PDP. Cerbos is a useful PDP reference, while Permit is the closest turnkey enforcement-proxy reference. Cerbos itself is documented as a stateless PDP, not a proxy [executive_insights[0]] [4][79.14].
- **Three-State Decision Contract**: Normalize every request to `ALLOW`, `DENY`, or `REQUIRE_APPROVAL`. Only `ALLOW` crosses the downstream boundary. Treat missing mappings, malformed decisions, timeouts, and PDP errors as `DENY`.
- **HTTP for the Real PEP**: Use Streamable HTTP for the host-to-Guard boundary and Guard-to-remote-server boundary. Provide a small stdio bridge only where a desktop host needs a local process. Remote MCP connections use Streamable HTTP [executive_insights[1]] [3].
- **Approval Is Application State**: Return a structured pending result containing a decision identifier and poll endpoint or `get_decision` operation. Do not use elicitation as the authorization mechanism: MCP explicitly separates URL-mode elicitation from MCP authorization [executive_insights[2]] [11].
- **Default-Deny Tool Catalog**: Map `(server_id, tool_name)` to a stable business action and policy resource. Reject every unmapped or ambiguous tool before calling the PDP, and never authorize solely from a tool description.
- **Credential Compartmentalization**: The Guard's administrative MCP credential must never be available to the model or downstream tool arguments. Use a separate short-lived agent credential for host-to-Guard calls and narrowly scoped, audience-bound credentials for each downstream server. MCP servers must validate that tokens are intended for themselves [executive_insights[3]] [12].
- **Evidence, Not Just Logs**: Emit a tamper-evident decision record before execution and an outcome record after execution, including hashes or redacted arguments, PDP version, approval identity, downstream status, and returned-content classification.

## Recommended Architecture: Guard as the Only Host-Visible MCP Server

The recommended topology is:

```text
Cursor / Claude Desktop
        |
        | Streamable HTTP or local stdio bridge
        v
Guard MCP Server / PEP
  - exposes only approved facade tools
  - authenticates agent and human context
  - maps server/tool to action
  - calls remote PDP
  - owns approval state
  - forwards only ALLOW
        |
        +--> Remote PDP: ALLOW | DENY | REQUIRE_APPROVAL
        |
        +--> Downstream MCP client connections
              - one credential per target server
              - audience-bound and least privilege
```

The Guard should implement `tools/list` as a policy-filtered, deterministic catalog and `tools/call` as the enforcement boundary. It should not simply relay arbitrary JSON-RPC messages. For each call, normalize the target identity, tool name, arguments, caller, agent, tenant, environment, and request correlation ID. Resolve the tool mapping before policy evaluation, then submit a policy input such as:

```json
{
  "subject": {"agent_id": "...", "human_id": "...", "tenant": "..."},
  "resource": {"mcp_server": "github-prod", "action": "repo.issue.create"},
  "action": "execute",
  "context": {"tool": "create_issue", "arguments_hash": "...", "approval_id": null}
}
```

The PDP response should be versioned and machine validated:

```json
{
  "decision": "ALLOW",
  "decision_id": "pdp-...",
  "policy_version": "...",
  "expires_at": "...",
  "obligations": {"redact": ["token"]}
}
```

`DENY` returns a normal MCP tool error without contacting downstream. `ALLOW` causes the Guard to invoke the downstream MCP client using a server-specific credential. `REQUIRE_APPROVAL` creates a pending record containing the exact original request, an arguments hash, policy decision, expiry, and an approval URL or UI reference. Never allow the model to alter the request while it is pending; the resumed request must match the stored hash or be evaluated again.

This design also makes the bypass rule explicit: downstream MCP servers must not be present in the host's configuration. If the host registers both Guard and the original servers, the model can select the original tool and bypass the Guard. This is not a policy failure inside the Guard; it is a topology failure. Multiple connected servers also create cross-server tool-poisoning and credential-confusion risks [recommended_architecture_guard_as_the_only_host_visible_mcp_server[0]] [14]. Use network policy, DNS or service identity controls, and downstream firewall rules to ensure only the Guard identity can reach protected servers.

## Permit.io, Cerbos, Kong, and Cloudflare: What to Copy and What to Avoid

| Product | Copy | Do not assume it provides |
|---|---|---|
| Permit MCP Gateway | Transparent enforcement proxy, real-time authorization, consent, trust ceilings, approval queues, and decision audit | A generic arbitrary remote PDP adapter or content-aware prompt-injection defense |
| Cerbos MCP | Stateless PDP separation, declarative tool policies, contextual decisions, and structured authorization logs | A ready-made MCP traffic proxy or host-bypass prevention |
| Kong MCP Traffic Gateway | Edge authentication, ACLs, rate limits, transformations, proxying, metrics, and MCP request logs | The requested three-state business approval PDP |
| Cloudflare MCP and AI Gateway | Internet edge, OAuth/access control, remote MCP hosting, and operational controls | A documented per-tool ALLOW/DENY/REQUIRE_APPROVAL engine |

Permit is the best product pattern to copy when the objective is a turnkey enforcement proxy. Its documented flow authenticates the caller, identifies the agent and authorizing human, checks the tool against policy, forwards allowed calls, and logs the decision [permit_io_cerbos_kong_and_cloudflare_what_to_copy_and_what_to_avoid[0]] [8][70.74-90]. Its trust-ceiling model is especially useful: the human grant cannot exceed the administrator ceiling [permit_io_cerbos_kong_and_cloudflare_what_to_copy_and_what_to_avoid[0]] [8]. Its HITL features include per-tool, per-server, and per-trust-level approval policies, approval queues, and approval history [permit_io_cerbos_kong_and_cloudflare_what_to_copy_and_what_to_avoid[1]] [5].

The architectural caution is coupling. Permit documents its control plane and default data plane as the place where policies are defined, decisions evaluated, and audit logs stored [permit_io_cerbos_kong_and_cloudflare_what_to_copy_and_what_to_avoid[0]] [8]. It documents hosted, customer-controlled, and fully on-premises deployment options, but does not document an arbitrary custom remote PDP integration [permit_io_cerbos_kong_and_cloudflare_what_to_copy_and_what_to_avoid[0]] [8][70.172-178]. Also copy its boundary: it does not claim to validate upstream server business logic or inspect returned data, and it treats prompt-injection defense as a client and model-layer responsibility [permit_io_cerbos_kong_and_cloudflare_what_to_copy_and_what_to_avoid[0]] [8]. Therefore, use Permit if its control plane and approval model fit; otherwise copy the pattern, not the dependency.

Cerbos is the cleanest reference for the PDP side. Its MCP material describes a stateless PDP queried by MCP servers, with policies controlling access to tools and downstream service-to-service requests [permit_io_cerbos_kong_and_cloudflare_what_to_copy_and_what_to_avoid[2]] [4]. It also describes structured records showing which agent or workload accessed what, when, on whose behalf, and which policy granted access [permit_io_cerbos_kong_and_cloudflare_what_to_copy_and_what_to_avoid[2]] [4]. Build the Guard PEP around that PDP rather than treating Cerbos as the proxy.

Kong is a strong edge and traffic layer. Its MCP gateway provides authentication, ACLs, transformations, rate limiting, and observability, including JSON-RPC methods, payloads, latencies, and errors [permit_io_cerbos_kong_and_cloudflare_what_to_copy_and_what_to_avoid[3]] [6][67.28-29][67.42-44]. Cloudflare is similarly useful for internet-facing transport, OAuth, and operational hosting; its MCP documentation describes remote Streamable HTTP and OAuth account access [permit_io_cerbos_kong_and_cloudflare_what_to_copy_and_what_to_avoid[4]] [3]. Neither source documents the requested business-level three-state PDP contract. Put Kong or Cloudflare in front of the Guard if useful, but keep tool-to-action authorization and approval semantics in the Guard/PDP plane.

## Streamable HTTP Versus stdio for Cursor and Claude Desktop

Use Streamable HTTP as the canonical transport because the PEP is a networked security boundary. It provides a stable endpoint, centralized authentication, horizontal scaling, independent deployment, and a natural place for request correlation and policy enforcement. It also avoids giving a desktop process direct access to downstream credentials.

Use stdio only as a compatibility adapter. The adapter is a local process configured in Claude Desktop or Cursor, and it forwards MCP traffic to the Guard's Streamable HTTP endpoint. The adapter should contain no policy logic, no downstream credentials, and no approval authority. This pattern is appropriate when a host has better support for local MCP processes than remote servers, or when local OAuth browser interaction is required.

The 2026-07-28 specification has important consequences for the HTTP Guard. It removes protocol-level sessions and the `Mcp-Session-Id` header; state spanning requests must use explicit server-minted handles [streamable_http_versus_stdio_for_cursor_and_claude_desktop[0]] [7]. Requests carry protocol version and client capabilities in `_meta`, and servers must implement `server/discover` [streamable_http_versus_stdio_for_cursor_and_claude_desktop[0]] [7]. A broken response stream is retried as a new request with a new request ID, not by SSE replay [streamable_http_versus_stdio_for_cursor_and_claude_desktop[0]] [7]. The Guard should therefore keep approval records, idempotency keys, and correlation state in an external store, not in an MCP session.

For compatibility with older hosts, implement a version-adapter layer. Accept older initialization behavior where required, emit current metadata when supported, and make retry behavior idempotent. Treat a repeated `tools/call` as potentially duplicated: the Guard should use an idempotency key and downstream-specific deduplication for side effects.

## REQUIRE_APPROVAL UX Without Elicitation

Approval should be a Guard-owned asynchronous workflow:

1. The model calls the facade tool.
2. The Guard resolves the action and calls the PDP.
3. The PDP returns `REQUIRE_APPROVAL`.
4. The Guard persists the immutable request and returns a structured MCP result:

```json
{
  "status": "REQUIRE_APPROVAL",
  "decision_id": "dec_123",
  "expires_at": "2026-09-14T12:00:00Z",
  "next": {"method": "get_decision", "decision_id": "dec_123"},
  "message": "Human approval required; downstream execution has not occurred."
}
```

5. The client or host polls `get_decision` using the decision ID. `get_decision` is read-only and is not an approval authority.
6. A separate human-facing approval UI calls an admin approval API using an admin credential. The model never receives that credential and the Guard does not expose `approve` or `deny` as model-callable tools.
7. On approval, the client retries the original facade tool with the decision ID. The Guard verifies subject, target, arguments hash, policy version, expiry, and approval scope before forwarding.
8. On denial or expiry, the Guard returns a terminal denial and never contacts downstream.

This is deliberately not elicitation. Elicitation is for a server to request additional user information through the client [require_approval_ux_without_elicitation[0]] [11]. URL-mode elicitation can support external payment or authorization interactions, but that authorization is separate from MCP authorization, and MCP servers must not rely on it to authorize the user's access to the MCP server [require_approval_ux_without_elicitation[0]] [11]. Using elicitation as a security decision would make approval dependent on host UI behavior and blur the trusted approval principal.

## Tool Mapping, Credentials, Failure Handling, and Evidence

Maintain a signed or controlled registry with one row per exposed tool:

| Field | Requirement |
|---|---|
| `server_id` | Stable identity, not display name or URL alone |
| `tool_name` | Exact downstream name and version |
| `action` | Stable business action, such as `repo.issue.create` |
| `risk` | Read, write, destructive, or credential-affecting |
| `target` | Downstream server and route |
| `argument_policy` | Allowed fields, limits, and redactions |
| `approval_policy` | Conditions producing `ALLOW`, `DENY`, or pending |
| `credential_ref` | Secret reference, never the secret itself |

Default deny must happen before the PDP for an unmapped tool. A mapping failure is an authorization failure, not a request to ask the PDP to guess. Tool descriptions and annotations are untrusted inputs [tool_mapping_credentials_failure_handling_and_evidence[0]] [2]; never derive a privileged action from a description supplied by the model or an unpinned server.

Separate at least three credentials: an administrator credential for the approval API and policy administration, an agent credential for host-to-Guard access, and a per-downstream credential for the Guard-to-server call. The Guard should exchange or mint audience-bound downstream tokens where possible, and must never pass the host bearer token through to a downstream server. The 2026 authorization rules require resource indicators and audience-specific token validation, and prohibit accepting or transiting tokens intended for another resource [tool_mapping_credentials_failure_handling_and_evidence[1]] [12][107.93-99].

Fail closed on PDP timeout, network failure, invalid signature, unknown decision, policy-version mismatch, approval-store failure, mapping-store failure, or downstream credential acquisition failure. Return a safe, non-sensitive error and record the failure. Do not fall back to cached `ALLOW` for writes unless the policy explicitly defines a bounded, signed offline mode; the safer default is no execution.

Record two evidence events. The pre-execution event contains request ID, caller, human delegation, Guard version, target server, exact tool, mapping version, arguments hash, PDP decision, policy version, approval ID, and redaction metadata. The post-execution event contains downstream request ID, start and end times, status, MCP `isError`, response hash or classified summary, and whether the result was transformed or redacted. Permit's audit model is a useful minimum because it records agent, human, tool, server, timestamp, and decision [tool_mapping_credentials_failure_handling_and_evidence[2]] [8]; Cerbos provides the complementary policy attribution model [tool_mapping_credentials_failure_handling_and_evidence[3]] [4]. Never store raw secrets or unrestricted tool output in the audit stream.

## MCP Specification Implications Across the Three Revisions

| Revision | Relevant implication for the Guard |
|---|---|
| 2025-03-26 | Treat tools as arbitrary code execution, require access controls, expose deterministic discovery and enforce consent in the application. The tools specification defines `tools/list` and `tools/call`, while the security guidance says a human should be able to deny invocations [mcp_specification_implications_across_the_three_revisions[0]] [10][114.74-86]. |
| 2025-11-25 | Strengthens explicit consent and tool-safety expectations; tool descriptions and annotations are untrusted, and implementors must build authorization and consent into the application because MCP cannot enforce those principles by itself [mcp_specification_implications_across_the_three_revisions[1]] [2]. Elicitation is an input workflow, not the Guard's approval protocol [mcp_specification_implications_across_the_three_revisions[2]] [11][111.123-124]. |
| 2026-07-28 | Removes protocol sessions, adds per-request metadata and `server/discover`, changes stream-failure retry semantics, and formalizes task polling and cache metadata [mcp_specification_implications_across_the_three_revisions[3]] [7][82.17-19]. The Guard must externalize approval state and make retries idempotent. OAuth resource indicators and audience validation remain transport authorization controls, not a substitute for per-tool PDP decisions [mcp_specification_implications_across_the_three_revisions[4]] [12][107.93-99]. |

The key distinction is transport authorization versus action authorization. MCP OAuth proves that a client may access a protected MCP resource. It does not decide whether this caller may execute `repo.issue.create` with these arguments at this moment. That decision remains the Guard's PEP plus the remote PDP.

## .NET ModelContextProtocol SDK 2.2 Implementation Guidance

Use the official C# SDK as both sides of the Guard: the server side exposes the Guard facade to Cursor or Claude Desktop, and the client side connects to each downstream MCP server. The SDK is explicitly intended for .NET applications, services, and libraries implementing MCP clients and servers [net_modelcontextprotocol_sdk_2_2_implementation_guidance[0]] [1]. Pin the exact `ModelContextProtocol` 2.2 package and test its transport and API surface rather than assuming that an SDK minor version provides a generic authorization middleware hook.

Put enforcement in the application boundary around the SDK, not in tool descriptions and not in downstream handlers. The server-side handler should perform authentication, mapping, PDP call, approval-state lookup, and evidence creation before invoking a downstream client. The downstream client should be created from a credential reference selected by the mapping registry. Register only Guard facade tools, not an unrestricted dynamic proxy that exposes every downstream name.

Test the implementation against four matrices: old and current protocol revisions; HTTP and stdio host adapters; ALLOW, DENY, and approval transitions; and failure injection for PDP, approval store, token exchange, stream break, duplicate retry, and downstream timeout. Verify that no test configuration registers the downstream MCP server in parallel with the Guard.

## Synthesis: The Decision

The best architecture is not a generic MCP reverse proxy with an authorization header. It is a narrow, host-visible Guard that owns the tool catalog and is the sole path to protected downstream servers. Its PEP performs deterministic mapping and fail-closed enforcement; its remote PDP returns the three-state decision; its external approval store handles asynchronous human decisions; and its evidence pipeline records both authorization and execution.

Permit demonstrates the most complete proxy and approval product pattern. Cerbos demonstrates the cleanest PDP separation. Kong and Cloudflare demonstrate valuable edge, authentication, traffic, and observability layers. The non-obvious trade-off is that adding all of them does not automatically produce a secure PEP: edge authentication can establish identity, but only the Guard can bind a specific `(server, tool, arguments, subject)` to a business action and prevent direct host bypass.

Choose Streamable HTTP for the production boundary, stdio only as a credentialless local bridge, and the 2026 stateless model for approval state and retries. Keep admin approval capabilities outside the model's tool namespace. If any direct downstream registration, unmapped tool, invalid token audience, unavailable PDP, or unverifiable approval can still reach a side-effecting server, the system is not yet fail-closed.

## Sources

- https://www.cerbos.dev/features-benefits-and-use-cases/dynamic-authorization-for-MCP-servers
- https://developers.cloudflare.com/agents/model-context-protocol/
- https://modelcontextprotocol.io/specification/2025-11-25/client/elicitation
- https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization
- http://invariantlabs.ai/blog/mcp-security-notification-tool-poisoning-attacks
- https://docs.permit.io/permit-mcp-gateway/overview/
- https://docs.permit.io/permit-mcp-gateway/advanced-features
- https://developer.konghq.com/ai-gateway/mcp/
- https://modelcontextprotocol.io/specification/2026-07-28/changelog
- https://modelcontextprotocol.io/specification/2025-11-25
- https://modelcontextprotocol.io/specification/2025-03-26/server/tools
- https://csharp.sdk.modelcontextprotocol.io/v2/
