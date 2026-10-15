# Start here

If you are beginning implementation today, use this sequence.

## Day 1 - lock the contract

1. Read `docs/02_PRD_MVP.md` and `docs/04_AUTHORIZATION_MODEL.md`.
2. Review `spec/openapi.yaml`.
3. Freeze the MVP response states: `ALLOW`, `DENY`, `REQUIRE_APPROVAL`.
4. Keep the LLM outside the authorization decision path.

## First vertical slice

Implement one end-to-end scenario only:

`custom agent -> POST /v1/authorize -> deterministic policy -> ALLOW/DENY/REQUIRE_APPROVAL -> approval -> permit -> execution evidence -> audit view`

Recommended demo action:

`invoice.cancel`

because it naturally exercises resource attributes, thresholds, approval and evidence.

## Suggested initial solution layout

```text
src/
  Deleguard.Api/
  Deleguard.Application/
  Deleguard.Domain/
  Deleguard.Infrastructure/
  Deleguard.Policy/
  Deleguard.Workers/
tests/
  Deleguard.UnitTests/
  Deleguard.IntegrationTests/
  Deleguard.SecurityTests/
sdk/
  dotnet/
  python/
  node/
```

## Definition of first usable milestone

A developer can register a custom agent, call `POST /v1/authorize`, configure one resource-aware policy, receive a deterministic decision, approve a pending action, report its execution result, and inspect an append-only audit trail.

Do not implement model routing, memory, generic agent orchestration or an LLM observability platform before this vertical slice works.
