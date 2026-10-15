# IDE policy library (draft examples)

These opt-in templates cover filesystem access plus `process.execute`, `mcp.call`, `mcp.discover`, `network.connect`, `git.clone`, `git.fetch`, `git.commit`, `git.push`, `package.install`, `service.manage`, `credential.use`, `ide.settings.change`, `extension.install`, `debug.attach`, and `browser.navigate`. AI Gateway is not involved. They are not automatically published or assigned to agents.

The new non-filesystem templates are intentionally explicit DENY rules with a `DENY` fallback. They are guardrails, not approval templates: an administrator must replace them with a narrower, reviewed policy that constrains server/host, command, registry, branch, credential, extension, or IDE setting as appropriate. A temporary grant must never turn these default-denied categories into an allow.

## Decisions

| Fixture scenario | Expected decision |
| --- | --- |
| `ide-demo` project, canonical `src` or `src/*` file operation | ALLOW |
| `ide-demo`, `infra/production` or any descendant (including read/list/search) | REQUIRE_APPROVAL |
| `.deleguard` or any descendant | DENY |
| Missing/unknown project, missing path, unknown action, other directory | DENY |
| Lookalike `src0`, `infra/production-old`, case mismatch | DENY |
| Traversal, backslash, repeated separators or dot segments | DENY |
| Move inside src | ALLOW |
| Move src to/from production | REQUIRE_APPROVAL |
| Move with forbidden/unknown/missing destination | DENY |
| Move production to forbidden directory | DENY (wins over approval) |
| Process or MCP operation in `ide-demo` | REQUIRE_APPROVAL |
| Process or MCP operation with unknown/missing project | DENY |
| Network, Git, package, service, credential, MCP discovery, IDE configuration, extension, debugger, or browser operation | DENY |

Every fixture includes its own explicit `expected` decision in `fixtures/ide-*.json`. The `IdePolicyTests` theory reads every fixture through the real YAML parser and native engine. Approvals expire after 15 minutes and require `org-admin`. This is approval of a single operation; a temporary access grant must be evaluated separately by the server and can never override DENY.

## Trust boundary

These are restrictive **examples**, not a sandbox or an authorization scheme for arbitrary projects. Before deployment replace `ide-demo` with the registered project ID in each YAML and matching fixtures. Application/organization isolation, agent assignment, registered project identity and the canonical path must be enforced by the authorization integration. A string supplied by an arbitrary client does not prove project membership or the actual file accessed.

The adapter resolves physical paths, links and junctions, ensures confinement to its configured root, and supplies canonical project-relative paths with `/` separators and no empty, `.` or `..` segments. Move authorization validates both source and destination, including nonexistent destination parents. The server additionally checks configured protected/denied directories at segment boundaries, using the project's case semantics. These YAML templates use ordinal comparisons; uppercase variants default to DENY and must not be silently lowercased on a case-sensitive filesystem.

The current DSL has no prefix operator. A subtree is represented by equality to the directory or the ordinal interval `path >= 'src/'` and `path < 'src0'`. This matches a segment boundary, unlike substring matching. Tests cover exact directories, descendants and prefix lookalikes. Rejecting any `..` substring is intentionally restrictive and also blocks harmless filenames containing consecutive dots. The integration's canonical path validation remains necessary; YAML alone is not a filesystem parser.

The templates do **not** trust `resource.attributes.protected` supplied by the client: production requires approval even when this boolean is false. Custom project protected/denied directories must be enforced from administrator-controlled configuration by the server before any ALLOW. A server-derived protected classification can strengthen a decision; untrusted booleans must never weaken it.

Hooks only control intercepted operations. Shell approval does not establish which files a command will touch; MCP approval does not establish a tool's internal effects. Local processes can bypass hooks unless separately isolated. No prompts, contents or command output are required by these policy fixtures.

## Install and publish

1. Register a product application, project and agent. Register each desired action with its resource type above; assign only the actions needed.
2. Import the corresponding YAML into the existing policy editor. Set the actual project ID and desired directory rules. Assign the policy to the same agent.
3. Copy the JSON fixture cases into the policy's saved simulation library after substituting the project ID. Compare with the current published version and run all cases. Simulation must not authorize or execute anything.
4. Publish using the existing policy publishing flow only after all cases pass; there is no automatic publish in this library. Repeat cases for every changed path/project configuration.
5. Test ALLOW, DENY and REQUIRE_APPROVAL through the actual IDE adapter and verify reported execution evidence. The console must describe this as client-reported evidence and show uncovered capabilities.

Run library regression tests:

```powershell
dotnet test tests/Deleguard.UnitTests/Deleguard.UnitTests.csproj --filter FullyQualifiedName~IdePolicyTests
```
