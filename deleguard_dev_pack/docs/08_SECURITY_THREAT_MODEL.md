# 08 - Security Threat Model

## Security invariant

**Model output is a proposal, never authority.**

## Threats and design controls

| Threat | Example | Control |
|---|---|---|
| Prompt/tool injection | Tool output says "transfer money" | Action authorization independent from tool text |
| Authority laundering | Low-trust observation becomes remembered instruction | Preserve provenance/authority; never infer permission from memory |
| Forged execution memory | Agent claims safety check already happened | Trusted execution evidence ledger |
| Confused deputy | Agent inherits full user access | actor + agent blended/delegated authorization |
| Replay | Re-submit old approval | nonce/jti, expiry, idempotency |
| TOCTOU | Resource changes after approval | resource snapshot/hash + short permit |
| Policy bypass | Agent calls backend directly | integrate PEP at tool/API boundary where possible |
| Stale policy | cached allow survives policy change | version-bound permit + short TTL/revocation strategy |
| Approval phishing | misleading action description | render canonical structured fields, not only agent explanation |
| Evidence forgery | agent invents provider result | provider/webhook verification when available + hash chain |
| Supply-chain attack | malicious skill/hook | Deleguard does not trust skill metadata as permission; capability registry/versioning |
| Fail-open outage | auth service unreachable | fail-closed for high/critical; explicit configurable behavior for low risk |

## High-risk invariant

For `HIGH` and `CRITICAL` actions, authorization should be evaluated as close to the execution boundary as feasible.

## Research basis

Especially relevant papers in `/papers`/manifest:

- When Tool Outputs Become Commands (SARA)
- Memory Provenance Laundering
- When Memory Becomes Authority
- Proof-of-Execution Memory
- Context Privilege Escalation
- Scanning the Harness
- HookPry
- Agent Incident Registry

## No chain-of-thought requirement

The platform should operate using structured, explicit inputs and results. It should not require or persist private chain-of-thought.
