---
name: deleguard-add-action
description: Registers a new protected Deleguard action and wires authorize through evidence. Use when adding actions such as invoice.cancel, email.send, or sql.execute.
---

# Add a protected action

1. Add an `ActionDefinition` with name, resource type, and risk (`LOW|MEDIUM|HIGH|CRITICAL`).
2. Add a policy YAML that matches that action. HIGH/CRITICAL should REQUIRE_APPROVAL or DENY by default.
3. Confirm `POST /v1/authorize` persists a decision before returning.
4. Confirm ALLOW issues a permit bound to resource id/snapshot hash.
5. Confirm `POST /v1/executions` rejects evidence without a valid permit.
6. Add at least one unit fixture and one HTTP/integration case.
