# Deleguard AI Gateway PEP

Client-side OpenAI-compatible policy enforcement point. Agents call this process with **base URL + `dgk_` key + a catalog alias**. It calls `POST /v1/authorize` with **model metadata only**, forwards to the provider **only on ALLOW**, and never sends prompts to Deleguard. Provider keys stay in this process (BYOK).

## Run

```bash
dotnet run --project src/Deleguard.AiGateway --urls http://127.0.0.1:5088
```

Set `Providers__openai__ApiKey` (and any other `Providers__{name}__ApiKey`) on this process. Enable the catalog in the console at `/access/ai-gateway`.

## Connect an agent (drop-in OpenAI)

Default base URL: `http://127.0.0.1:5088/v1`.

```bash
curl http://127.0.0.1:5088/v1/chat/completions \
  -H "Authorization: Bearer dgk_YOUR_AGENT_KEY" \
  -H "Content-Type: application/json" \
  -d '{"model":"chat.default","messages":[{"role":"user","content":"Hello"}]}'
```

The PEP forwards that Bearer token to the PDP. `Deleguard__ApiKey` is an optional local fallback for tests. If neither is present the PEP returns **401**. `Deleguard__AgentId` is optional: the authenticated key already identifies the agent.

Response headers (no prompts): `X-Deleguard-Decision`, `X-Deleguard-Model`, `X-Deleguard-Cache`.

`GET /health` reports `{ status, pdp_configured, providers }` — provider **names** only, never secrets.

Failover after ALLOW stays inside the catalog allowlist (same fallback group and region) on **429**, **503**, and other **5xx**.

Humans approve pending spend in the console at `/approvals/{id}`. This process has no approve tool.
