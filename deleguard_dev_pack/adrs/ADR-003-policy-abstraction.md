# ADR-003 - Product policy abstraction over policy engine

Status: Proposed.

Expose a Deleguard policy schema and compile/translate to a deterministic engine. Do not expose engine-specific Rego/Cedar syntax as the only product contract. This preserves future engine portability and improves UX.
