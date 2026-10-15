# Contributing to DeleGuard

Thank you for helping improve DeleGuard. Contributions go through a pull request to `main`, maintainer review, and CI checks. This process also applies to the maintainer's own changes. Keep each pull request focused on one problem so it can be reviewed and validated independently.

For a bug, include the affected component, steps to reproduce, expected behavior, and actual behavior in an [issue](https://github.com/juliansprt/DeleGuard/issues). Discuss large features or changes to public contracts with the maintainer before implementing them. Small fixes and documentation improvements can start directly with a pull request.

## Get ready

Read the [README](README.md#quick-start) for local startup and the [development guide](docs/DEVELOPMENT.md) for configuration, optional components, and troubleshooting. The core requirements are the .NET 10 SDK, Docker with Linux containers and Compose v2, and Git. The full acceptance script also uses PowerShell 7, Python 3, and Node.js; see the development guide for supported versions.

Before changing behavior, review:

- [Project instructions](AGENTS.md) and the [repository map](docs/DEVELOPMENT.md#repository-structure).
- [START_HERE](deleguard_dev_pack/START_HERE.md) and the [product requirements](deleguard_dev_pack/docs/02_PRD_MVP.md).
- The relevant [API specification](deleguard_dev_pack/spec/openapi.yaml), [execution contract](deleguard_dev_pack/spec/execution-v1.md), or other contracts in [`deleguard_dev_pack/spec`](deleguard_dev_pack/spec/).

## Use a branch and open a pull request

1. Fork [juliansprt/DeleGuard](https://github.com/juliansprt/DeleGuard) on GitHub, then clone your fork. Contributors with repository write access can create a branch in this repository instead.

   ```powershell
   git clone https://github.com/YOUR_USERNAME/DeleGuard.git
   cd DeleGuard
   git remote add upstream https://github.com/juliansprt/DeleGuard.git
   git fetch upstream
   git switch -c fix/describe-the-change upstream/main
   ```

2. Make the smallest complete change that addresses the problem. Follow the conventions of the surrounding code, preserve module boundaries, and update affected documentation and contracts.
3. Run the relevant validation below. Add or update meaningful regression tests when changing behavior. Use a draft pull request if work or verification is still in progress.
4. Review your diff, commit, and push your branch:

   ```powershell
   git diff --check
   git diff
   git add path/to/changed-file
   git commit -m "Fix: describe the behavior corrected"
   git push -u origin fix/describe-the-change
   ```

   Replace the example branch, file path, and commit message with your own. Stage only files intended for the contribution.

5. Open a pull request from your branch to **`juliansprt/DeleGuard:main`**. Complete the pull request template, link related issues, explain the resulting behavior, and include validation results. For console changes, add screenshots with demo data.
6. Respond to review comments and push follow-up commits to the same branch. Keep it current with `main` when needed, then rerun checks affected by those changes.

Changes to `main` must follow the pull request process. The maintainer, [@juliansprt](https://github.com/juliansprt), reviews and merges contributions; [CODEOWNERS](.github/CODEOWNERS) assigns ownership across the repository. The required **PR validation** check must pass, and review conversations must be resolved before merge. The current rules do not require a second person's approval. Merge with a squash or rebase to preserve linear history; force pushes and deletion of `main` are blocked. A draft pull request or a previously successful run does not replace validation of the current changes.

## Validate your changes

The required **PR validation** workflow runs for every pull request. It checks whitespace on changed lines, local contribution-document links and heading anchors, the banner SVG, and the existing Python IDE adapter contracts. Run its documentation and adapter checks locally with:

```powershell
python scripts/validate-contribution-docs.py
python -m unittest discover -s integrations/ide -p "test_*.py" -v
```

This required check does not build or test the .NET backend. The existing **v1 acceptance** and **Portable installer validation** workflows run separately and are not required branch-protection checks while the documented baseline issues remain unresolved. Maintainers must inspect their results and the validation relevant to each change before merging.

Run commands from the repository root. Start with the suites that exercise your change:

```powershell
dotnet build Deleguard.slnx
dotnet test tests/Deleguard.UnitTests
dotnet test tests/Deleguard.DemoAgent.Tests
```

For changes to persistence, authorization, credentials, permits, approvals, or evidence, run the integration and security suites. Docker must be running: these suites create disposable PostgreSQL databases through Testcontainers; a running local API is not required.

```powershell
dotnet test tests/Deleguard.IntegrationTests
dotnet test tests/Deleguard.SecurityTests
```

The acceptance script runs the main suites and checks the .NET client, IDE adapter contracts, Python client, and TypeScript client. It requires Docker, PowerShell 7, Python, and Node.js. Leave `DELEGUARD_TEST_POSTGRES` unset so acceptance uses disposable databases.

```powershell
pwsh -File scripts/v1/verify.ps1
```

For a desktop change, also run `dotnet test tests/Deleguard.Desktop.Tests`. For a documentation site change, run `npm ci` followed by `npm run build` from `docs-site`. For Markdown-only changes, check links, images, and rendering; run additional checks if examples or commands changed.

The [initial validation results](docs/DEVELOPMENT.md#initial-validation-results) document existing dependency warnings and a headless desktop test failure. Report the exact command, result, and any environment limitations in your pull request. Do not disable security checks, weaken assertions, or mark a failing check as passing to work around a known issue. A required failing check needs to be resolved before merge.

## Preserve the authorization and execution guarantees

DeleGuard is a policy decision, approval, and evidence platform. Changes must preserve these contracts:

- Decisions remain `ALLOW`, `DENY`, and `REQUIRE_APPROVAL`, computed by the native policy engine. Keep the model outside the authorization decision path.
- Maintain tenant isolation, credential boundaries, policy versioning, and fail-closed behavior. Missing, invalid, expired, or unavailable authorization must never release a protected action.
- Authorization or approval alone does not permit execution. Retrieve the current decision after approval, refresh external resource facts, and consume the permit before the external side effect. Execute only after `acquired: true`.
- Preserve the operation ID, exact-argument hash, resource snapshot binding, and idempotency rules across retries. A lost consumption response is an uncertain outcome, not permission to repeat the side effect.
- Bind execution evidence to its valid decision and execution ID, preserve append-only audit behavior, and do not label a client-reported outcome as independently verified.
- Keep AI prompts and provider keys out of authorization metadata and the SaaS decision path. Provider credentials belong in the gateway or customer integration.

Use the [execution contract](deleguard_dev_pack/spec/execution-v1.md) for the detailed rules. Policy changes need representative allow, deny, and approval fixtures plus counterexamples for relevant boundaries. Changes to authorization, permits, approvals, or evidence need focused security review. If you use coding agents, follow the specialist instructions in [AGENTS.md](AGENTS.md).

## Make changes easy to review

- Explain why the change is needed and what users or integrations will observe. Include before/after examples where useful.
- Keep unrelated cleanup, generated files, and build outputs out of the pull request. Match existing code style without reformatting unrelated files.
- Document dependency additions, database migrations, configuration changes, compatibility impact, and rollout considerations when relevant.
- Use English for contribution documentation and pull request descriptions. Preserve the intended language when updating localized product documentation.
- Never commit real API keys, tokens, cookies, private keys, `.env` files, database dumps, or customer data. Use documented local demo credentials and redact logs and screenshots. Do not publish sensitive vulnerability details in an issue or pull request.
- Preserve third-party attribution and notices. The project's own source does not yet include a license; do not add or change licensing terms as part of an unrelated contribution. See the existing [third-party notices](src/Deleguard.Api/wwwroot/lib/codemirror/THIRD_PARTY_NOTICES.md).
