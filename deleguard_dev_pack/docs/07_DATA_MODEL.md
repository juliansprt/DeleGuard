# 07 - Data Model

## Core entities

### Organization
Tenant/security boundary.

### Environment
`development`, `staging`, `production` or custom.

### Agent
Non-human identity registered by organization/environment.

### Actor
Human/service identity. Prefer external IdP subject references rather than duplicating user directories.

### Delegation
Optional bounded relationship from actor/service to agent.

### ActionDefinition
Normalized action name, resource type, risk and metadata.

### Policy / PolicyVersion
Immutable published policy versions.

### AuthorizationDecision
Canonical request digest + result + policy version(s) + explanation.

### ApprovalRequest / ApprovalEvent
Pending request, approver, decision, expiration, narrowed scope.

### Permit
Short-lived signed authorization artifact.

### ExecutionEvidence
Result of the real action: status, provider reference, relevant hashes/metadata.

### AuditEvent
Append-only domain security event.

### WebhookEndpoint / WebhookMessage / WebhookDeliveryAttempt
Tenant-configured outbound callbacks, fan-out from the outbox, and per-attempt HTTP history.

## PostgreSQL outline

```text
organizations
organization_environments
agents
agent_credentials
actors
actor_delegations
action_definitions
policies
policy_versions
authorization_decisions
approval_requests
approval_events
permits
execution_evidence
audit_events
outbox_events
webhook_endpoints
webhook_endpoint_secrets
webhook_messages
webhook_delivery_attempts
```

## Audit immutability

Application APIs must never expose UPDATE/DELETE for audit events. Add a per-tenant hash chain:

```text
hash_n = H(hash_{n-1} || canonical_event_n)
```

Later add KMS signing/checkpoints or external immutable storage.

## Data minimization

Do not log full prompts/responses by default. Deleguard should store the authorization-relevant envelope, not private reasoning traces.
