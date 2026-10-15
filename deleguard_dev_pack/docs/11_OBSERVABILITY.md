# 11 - Observability

## Goal

Answer: **who/what tried to do what, why was it allowed/denied, who approved it, and what actually happened?**

## Metrics

- authorization checks/sec;
- decision latency;
- allow/deny/approval ratios;
- approval latency;
- expired approvals;
- evidence completion rate;
- fail-closed events;
- actions by risk;
- actions by agent/team/environment;
- policy hit counts;
- policy simulation failures.

## Trace model

Propagate W3C `traceparent`. Add attributes:

```text
deleguard.org_id
deleguard.agent_id
deleguard.action
deleguard.resource_type
deleguard.risk
deleguard.decision
deleguard.policy_version
deleguard.decision_id
```

Avoid adding sensitive resource contents to telemetry.

## Dashboard MVP

- active agents;
- checks today;
- high/critical actions;
- pending approvals;
- denials;
- policy violations;
- recent action timeline.
