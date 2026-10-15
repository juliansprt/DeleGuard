# 17 - Outbound webhooks

Deleguard sends signed HTTPS callbacks after durable authorization state changes. Delivery is asynchronous from the transactional outbox and **never** runs on the ALLOW/DENY path.

## Events

| type | When |
|---|---|
| `authorization.decided` | After a decision is persisted (including REQUIRE_APPROVAL) |
| `approval.resolved` | After approve or deny |
| `approval.expired` | When a pending approval times out |
| `execution.reported` | After execution evidence is accepted |
| `webhook.endpoint.disabled` | Operational: endpoint auto-disabled |
| `webhook.ping` | Diagnostic ping from console/API |

## Delivery contract (Standard Webhooks)

`POST` JSON body:

```json
{
  "id": "msg_...",
  "type": "authorization.decided",
  "timestamp": "2026-09-13T12:00:00.0000000Z",
  "data": { }
}
```

Headers:

- `webhook-id` — stable message id (idempotency key; unchanged across retries)
- `webhook-timestamp` — unix seconds of this attempt
- `webhook-signature` — `v1,<base64-hmac>` (space-delimited during secret rotation)

Signed content: `{webhook-id}.{webhook-timestamp}.{raw body}` with HMAC-SHA256 over the endpoint secret (`whsec_` + base64).

Success is any HTTP `2xx`. Timeouts, `5xx`, and most `4xx` retry with exponential backoff. `410 Gone` disables the endpoint. At-least-once: consumers must ignore duplicate `webhook-id`s.

## Configuration

Console: **Configure → Webhooks**. Console API and Administration MCP (never agent keys):

- `GET/POST /v1/webhooks` — MCP `webhook_list`, `webhook_create`
- `GET/PATCH /v1/webhooks/{id}` — MCP `webhook_get`, `webhook_update`
- `POST /v1/webhooks/{id}/rotate-secret` — MCP `webhook_rotate_secret`
- `POST /v1/webhooks/{id}/ping` — MCP `webhook_ping`
- `GET /v1/webhooks/{id}/attempts` — MCP `webhook_attempts`
- `POST /v1/webhooks/{id}/messages/{msgId}/resend` — MCP `webhook_resend`
- `POST /v1/webhooks/{id}/recover` — MCP `webhook_recover`

Permissions: `webhooks.read` to list; `webhooks.write` to mutate. Changing URL or rotating the secret is HIGH risk for operators (console approval). The signing secret is shown once.

Private, loopback, and link-local destinations are rejected (SSRF). Production URLs must be HTTPS.
