# 02 - Product Requirements Document - MVP

## Objective

Ship a developer-first SaaS that can be added immediately before an agent executes a sensitive action.

## Primary use case

A customer already has arbitrary agent code:

```csharp
var response = await llm.Call(...);
if (response.Action == "invoice.cancel")
    await CancelInvoice(id);
```

They add one call:

```csharp
var d = await deleguard.AuthorizeAsync(request);
if (d.Allowed)
    await CancelInvoice(id);
```

## MVP user stories

### Agent registration

As a platform engineer, I can register an agent and environment so decisions are attributable to a concrete non-human identity.

### Action registry

As a developer, I can declare actions such as `email.send`, `invoice.cancel`, `sql.execute` and associate a default risk class.

### Authorization

As an agent, I can send `{agent, actor, action, resource, context}` and receive a deterministic decision.

### Policies

As an administrator, I can define ordered, versioned policies using resource/context attributes.

### Human approval

As an approver, I can approve or deny a pending action once, with an expiry and optional narrowed scope.

### Evidence

As an application, I can report whether an authorized action executed and include external provider references.

### Audit

As an auditor, I can reconstruct request -> policy -> decision -> approval -> execution.

## MVP decisions

- `ALLOW`
- `DENY`
- `REQUIRE_APPROVAL`

No fuzzy response. No LLM output in the decision path.

## Risk levels

- `LOW`
- `MEDIUM`
- `HIGH`
- `CRITICAL`

Example default behavior:

| Risk | Default |
|---|---|
| LOW | allow + audit |
| MEDIUM | allow + audit, configurable |
| HIGH | approval |
| CRITICAL | approval + stronger identity/MFA or deny |

## Functional acceptance criteria

- Multi-tenant isolation.
- Versioned policies.
- Fail-closed mode available and default for high/critical actions.
- Idempotent authorization requests.
- Decision contains policy/version/reason.
- Approval expiry supported.
- Evidence can only attach to a valid decision/permit.
- Audit events append-only from the API perspective.
- Secret/API-key rotation.
- At least .NET, Python and Node examples.

## Initial SLO targets (product targets, not guarantees)

- Authorization decision p95: < 100 ms from SaaS edge under normal conditions.
- No synchronous LLM call in authorization.
- Authorization availability target: 99.9% for beta design.
- Audit write may be asynchronous only if decision durability is preserved via transactional outbox.

## Success metrics

- Time to first protected action < 15 minutes.
- % authorization checks from custom/non-framework agents.
- approval completion time;
- policy-denied risky actions;
- evidence completion rate;
- SDK adoption;
- weekly active agents;
- authorization checks / organization.
