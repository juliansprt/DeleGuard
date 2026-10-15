# 01 - Product Vision

## Working name

**Deleguard** (provisional).

## Category

**Agent Action Authorization** / **Agentic Authorization Infrastructure**.

## Core promise

> AI agents may reason freely, but sensitive actions must be deterministically authorized.

## Problem

Most agent systems eventually reduce to code similar to:

```text
LLM proposes action -> application executes action
```

This creates a trust boundary problem. Prompt injection, tool-output injection, stale memory, authority laundering, accidental delegation and ordinary reasoning errors can cause a valid-looking model output to trigger an invalid real-world action.

Existing products frequently start from one of these angles:

- LLM gateway / model routing;
- AI security / prompt protection;
- MCP-specific authorization;
- workload identity;
- generic application authorization.

Deleguard should enter through a narrower workflow:

```text
WHO is acting?
ON BEHALF OF whom?
WHAT action is requested?
ON WHICH resource?
UNDER WHICH context?

=> ALLOW | DENY | REQUIRE_APPROVAL
```

## Target user

Initial buyer/user:

- platform engineer;
- AI/agent platform team;
- senior developer / architect;
- security engineer supporting agent teams.

Initial ICP:

- organizations already running agents that call real APIs/tools;
- custom or mixed agent stacks;
- workflows with financial, destructive, compliance-sensitive or production actions;
- teams that need approvals and auditability without rewriting their agents.

## Explicit non-goals for MVP

- building agents;
- hosting LLMs;
- becoming a general-purpose LLM proxy;
- prompt playground;
- vector memory;
- replacing IAM/IdP;
- full SIEM;
- autonomous policy creation with an LLM.

## Differentiator

**Custom-agent first.** If the customer's code can make an HTTP request, it can use Deleguard. MCP/A2A/OpenAI/Anthropic integrations are adapters, not prerequisites.

## Product vocabulary

- **Agent**: non-human workload that proposes actions.
- **Actor**: human/service identity on whose behalf the agent is acting.
- **Action**: normalized capability such as `invoice.cancel`.
- **Resource**: domain object affected by the action.
- **Context**: environment/risk/tenant/request attributes.
- **Decision**: allow, deny or require approval.
- **Permit**: short-lived authorization artifact for an allowed action.
- **Approval**: explicit human authorization for a pending decision.
- **Evidence**: proof/result of the action actually executed.
- **Receipt**: immutable record binding request, policy, decision, approval and execution evidence.
