# Deleguard Guard MCP (legacy local adapter)

> New deployments must use the managed remote Guard endpoint at `/mcp/guard`. It accepts only Streamable HTTP and forwards only to configured remote HTTPS MCPs. This project remains temporarily for backward-compatible local integrations; it is not a supported remote deployment path and must not be exposed to a network.

Client-side policy enforcement point. Cursor talks only to this MCP. The Guard calls `POST /v1/authorize` and forwards `tools/call` to your downstream MCP **only** on `ALLOW`.

## Run

```bash
dotnet run --project src/Deleguard.McpGuard
```

stdio is the default (Cursor / Claude Desktop). Streamable HTTP sidecar:

```bash
dotnet run --project src/Deleguard.McpGuard -- --http
```

## Cursor

Replace downstream MCP servers with the Guard. Do not register both.

```json
{
  "mcpServers": {
    "deleguard-guard": {
      "command": "dotnet",
      "args": ["run", "--project", "src/Deleguard.McpGuard"],
      "env": {
        "Deleguard__Url": "http://127.0.0.1:5080",
        "Deleguard__ApiKey": "dgk_YOUR_AGENT_KEY",
        "Deleguard__AgentId": "invoice-agent"
      }
    }
  }
}
```

Use a `dgk_` agent key. Administration MCP tokens (`dgm_`) cannot authorize. The Guard has no approve/deny tool.

## Config

- `Deleguard__Url`, `Deleguard__ApiKey` (`dgk_`), `Deleguard__AgentId`
- `--http` also requires `Deleguard__HttpToken`; send `Authorization: Bearer` to `/mcp`
- `DownstreamMcps` in `appsettings.json`: `Name`, `Transport` (`stdio` or `http`), `Command`/`Arguments` or `Url`
- Bindings live in Deleguard (`GET /v1/guard/config`). Unmapped tools are not listed and are denied.
- After `REQUIRE_APPROVAL`, poll `get_decision`. If the Guard process restarted, call the original tool again (authorize is idempotent). Forwarding still requires a live permit.
