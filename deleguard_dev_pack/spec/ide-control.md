# IDE control API

JSON snake_case. All reads and writes are scoped to the authenticated organization/application. Agent endpoints additionally bind the project to the authenticated agent; no caller-supplied tenant id is accepted.

| Method/path | Credential | Purpose |
| --- | --- | --- |
| GET /v1/ide/config/{project} | dgk | Project path rules and diff-sharing setting |
| POST /v1/ide/sessions | dgk | Adapter/session report: id, project_id, adapter, version, platform, capabilities |
| POST /v1/ide/permits/consume | dgk | decision_id, operation_id, snapshot_hash; returns acquired and already_consumed |
| GET /v1/ide/projects | console agents.read | Registered projects |
| POST /v1/ide/projects | admin bearer actions.write + policies.write + admin PDP | id, name, agent_id, case_sensitive, share_diff, protected_paths[], denied_paths[] |
| GET /v1/ide/sessions | console audit.read | Recent reported sessions |
| GET /v1/ide/grants | console approvals.read | Recent directory grants |
| POST /v1/ide/grants | admin bearer approvals.resolve + actions.write + policies.write + admin PDP | project_id, directory, actions[], minutes (1–60, default 15) |
| POST /v1/ide/grants/{id}/revoke | same grant permissions | Revoke future access |

Administrative writes return 200 with data, 202 REQUIRE_APPROVAL when the administration policy requires review, or 403 DENY. A 202 does not apply the change; current administrative gateway does not automatically resume callbacks. Razor forms have antiforgery protection; JSON writes require an explicit administrative bearer.

Authorize uses the existing endpoint. IDE resource attributes include project_id, path, destination_path for moves, operation_id, operation_hash and tool_name. Resource snapshot_hash binds the metadata; operation_hash is a local SHA256 over exact tool identity/arguments/path and filesystem snapshot. Context includes source=ide-hook, session_id and adapter. Never use client protected booleans as authority.

A published policy and action grant are required. Project rules can restrict the policy. Explicit YAML DENY or REQUIRE_APPROVAL cannot be relaxed by temporary directory access. Concessions only satisfy review added by project-directory protection where YAML allows. Every operation and consumption checks server state again.

Consumption is at most once per decision and per agent/application operation id. already_consumed is a denial of another release, not permission to execute again. Invalid scope is 404, mismatched/expired/revoked/changed authorization is 409. SQL consumption checks the final release deadline. No endpoint promises exactly-once external execution.

Evidence continues through POST /v1/executions. IDE decisions require an authenticated, scoped consumption record. Late evidence can attach after expiry only if consumption preceded expiry and the permit was not revoked; result remains client-reported. This exception does not apply to ordinary unconsumed permits.

Migration IdeControl creates ide_projects, ide_sessions, ide_grants and ide_permit_uses. No AI Gateway settings are needed. Capability reports are untrusted operational metadata, not attestation. A project is bound to one agent; use another project registration for another agent or changed case semantics.

## Project-local adapter bundle (v3)

The installer writes `.deleguard/{cursor|vscode|claude}/config.json`, a pinned runtime, integrity manifest and independent SQLite journal in the selected workspace, never a machine-wide profile/config for new installations. An OS credential store account derives from API URL, canonical workspace root, project, agent, environment and adapter. No credential is written to MCP configuration, source, argv or logs. The server-authenticated organization/application/agent scope is pinned on installation and checked before requesting authorization; reconnecting with another tenant is rejected.

`install --plan` previews without API calls or credential prompts. Managed files with local modifications require explicit `--replace-managed`; unrelated hooks and MCP entries are preserved. `--migrate-profile` verifies the exact previous project and command, snapshots its journal including WAL and preserves operation identities. Legacy state remains available; migration never executes or automatically resumes an operation. `--reconnect` validates a replacement key before storing it and never clears the journal.

`doctor --config <project-local-config>` is read-only with respect to authorization and execution: it validates local integrity and uses GET config only. Missing policy tests, MCP login and native hook coverage remain unverified. URL-only MCP configuration requires HTTPS and native IDE authentication; it does not establish that the deployed server implements OAuth. Skills and read-only reviewer agents are guidance, not security boundaries. Windows uses current-user DPAPI directly and an OS-path-resolved launcher. Hooks retain their documented partial coverage and host-specific failure semantics.
