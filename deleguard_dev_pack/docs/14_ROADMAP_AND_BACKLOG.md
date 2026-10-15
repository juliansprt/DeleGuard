# 14 - Roadmap and Backlog

## Phase 0 - validation (before heavy build)

- 10-15 customer discovery interviews.
- Prototype clickable console.
- Landing page + waitlist.
- 3 reference integrations: invoice cancellation, production SQL, email sending.
- Validate pricing metric: agents, checks, or enterprise platform fee.

## Sprint 0 - foundation

- solution/repo structure;
- PostgreSQL schema/migrations;
- organization/environment;
- API auth/API keys;
- OpenTelemetry;
- CI/CD and security scanning.

## Sprint 1 - authorization core

- agents/actions registry;
- `POST /v1/authorize`;
- normalized canonical request;
- deterministic PDP adapter;
- default deny;
- decision persistence;
- idempotency.

## Sprint 2 - policies

- product-facing policy schema;
- policy versioning;
- simulator/test fixtures;
- policy publish workflow;
- deny/approval/allow precedence.

## Sprint 3 - approvals

- approval queue;
- web approval UI;
- expiry;
- approve/deny;
- signed short-lived permit;
- webhook notification.

## Sprint 4 - evidence and audit

- execution evidence endpoint;
- receipt model;
- hash-chained audit events;
- timeline UI;
- export JSON/CSV.

## Sprint 5 - SDKs and onboarding

- .NET SDK;
- Python SDK;
- Node SDK;
- quickstart templates;
- action auto-discovery telemetry (non-enforcing preview mode).

## Sprint 6 - private beta hardening

- threat tests;
- quotas/rate limits;
- key rotation;
- multi-tenant security review;
- backup/restore;
- load tests;
- audit integrity validator.

## Phase 2

- Slack/Teams approvals;
- OIDC/IdP integrations;
- actor-agent delegation;
- MCP adapter;
- policy recommendations;
- SIEM export;
- local/self-hosted PEP/PDP.

## Phase 3

- A2A authorization;
- offline-verifiable receipts;
- external provider evidence verification;
- temporary scoped delegation tokens;
- policy-as-code GitOps;
- anomaly/risk scoring (still deterministic baseline + optional ML signals).
