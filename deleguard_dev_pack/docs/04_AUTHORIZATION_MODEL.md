# 04 - Authorization Model

## Canonical request

```json
{
  "agent": {"id": "invoice-agent", "environment": "production"},
  "actor": {"id": "usr_481", "type": "human"},
  "action": "invoice.cancel",
  "resource": {
    "type": "invoice",
    "id": "FAC-8392",
    "attributes": {
      "amount": 35000000,
      "currency": "COP",
      "status": "issued",
      "reportedToTaxAuthority": true
    }
  },
  "context": {
    "tenant": "tenant-a",
    "reason": "duplicate invoice",
    "riskSignals": []
  }
}
```

## Decision semantics

### ALLOW

A policy explicitly authorizes the action. Return a bounded permit.

### DENY

No applicable policy authorizes it, an explicit deny wins, identity/delegation is invalid, or a required contextual condition is missing.

### REQUIRE_APPROVAL

The request is potentially legitimate but requires an external human authorization event before a permit can be issued.

## Default deny

Sensitive capabilities should be deny-by-default. Absence of a policy is not permission.

## Explicit deny precedence

Recommended precedence:

1. platform invariant / emergency block;
2. explicit deny;
3. approval requirement;
4. explicit allow;
5. default deny.

## Delegation

Actor and agent are separate principals.

```text
Actor (human/service)
      |
      | delegates bounded authority
      v
Agent
      |
      | requests
      v
Action on Resource
```

A user's entitlement must not automatically grant the agent all user privileges. Policies can constrain the intersection:

```text
allowed = actor_entitlement ∩ agent_capability ∩ contextual_policy
```

## TOCTOU control

For high-risk actions, bind the permit to a stable resource identifier and relevant resource snapshot/hash. If the resource changes materially between decision and execution, require reauthorization.

## Idempotency

`Idempotency-Key` must map the same logical request to the same decision while valid. This protects retries from duplicate approval/action chains.
