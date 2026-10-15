# Deleguard - Development Starter Pack

> Working name. Product concept: **universal authorization, human approval and execution evidence for AI-agent actions**.

## Product in one sentence

Deleguard sits immediately before a sensitive agent action and returns one deterministic decision: **ALLOW**, **DENY**, or **REQUIRE_APPROVAL**. It works with custom agents, raw HTTP, direct LLM API calls, MCP, A2A, agent SDKs and orchestration frameworks.

```text
User / System
     |
     v
AI Agent (any model/framework)
     |
     | proposed action
     v
Deleguard Authorization API
     |
     +--> Policy Engine --------> ALLOW
     |                         -> DENY
     |                         -> REQUIRE_APPROVAL
     |
     v
Existing customer code/tool/API
     |
     v
Execution Evidence -> Deleguard Audit Ledger
```

## What this pack contains

- `docs/` - product, architecture, security, integrations, roadmap and testing documentation.
- `spec/` - OpenAPI starter spec plus JSON schemas.
- `examples/` - minimal .NET, Python and Node integrations.
- `adrs/` - initial architecture decision records.
- `research/` - verified research corpus, paper map and implementation implications.
- `papers/` - destination for the original public paper PDFs.
- `scripts/` - paper downloader, PDF verifier and full-pack builder.
- `sources/` - competitor/source links used while preparing the package.

## Start here

Read `START_HERE.md` first if you are beginning implementation immediately.

## Recommended reading order

1. `docs/01_PRODUCT_VISION.md`
2. `docs/02_PRD_MVP.md`
3. `docs/03_ARCHITECTURE.md`
4. `docs/04_AUTHORIZATION_MODEL.md`
5. `spec/openapi.yaml`
6. `docs/08_SECURITY_THREAT_MODEL.md`
7. `docs/13_COMPETITIVE_POSITIONING.md`
8. `docs/14_ROADMAP_AND_BACKLOG.md`
9. `research/RESEARCH_TO_PRODUCT_MAP.md`

## Fastest MVP

Do **not** build an LLM gateway, memory system, agent framework or MCP platform first.

Build these four surfaces:

1. **Agents / actions registry**
2. `POST /v1/authorize`
3. **Policy + approval engine**
4. **Audit/evidence ledger**

The model is never part of the authorization decision path.

## Papers

The research manifest contains verified arXiv URLs. The current execution environment could verify the papers but could not materialize the binary arXiv PDFs into this ZIP. On Windows run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Download-Papers.ps1
```

Then validate them:

```powershell
python .\scripts\verify_pdfs.py
```

To create a new ZIP containing the downloaded PDFs:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Full-Pack.ps1
```

The scripts only download public paper PDFs from URLs listed in `research/papers_manifest.json`.
