# 13 - Competitive Positioning

Research date: 2026-09-12.

## Market validation

The surrounding category is clearly validated:

- Palo Alto Networks completed its acquisition of Portkey in May 2026 and positions an AI Gateway as a control plane for agent traffic.
- Kong AI Gateway 2.0 became GA on 2026-09-01 with MCP, identity-aware AI policies and agent-related capabilities.
- Zenity announced a $125M round in August 2026 focused on autonomous-agent security/governance.
- Noma announced a $100M Series B in 2025 and reported >1,300% ARR growth in its announcement.

See `../sources/COMPETITOR_SOURCES.md` for official links.

## Competitive map

| Segment | Examples | Do not compete head-on with |
|---|---|---|
| AI gateway/model proxy | Portkey/Palo Alto, Kong, Cloudflare | routing/caching/model analytics as core |
| Agent security platform | Zenity, Noma, Lakera/Check Point | broad CISO suite initially |
| Workload identity | Aembit | credential issuing/IAM replacement |
| General authorization | Cerbos, Permit | generic enterprise app authZ platform |
| Policy gateway | Control Core | broad AI gateway + Rego + governance suite |

## Proposed wedge

**Universal Agent Action Authorization + Approval + Execution Evidence**.

The product should win by making this workflow exceptionally simple for **custom agents**:

```text
POST /authorize
 -> ALLOW | DENY | REQUIRE_APPROVAL
 -> optional execution evidence
```

## Differentiation hypotheses to test

1. Custom agents/raw HTTP are first-class, not second-class adapters.
2. Resource-level business semantics, not just "can call tool X".
3. Human approval is first-class in the same decision model.
4. Execution evidence/receipts are first-class, not merely logs.
5. Excellent .NET support from day one plus Python/Node.
6. Deployable enforcement point close to internal tools in enterprise phase.

## Hard competitive truth

Control Core and Cerbos already overlap materially with the concept. The MVP must therefore prove that the combination of **developer onboarding + approval UX + action/resource semantics + evidence** solves a workflow better than assembling existing products.

## Kill criterion

Before large investment, run 10-15 interviews with teams that already have agents taking actions. If they are satisfied by existing Cerbos/Permit/Control Core/Kong integrations and do not value execution evidence/approval workflow enough to switch/pay, narrow or stop.
