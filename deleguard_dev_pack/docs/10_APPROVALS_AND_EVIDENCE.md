# 10 - Approvals and Execution Evidence

## Human approval object

Approval UX must show canonical facts:

```text
Agent: TreasuryAgent
Actor: user:123
Action: payment.create
Amount: COP 24,500,000
Destination: Vendor XYZ
Environment: production
Policy: treasury-prod v7
Expires: 15 min
```

Agent-generated prose may be shown only as supplemental context.

## Approval choices

MVP:

- Approve once
- Deny

Phase 2:

- Approve with lower amount/scope
- Approve until time
- Approve N operations
- Approve specific resource/vendor only

## Evidence lifecycle

```text
REQUESTED
 -> DECIDED
 -> APPROVAL_PENDING (optional)
 -> PERMITTED
 -> EXECUTION_REPORTED
 -> VERIFIED (when external verification exists)
```

## Proof-of-execution inspiration

Never treat an agent message like "done" as proof. Evidence should originate from trusted integration layers when possible:

- provider transaction id;
- signed webhook;
- database commit id;
- Git commit/PR SHA;
- deployment id;
- external API response digest.

## Receipt

A receipt binds:

- normalized request digest;
- identity/delegation;
- policy/version;
- decision;
- approval event;
- permit id;
- evidence;
- timestamps;
- event-chain hash.
