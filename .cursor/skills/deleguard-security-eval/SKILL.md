---
name: deleguard-security-eval
description: Runs Deleguard adversarial authorization checks from the threat model. Use when reviewing security of the authorize path, permits, idempotency, or evidence.
---

# Security eval

Run or extend `tests/Deleguard.SecurityTests` for:

- missing/invalid agent credential → 401
- spoofed `agent.id` ≠ authenticated agent → 400
- unknown/confusable action → DENY
- idempotency key reused with a different body → 409
- evidence on DENY/pending decision → 409
- evidence with a different resource snapshot hash → 409
- no LLM or outbound side effects on the authorize path

Fix fail-open bugs before adding features.
