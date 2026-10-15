# 06 - Policy Model

## Product-facing policy DSL

Do not expose Rego as the only end-user experience. Define a stable Deleguard policy model and compile/translate to the underlying deterministic engine.

Example:

```yaml
id: invoice-cancel-production
version: 4
priority: 100
match:
  action: invoice.cancel
  resource_type: invoice
conditions:
  all:
    - field: context.environment
      op: eq
      value: production
rules:
  - when:
      field: resource.attributes.reportedToTaxAuthority
      op: eq
      value: true
    effect: DENY
    reason: Reported invoices cannot be autonomously cancelled

  - when:
      field: resource.attributes.amount
      op: gt
      value: 10000000
    effect: REQUIRE_APPROVAL
    approval:
      expires_in: 15m
      approver_roles: [billing-manager]

  - effect: ALLOW
```

## Requirements

- deterministic;
- versioned;
- testable before publish;
- diffable;
- explainable;
- no arbitrary executable customer code in SaaS PDP;
- deny-by-default support;
- reusable policy sets by environment/team.

## Policy simulator

Every policy publish flow should support fixtures:

```text
Given request X -> expected DENY
Given request Y -> expected REQUIRE_APPROVAL
Given request Z -> expected ALLOW
```

Publishing should fail if mandatory regression tests fail.

## Future AI-assisted policy authoring

An LLM may generate a *candidate* policy from natural language, but it must never directly become active. Required flow:

```text
Natural language
 -> LLM draft
 -> deterministic parse
 -> static checks
 -> policy tests
 -> human review
 -> versioned publish
```
