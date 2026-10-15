# ADR-004 - Append-only audit and evidence

Status: Accepted.

Decision, approval and execution events are append-only. Corrections create new events. Events participate in a tenant-specific hash chain; later versions can checkpoint/sign chains with KMS.
