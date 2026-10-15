# 12 - Testing and Evals

## Unit / deterministic tests

- policy operators;
- precedence;
- missing attributes;
- default deny;
- permit expiry;
- idempotency;
- audit hash chain;
- delegation intersection.

## Policy regression tests

Treat policies like code. Every policy version carries fixtures and expected decisions.

## Adversarial tests

- tool result contains instruction to escalate;
- prompt claims admin approval that never happened;
- memory contains old permission;
- agent changes actor id;
- resource amount changes after approval;
- duplicate/replayed execution;
- policy service unavailable;
- malicious skill/tool name;
- Unicode/confusable action names;
- large resource attributes / schema abuse.

## E2E reference scenarios

1. Invoice draft cancel -> ALLOW.
2. Issued high-value invoice -> REQUIRE_APPROVAL.
3. Tax-reported invoice -> DENY.
4. SQL SELECT -> ALLOW.
5. SQL DELETE production -> REQUIRE_APPROVAL/DENY.
6. Payment over threshold -> approval + evidence.

## Research-inspired evaluation

- Counterexamples as Feedback: return concrete policy test failures, not generic errors.
- SAGE: prefer state-grounded deterministic verification.
- SciCode-Verified: audit the evaluator itself; broken evals can create false confidence.
