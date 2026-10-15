---
name: deleguard-add-policy
description: Adds a Deleguard product policy YAML, fixtures, seed wiring, and regression tests. Use when creating or changing authorization policies, invoice rules, or policy fixtures.
---

# Add a Deleguard policy

1. Write YAML under `policies/` using `match.action`, `match.resource_type`, ordered `rules`, and `fallback`.
2. Add fixtures in `policies/fixtures/` covering ALLOW, DENY, and REQUIRE_APPROVAL when those effects apply.
3. Seed a published `PolicyVersion` (see `DemoSeeder`) or document the SQL/API needed to publish.
4. Add or update unit tests in `tests/Deleguard.UnitTests` that parse the YAML through `PolicyYamlParser` and `NativePolicyEngine`.
5. Do not publish a policy that has no fixtures.
