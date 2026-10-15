# 05 - API Design

The machine-readable starter is `../spec/openapi.yaml`.

## Public core

### `POST /v1/authorize`

Returns one of:

```json
{"decision":"ALLOW","decision_id":"dec_...","permit":"..."}
```

```json
{"decision":"DENY","decision_id":"dec_...","reason":"..."}
```

```json
{"decision":"REQUIRE_APPROVAL","decision_id":"dec_...","approval_id":"apr_..."}
```

### `GET /v1/decisions/{decision_id}`

Read current decision and approval/execution state.

### `POST /v1/approvals/{approval_id}/approve`

Human/API-controlled approval, optionally narrowing scope or expiry.

### `POST /v1/approvals/{approval_id}/deny`

Deny pending action.

### `POST /v1/executions`

Report execution evidence tied to a decision/permit.

## Admin APIs

- `/v1/agents`
- `/v1/actions`
- `/v1/policies`
- `/v1/policies/{id}/versions`
- `/v1/policies/{id}/test`
- `/v1/audit/events`
- `/v1/webhooks` (console only; see `17_WEBHOOKS.md`)

## Headers

- `Authorization: Bearer ...`
- `Idempotency-Key: ...`
- `X-Deleguard-Agent-Id: ...` optional convenience header
- `traceparent` OpenTelemetry/W3C

## Error model

Never use HTTP success to imply authorization success. A DENY is a valid evaluated response and can be HTTP 200 with `decision=DENY`. Authentication/schema/server failures use 4xx/5xx.
