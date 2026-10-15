# ADR-001 - No LLM in the authorization decision path

Status: Accepted for MVP.

## Decision

Authorization decisions are deterministic and policy-driven. LLMs may assist administrators in drafting policy later, but never execute synchronously to decide ALLOW/DENY/APPROVAL.

## Rationale

- predictable latency;
- reproducibility;
- auditability;
- prompt injection resistance;
- no model/provider outage dependency;
- easier security certification.
