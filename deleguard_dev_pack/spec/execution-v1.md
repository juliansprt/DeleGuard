# Execution contract, beta 1

Authorization outcomes remain ALLOW, DENY and REQUIRE_APPROVAL. Authorization alone does not release a protected operation.

`POST /v1/authorize` adds `operation: {id, hash}`. The ID identifies one logical invocation, not its content. Persist it with the job; a retry keeps it. A legitimate new invocation gets a new ID even with identical arguments. The hash is lowercase SHA-256 of canonical exact arguments, computed by the trusted integration. The operation ID is at most 128 characters; SDKs use UUIDs. Keep Idempotency-Key within 8–128 characters.

The result adds `permit_id`, `operation`, `resource_snapshot_hash` and, when querying a consumed decision, `execution`. GET the decision after approval; cached initial authorization responses may still describe the original outcome. Refresh external resource facts immediately before consumption. The central service cannot independently observe customer resources.

`POST /v1/permits/{permitId}/consume` requires:

```json
{"operation_id":"job-uuid","operation_hash":"64 lowercase hex characters","resource_snapshot_hash":"64 lowercase hex characters"}
```

It returns `{acquired, already_consumed, execution_id}`. Only `acquired:true` permits one call. A timeout, invalid response, error or `acquired:false` must never release execution. Consumption is committed before the HTTP response. Lost responses leave an UNKNOWN operation; inspect it instead of retrying the effect. The IDE consume endpoint adapts its existing decision_id/operation_id/snapshot_hash to this contract and adds execution_id to its result.

`POST /v1/executions` requires decision_id, execution_id, matching resource_snapshot_hash and status SUCCESS, FAILED, PARTIAL or UNKNOWN. Permit_id, provider_reference, result_hash and metadata are supported. Repeating the same report is idempotent. UNKNOWN can be enriched with a late terminal result, including after expiry/revocation; contradictory terminal reports return 409. No report reopens execution. Verification is REPORTED or UNKNOWN; clients cannot mark themselves externally VERIFIED.

`POST /v1/evaluations` accepts the authorization envelope and returns evaluation metadata only. It issues no permit, calls no provider and creates no decision. `GET /v1/incidents` and `POST /v1/incidents/{executionId}` use the selected product application and console permissions. `POST /v1/application/suspension` stops new consumption, not already dispatched provider work.

401: wrong credential. 403: suspended or insufficient access. 404: no resource in the authenticated scope. 409: changed binding, expiry, replay or conflicting result. 503: authorization unavailable. Treat all errors as no permission to execute.

Migration preserves historical decisions. Rows without operation binding cannot be consumed; create a new authorization. Old IDE tombstones remain authoritative during revision transitions. Do not roll the application back to a revision that executes unconsumed permits while accepting v1 client traffic.
