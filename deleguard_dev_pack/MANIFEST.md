# Deleguard Development Starter Pack - Manifest

## Product documentation

- `docs/01_PRODUCT_VISION.md` - product thesis, buyer, positioning and non-goals.
- `docs/02_PRD_MVP.md` - MVP requirements and acceptance criteria.
- `docs/03_ARCHITECTURE.md` - proposed runtime and service architecture.
- `docs/04_AUTHORIZATION_MODEL.md` - subject/action/resource/context model.
- `docs/05_API_DESIGN.md` - API conventions, idempotency and lifecycle.
- `docs/06_POLICY_MODEL.md` - policy semantics and example rules.
- `docs/07_DATA_MODEL.md` - initial relational/data entities.
- `docs/08_SECURITY_THREAT_MODEL.md` - primary threats and mitigations.
- `docs/09_INTEGRATION_GUIDE.md` - custom-agent integration patterns.
- `docs/10_APPROVALS_AND_EVIDENCE.md` - human approvals and proof-of-execution.
- `docs/11_OBSERVABILITY.md` - traces, metrics, logs and audit principles.
- `docs/12_TESTING_AND_EVALS.md` - deterministic and adversarial test plan.
- `docs/13_COMPETITIVE_POSITIONING.md` - product wedge and market boundaries.
- `docs/14_ROADMAP_AND_BACKLOG.md` - staged roadmap and implementation backlog.
- `docs/15_DEPLOYMENT_AND_OPERATIONS.md` - deployment/operational guidance.
- `docs/16_OPEN_QUESTIONS.md` - decisions to resolve during discovery.
- `docs/17_WEBHOOKS.md` - outbound Standard Webhooks, retries, and SSRF rules.

## Architecture decisions

- `adrs/ADR-001-no-llm-in-auth-path.md`
- `adrs/ADR-002-custom-agent-first.md`
- `adrs/ADR-003-policy-abstraction.md`
- `adrs/ADR-004-append-only-audit.md`
- `adrs/ADR-005-modular-monolith-first.md`
- `adrs/ADR-006-permit-resource-binding.md`

## Machine-readable specifications

- `spec/openapi.yaml`
- `spec/authorization-request.schema.json`
- `spec/evidence-receipt.schema.json`
- `spec/policy-example.yaml`

## Integration examples

- `.NET`: `examples/dotnet/Example.cs`
- Python: `examples/python/example.py`
- Node.js: `examples/node/example.mjs`
- Raw request: `examples/authorize-request.json`

## Research

- `research/PAPER_INDEX.md` - categorized research index.
- `research/RESEARCH_TO_PRODUCT_MAP.md` - how findings map to product requirements.
- `research/papers_manifest.json` / `.csv` - verified source manifest.
- `sources/COMPETITOR_SOURCES.md` - competitor/source reading list.

## Paper tooling

- `scripts/Download-Papers.ps1`
- `scripts/download_papers.py`
- `scripts/verify_pdfs.py`
- `scripts/Build-Full-Pack.ps1`
- `PAPERS_STATUS.md`
