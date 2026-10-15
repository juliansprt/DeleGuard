# Pilot policy packs / Políticas para pilotos

These are versioned reference policies, not published tenant configuration. The GitHub pack has executable native-engine fixtures; publishing and an HTTP execution check remain per-tenant onboarding steps. Existing `invoice.cancel` rules remain the business-action demonstration.

Son políticas de referencia versionadas, no configuración publicada. El paquete GitHub incluye casos ejecutables contra el motor nativo. Cada cliente debe adaptar los repositorios, probar el flujo HTTP y publicar una versión comprobada durante su incorporación.

## GitHub issue creation contract

| Field | Contract |
| --- | --- |
| Action name | `github.issue.create` |
| Resource type | `repository` |
| Registered risk | `HIGH` |
| `resource.id` | Exact lowercase `owner/repository`, supplied by the trusted integration |
| Sandbox destination | `deleguard-pilot/sandbox` (example placeholder) |
| Approval destination | `deleguard-pilot/pilot-issues` (example placeholder) |
| `agent.environment` | `sandbox` or `pilot`, registered on the agent; must match the destination classification |
| `resource.attributes.providerHost` | String `api.github.com`, taken from the configured execution destination |
| `resource.attributes.repositoryEnvironment` | String `sandbox` or `pilot`, from the integration's reviewed repository configuration |
| `resource.attributes.visibility` | String `private`, from repository metadata |
| `resource.attributes.archived` | Boolean `false`, from repository metadata |
| `resource.attributes.issuesEnabled` | Boolean `true`, from repository metadata |

The reference file is [`policies/github-issue.yaml`](../../policies/github-issue.yaml). Replace both repository identifiers and update the [fixtures](../../policies/fixtures/github-issue.json) together. The identifiers are examples; this pack does not create a GitHub repository or grant access to one. Keep `fallback: DENY` and the explicit DENY rules. A separate allow policy cannot override a matching DENY.

La ejecución automática se limita al repositorio privado de sandbox indicado. Crear un issue en el repositorio de piloto requiere aprobación de `org-admin` durante 15 minutos. Producción, destinos desconocidos y datos ausentes se bloquean. No ampliar la lista de repositorios ni cambiar los roles sin actualizar y ejecutar los casos.

The trusted integration validates JSON types and resolves repository metadata before authorization. Never accept classification, visibility, host or lifecycle flags from model output. The DSL performs value comparisons, not complete request-schema validation. The policy does not validate issue text or independently fetch repository facts.

Bind the complete outbound issue request (repository, title, body, labels and any other submitted fields) to the operation/permit binding used by the execution client. The metadata conditions above are not a substitute for that binding. Reuse operation identity only for retries of the same intended operation. Recheck repository identity and relevant facts before executing an approved write. An uncertain provider result must be investigated before another write.

## Draft registration, testing and publication

Use an authorized console session for administration endpoints, not an agent's `dgk_` credential. All identifiers below are tenant-specific; select one product application and its agents.

1. Register the `sandbox` and `pilot` environments in that application and the corresponding agents. Assign an active `org-admin` approver for the pilot. Use a private test repository for each destination and a credential limited to the required repositories.
2. Create the action with `POST /v1/actions`:

   ```json
   {
     "application_id": "<product-application-id>",
     "name": "github.issue.create",
     "resource_type": "repository",
     "risk": "HIGH",
     "agent_ids": ["<sandbox-agent-id>", "<pilot-agent-id>"]
   }
   ```

3. Create a draft with `POST /v1/policies`, supplying `application_id`, `agent_ids` and `yaml` containing the complete adapted YAML string. Record the returned `policy_id` and `version_id`. Draft creation does not activate a policy. To revise an existing draft use `POST /v1/policies/{policyId}/versions` with a new YAML version number.
4. Execute the policy tests locally:

   ```powershell
   dotnet test tests/Deleguard.UnitTests/Deleguard.UnitTests.csproj --filter FullyQualifiedName~GitHubIssuePolicyTests
   ```

   To isolate build artifacts when an existing build is locked, add `--configuration PolicyPackValidation -p:UseAppHost=false`.

5. Test the tenant draft through `POST /v1/policies/{policyId}/test`. For each fixture send `{ "expected_effect": "<fixture.expected>", "request": <fixture.request> }`, replacing example identifiers with the exact configured destinations. The fixture's `expected_rule` is a local regression assertion, not an API request field. An optional `yaml` selects a candidate version explicitly; otherwise the endpoint tests the latest version.
6. In the policy detail simulator, save regression cases with name, expected decision, agent, environment, resource ID and attribute JSON, then compare the candidate. Save at least the sandbox ALLOW, pilot REQUIRE_APPROVAL, production DENY, unknown-repository DENY and missing-classification DENY cases. Add the remaining applicable metadata cases. Unknown-action/resource-type and context-tampering fixtures are also covered by the native tests and `/test`; the console's saved-case form uses the policy's own action/resource type. A call to `/test` alone does **not** save a publication regression case.
7. Only after saved cases pass, publish the tested version from the console or `POST /v1/policies/{policyId}/publish` with `{ "version_id": "<tested-version-id>" }`. Publish first in an isolated validation application and perform the HTTP acceptance below before repeating onboarding for a customer. The operation may itself require an administrative approval. Treat the version as published only after the response confirms `published: true`; re-read the policy and verify `published_version_id`. The server requires saved passing cases for product-policy publication.

Pasos de incorporación: registrar entornos y agentes, crear acción y borrador, adaptar los dos repositorios, ejecutar los casos, guardar regresiones en el simulador y publicar únicamente la versión comprobada. Ejecutar `/test` no equivale a guardar un caso ni a publicar la política.

## Fixture decisions and regression guarantees

| Fixture | Expected decision |
| --- | --- |
| `private-sandbox-allow` | `ALLOW` |
| `private-pilot-approval` | `REQUIRE_APPROVAL` — 15 minutes, `org-admin` |
| `production-agent-deny` | `DENY` |
| `production-repository-deny` | `DENY` |
| `unknown-repository-deny` | `DENY` |
| `lookalike-repository-deny` | `DENY` |
| `foreign-host-deny` | `DENY` |
| `public-repository-deny` | `DENY` |
| `archived-pilot-deny` | `DENY` |
| `disabled-issues-deny` | `DENY` |
| `missing-classification-deny` | `DENY` |
| `environment-mismatch-deny` | `DENY` |
| `unknown-environment-deny` | `DENY` |
| `unknown-action-deny` | `DENY` |
| `wrong-resource-type-deny` | `DENY` |
| `context-cannot-override-production-deny` | `DENY` |

[`GitHubIssuePolicyTests`](../../tests/Deleguard.UnitTests/GitHubIssuePolicyTests.cs) parses the shipped YAML with `PolicyYamlParser` and evaluates every fixture with `NativePolicyEngine`. Additional cases remove or null each required attribute for both supported destinations; all must DENY. Precedence checks put a broad ALLOW before the explicit DENY within a policy and in a higher-priority competing policy. Explicit DENY and this pack's fallback DENY must still win.

## HTTP acceptance before connecting a customer

The policy test suite proves decisions, not provider execution or tenant isolation. The integration acceptance run must demonstrate:

- The authenticated sandbox agent receives a persisted ALLOW decision and a permit for the exact repository snapshot/operation. A foreign agent/application cannot use that permit.
- The pilot request remains pending until an eligible approver approves it; rejecting or expiring it never enables execution. Changed title/body/destination invalidates the approved operation binding.
- A permitted operation is consumed once before the provider call. Concurrent attempts and retries do not create duplicate issues.
- Evidence is rejected without a valid matching consumed permit. Reported success includes the issue's provider ID and URL; independent read-back supplies separately verified evidence only when the verifier is authorized.
- Provider timeouts are recorded as unknown. Reconciliation queries the intended operation's external reference before any repeat creation.
- The production and unknown-repository cases perform no GitHub write.

Do not mark a tenant integration as verified or this pack as published merely because its native policy tests pass. No live GitHub write or policy publication is performed by this document or its fixture suite.

Antes de conectar un cliente, comprobar por HTTP la decisión persistida, aprobación, consumo único, evidencia y aislamiento. Un timeout conserva estado desconocido. La verificación por consulta posterior del issue debe distinguirse del éxito reportado por el cliente.
