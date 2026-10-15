using System.Text.Json;
using System.Text.Json.Nodes;
using Deleguard.Api.Http;
using Deleguard.Application;

namespace Deleguard.Api.Mcp;

public static class RemoteGuardMcpEndpoint
{
    public static IEndpointRouteBuilder MapRemoteGuardMcp(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mcp/guard", (HttpContext h) => Results.Json(new { name = "deleguard-guard", transport = "streamable-http", protocols = new[] { "2025-11-25", "2026-07-28" }, authentication = "dgk_ service credential" }));
        app.MapGet("/.well-known/oauth-protected-resource/mcp/guard", (HttpContext h) => Results.Json(new
        {
            resource = $"{h.Request.Scheme}://{h.Request.Host}/mcp/guard",
            bearer_methods_supported = new[] { "header" },
            scopes_supported = new[] { "mcp:connect", "mcp:tools:read", "mcp:tools:call" }
        }));
        app.MapPost("/mcp/guard", HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(HttpContext http, RemoteGuardMcpDispatcher dispatcher, CancellationToken ct)
    {
        var agent = http.GetAgentPrincipal() ?? throw new AgentAuthenticationException("Guard agent credential required.");
        using var document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        if (document.RootElement.ValueKind == JsonValueKind.Array) return Results.BadRequest(new { error = "json_rpc_batch_not_supported" });
        var request = document.RootElement;
        var method = request.TryGetProperty("method", out var m) ? m.GetString() : null;
        var hasId = request.TryGetProperty("id", out var id);
        if (method is null) return Results.BadRequest(new { error = "invalid_request" });
        object? result = method switch
        {
            "notifications/initialized" => null,
            "initialize" => new { protocolVersion = "2025-11-25", capabilities = new { tools = new { listChanged = false } }, serverInfo = new { name = "deleguard-guard", version = "2.0.0" } },
            "ping" => new { },
            "tools/list" => new { tools = (await dispatcher.DiscoverAsync(agent, ct)).Select(x => new { name = x.Name, description = x.Description, inputSchema = x.InputSchema, annotations = new { readOnlyHint = false, destructiveHint = true, idempotentHint = false, openWorldHint = false } }) },
            "tools/call" => await CallAsync(request, agent, dispatcher, ct),
            _ => throw new RequestValidationException("Unsupported MCP method.")
        };
        return hasId ? Results.Json(new { jsonrpc = "2.0", id = CloneId(id), result }) : Results.Accepted();
    }

    private static async Task<object> CallAsync(JsonElement request, Application.Abstractions.AgentPrincipal agent, RemoteGuardMcpDispatcher dispatcher, CancellationToken ct)
    {
        if (!request.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object) throw new RequestValidationException("tools/call parameters are required.");
        var name = parameters.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var args = parameters.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object ? a.Clone() : JsonSerializer.SerializeToElement(new { });
        var operationId = parameters.TryGetProperty("_meta", out var meta) && meta.TryGetProperty("deleguard_operation_id", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString() : null;
        var call = await dispatcher.CallAsync(agent, name, args, ct, operationId);
        return new { content = new[] { new { type = "text", text = call.Payload.GetRawText() } }, structuredContent = JsonNode.Parse(call.Payload.GetRawText()), isError = call.IsError };
    }

    private static object? CloneId(JsonElement id) => id.ValueKind == JsonValueKind.String ? id.GetString() : id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var n) ? n : null;
}
