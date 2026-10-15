# 15 - Deployment and Operations

## MVP topology

```text
Internet/API clients
  -> API ingress/WAF
  -> Deleguard ASP.NET API
      -> PostgreSQL
      -> Redis
      -> OPA/PDP
      -> Background worker/outbox
  -> Web console
```

## Production principles

- stateless API replicas;
- tenant-scoped DB access;
- secrets in managed KMS/vault;
- database PITR;
- Redis not source of truth;
- policies cached but versioned;
- decision durable before returning allow/approval;
- explicit behavior if PDP unavailable;
- immutable audit APIs;
- structured security logs.

## Failure behavior

| Failure | Recommended behavior |
|---|---|
| DB unavailable | deny/error, never fabricate allow |
| PDP unavailable | fail closed for protected actions |
| approval notification unavailable | request remains pending |
| audit exporter unavailable | transactional outbox retains event |
| external SIEM unavailable | authorization continues if core ledger durable |

## Environments

Keep organization policy sets isolated per environment. Production agent credentials must not authorize staging agents and vice versa.
