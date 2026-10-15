# Deleguard agent instructions

Deleguard is a **Policy Decision + Approval + Evidence** platform. Custom agents call `POST /v1/authorize` and receive `ALLOW`, `DENY`, or `REQUIRE_APPROVAL`. The model is never in the decision path.

## Read first

- `deleguard_dev_pack/START_HERE.md`
- `deleguard_dev_pack/docs/02_PRD_MVP.md`
- `spec` contracts in `deleguard_dev_pack/spec/`

## Code layout

- `src/Deleguard.Api` HTTP + Razor approval console
- `src/Deleguard.Application` use-case handlers
- `src/Deleguard.Domain` entities and invariants
- `src/Deleguard.Policy` YAML DSL + native PDP
- `src/Deleguard.Infrastructure` EF Core, permits, seed
- `src/Deleguard.Workers` outbox fan-out, webhook delivery, approval expiry
- `src/Deleguard.McpGuard` client Guard MCP (PEP): intercepts `tools/call`, authorizes with `dgk_`, forwards only on ALLOW
- `src/Deleguard.AiGateway` optional OpenAI-compatible PEP: authorizes metadata only, routes on ALLOW, never sends prompts to the SaaS
- `policies/` product YAML

## Cursor artifacts

- Rules: `.cursor/rules/`
- Skills: `deleguard-add-policy`, `deleguard-add-action`, `deleguard-security-eval`
- Subagents: `policy-author`, `authz-reviewer`, `module-keeper`

When changing policies, use `policy-author`. After authorization-path edits, use `authz-reviewer`.
