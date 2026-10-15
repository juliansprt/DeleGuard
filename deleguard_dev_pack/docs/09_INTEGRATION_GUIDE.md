# 09 - Integration Guide

## Integration level A - direct SDK/API

Best MVP integration. Add authorization immediately before action execution.

```text
LLM/agent -> proposed action -> Deleguard authorize -> customer executes tool
```

Works with any framework.

## Integration level B - middleware / interceptor

Wrap common internal tool interfaces so every action goes through a single enforcement abstraction.

```csharp
await protectedExecutor.ExecuteAsync(
    action: "invoice.cancel",
    resource: invoice,
    execute: () => invoiceService.Cancel(invoice.Id));
```

## Integration level C - MCP Guard (PEP)

Run `src/Deleguard.McpGuard` in the customer project. Intercept `tools/call`, map MCP server/tool into normalized action/resource, authorize, and forward only on ALLOW with a live permit.

The host must replace downstream MCP registrations with the Guard. Dual registration is a bypass. Streamable HTTP (`--http`) requires `Deleguard__HttpToken`.

Do not make MCP mandatory. The Guard is a client PEP, not a hosted MCP platform.

## Integration level D - proxy/PEP

For APIs that can be routed through a gateway/sidecar, enforce without agent-code changes. This is later enterprise scope.

## Custom agents

The minimum requirement is the ability to call HTTP. A raw script with direct OpenAI/Anthropic requests is a valid Deleguard client.

## Identity mapping

At minimum:

- API credential identifies organization + agent;
- request can include actor subject from customer's trusted auth layer;
- server verifies actor/delegation attributes where integrations exist.

Never trust an arbitrary `actor.id` supplied by an untrusted agent unless cryptographically bound to the request/session.
