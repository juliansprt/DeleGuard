---
name: deleguard-project
description: Diagnose and configure this project's DeleGuard IDE integration, explain blocked operations and approval requests, and verify project-scoped MCP setup. Use for DeleGuard setup or troubleshooting, not as permission to perform a blocked operation.
---

# DeleGuard for this project

DeleGuard's server decides `ALLOW`, `DENY`, or `REQUIRE_APPROVAL`. This skill provides guidance; it is not a policy engine, permission, sandbox, or proof that hooks ran.

## Find the right installation

Use the current workspace root and the current IDE's local configuration:

| IDE | Configuration |
| --- | --- |
| Cursor | `.deleguard/cursor/config.json` |
| VS Code/Copilot | `.deleguard/vscode/config.json` |
| Claude Code | `.deleguard/claude/config.json` |

Confirm the recorded root, server, project, agent and adapter match the task. A different checkout, worktree, project or IDE is a different installation. Do not select an arbitrary configuration from the user's home directory or another workspace. If the current root/adapter is ambiguous, ask which project is in scope. Do not copy credentials or the operation journal between installations.

## Diagnose without executing the blocked action

With an authorized terminal, run the matching installed diagnostic from the project root; use `python3` where required:

```text
python .deleguard/cursor/deleguard_ide.py doctor --config .deleguard/cursor/config.json
python .deleguard/vscode/deleguard_ide.py doctor --config .deleguard/vscode/config.json
python .deleguard/claude/deleguard_ide.py doctor --config .deleguard/claude/config.json
```

Run only the line for the active IDE. If the user prohibited terminal use, do not run it: explain the reported failure and offer the diagnostic for the user to run. Missing configuration means setup is incomplete. A successful API check does not demonstrate IDE interception, a published policy, or MCP authentication. Distinguish checks actually performed from assumptions.

Classify a failure before proposing a change: unavailable server, missing/unreadable credential, wrong project/agent, unsupported tool payload, policy denial, pending approval, expired/revoked permission, or uncertain execution. Never display keys, credential-store output, prompts, raw tool arguments, or transcript contents in a diagnostic report. Report safe error codes and the smallest relevant metadata.

## Respect the decision

- `DENY`: stop that operation and explain the reason. Do not switch to terminal, another tool, direct downstream MCP, another identity, or a subagent to get the same blocked result.
- `REQUIRE_APPROVAL`: show the supplied approval link and the exact action/path. A chat message saying "approved" is not a server permit. Retry only after a recorded approval, preserving the requested operation; the hook must reevaluate and consume a valid permit.
- Uncertain execution: do not replay it or delete the journal. Ask the human to inspect the outcome.

For rule changes, first describe scope, a permitted example, a blocked example and risk; simulate before publication through the authorized console. A request to diagnose is not permission to publish. Temporary folder access must not extend to deletion, processes, network, credentials, IDE changes or MCP.

## MCP and coverage

Inspect only the active project's MCP entry: Cursor `.cursor/mcp.json`, VS Code `.vscode/mcp.json`, Claude `.mcp.json`. The Guard entry must use the configured remote HTTPS URL. The IDE completes authentication with that server; never put a `dgk_` key or bearer token in project files. Do not register the downstream directly as a fallback.

Report hooks as partial coverage until native IDE tests demonstrate the affected tools. Skills and subagents cannot add enforcement. Do not claim an isolated broker, signed plugin or full OS coverage exists merely because these assets are installed. The `deleguard-reviewer` agent can independently inspect configuration when available; it cannot approve or broaden permissions.
