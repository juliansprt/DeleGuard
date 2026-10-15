using System.Text.Json;
using System.Text.Json.Nodes;
using Deleguard.Application;
using Deleguard.Api.Http;
using Deleguard.Application.Identity;

namespace Deleguard.Api.Mcp;

public static class AdminMcpEndpoint
{
    public const string Protocol = "2025-03-26";
    public const string ServerVersion = "1.2.0";

    private static readonly string[] SupportedProtocols = ["2025-03-26", "2025-11-25"];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static IEndpointRouteBuilder MapAdminMcp(this IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp", HandlePostAsync);
        app.MapGet("/mcp", (HttpContext http) =>
        {
            // Discovery only. Streamable HTTP clients POST JSON-RPC. SSE GET is not offered.
            return Results.Json(new
            {
                name = "deleguard-admin",
                transport = "streamable-http",
                protocol = Protocol,
                authentication = "bearer dgm_ token"
            });
        });
        app.MapGet("/.well-known/oauth-protected-resource", (HttpContext http) =>
        {
            var resource = $"{http.Request.Scheme}://{http.Request.Host}/mcp";
            return Results.Json(new
            {
                resource,
                bearer_methods_supported = new[] { "header" },
                resource_documentation = "Use an Administration MCP personal token (prefix dgm_). OAuth is not enabled."
            });
        });
        return app;
    }

    private static async Task<IResult> HandlePostAsync(HttpContext http, AdminMcpDispatcher dispatcher, CancellationToken cancellationToken)
    {
        // Mcp-Session-Id is ignored: this server is request-scoped and does not mint sessions.
        var caller = http.GetMcpPrincipal() ?? throw new AgentAuthenticationException("MCP authentication required.");
        using var document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            var results = new List<object?>();
            foreach (var item in root.EnumerateArray())
            {
                results.Add(await DispatchAsync(item, caller, dispatcher, cancellationToken));
            }

            return Results.Json(results, Json);
        }

        var response = await DispatchAsync(root, caller, dispatcher, cancellationToken);
        if (response is null)
        {
            return Results.Accepted();
        }

        return Results.Json(response, Json);
    }

    private static async Task<object?> DispatchAsync(
        JsonElement request,
        Application.Abstractions.McpCallerPrincipal caller,
        AdminMcpDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var method = request.TryGetProperty("method", out var methodNode) ? methodNode.GetString() : null;
        JsonElement id = default;
        var hasId = request.TryGetProperty("id", out id);
        if (string.IsNullOrWhiteSpace(method))
        {
            return Error(id, hasId, -32600, "Invalid request.");
        }

        if (method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            object result = method switch
            {
                "initialize" => Initialize(request),
                "ping" => new { },
                "tools/list" => await ListToolsAsync(caller, dispatcher, cancellationToken),
                "tools/call" => await CallToolAsync(request, caller, dispatcher, cancellationToken),
                "resources/list" => await dispatcher.ListResourcesAsync(caller, cancellationToken),
                "resources/templates/list" => dispatcher.ListResourceTemplates(),
                "resources/read" => await dispatcher.ReadResourceAsync(caller, ReadParamString(request, "uri") ?? string.Empty, cancellationToken),
                "prompts/list" => dispatcher.ListPrompts(),
                "prompts/get" => dispatcher.GetPrompt(ReadParamString(request, "name") ?? string.Empty, ReadParamObject(request, "arguments")),
                "completions/complete" => await dispatcher.CompleteAsync(caller, ReadParams(request), cancellationToken),
                _ => throw new RequestValidationException($"Unsupported method '{method}'.")
            };
            return hasId
                ? new { jsonrpc = "2.0", id = CloneId(id), result }
                : null;
        }
        catch (Exception ex) when (ex is RequestValidationException or ForbiddenException or NotFoundException or ConflictException)
        {
            return Error(id, hasId, -32602, ex.Message);
        }
        catch (Exception ex) when (ex is FailClosedException)
        {
            return Error(id, hasId, -32000, ex.Message);
        }
    }

    private static object Initialize(JsonElement request)
    {
        var requested = ReadParamString(request, "protocolVersion") ?? Protocol;
        var version = SupportedProtocols.Contains(requested, StringComparer.Ordinal) ? requested : Protocol;
        return new
        {
            protocolVersion = version,
            capabilities = new
            {
                tools = new { listChanged = false },
                resources = new { subscribe = false, listChanged = false },
                prompts = new { listChanged = false },
                completions = new { }
            },
            serverInfo = new { name = "deleguard-admin", version = ServerVersion },
            instructions =
                "Deleguard Administration MCP. Policy decides ALLOW, DENY, and REQUIRE_APPROVAL — never the model. " +
                "Before creating environments, agents, or policies, call docs_list and docs_get (start with configure-with-mcp). " +
                "Prompt configure-environment walks the setup. Full docs: http://localhost:4321. " +
                "Webhooks: webhook_list|get|create|update|rotate_secret|ping|attempts|resend|recover. Secrets are shown once. " +
                "Pending work is approved in the console at /approvals/{id}. There is no approve or deny tool. " +
                "Prefer structuredContent. Do not log prompts or raw model output."
        };
    }

    private static async Task<object> ListToolsAsync(
        Application.Abstractions.McpCallerPrincipal caller,
        AdminMcpDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var tools = await dispatcher.ListToolsAsync(caller, cancellationToken);
        return new
        {
            tools = tools.Select(tool => new
            {
                name = tool.ToolName,
                title = tool.Title,
                description = tool.Description,
                inputSchema = JsonNode.Parse((tool.ReadOnlyHint ? tool.InputSchema : OperationSchema.WithIdentity(tool.InputSchema)).GetRawText()),
                outputSchema = JsonNode.Parse(tool.OutputSchema.GetRawText()),
                annotations = new
                {
                    readOnlyHint = tool.ReadOnlyHint,
                    destructiveHint = tool.DestructiveHint,
                    idempotentHint = tool.IdempotentHint,
                    openWorldHint = tool.OpenWorldHint
                }
            })
        };
    }

    private static async Task<object> CallToolAsync(
        JsonElement request,
        Application.Abstractions.McpCallerPrincipal caller,
        AdminMcpDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var args = JsonSerializer.SerializeToElement(new { });
        var name = string.Empty;
        if (request.TryGetProperty("params", out var parameters))
        {
            name = parameters.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? string.Empty : string.Empty;
            if (parameters.TryGetProperty("arguments", out var arguments) && arguments.ValueKind is JsonValueKind.Object)
            {
                args = arguments.Clone();
            }
        }

        AdminMcpEnvelope envelope;
        AdminMcpToolSpec? spec = AdminMcpCatalog.Find(name);
        try
        {
            envelope = await dispatcher.CallAsync(caller, name, args, cancellationToken);
        }
        catch (Exception ex) when (ex is RequestValidationException or ForbiddenException or NotFoundException or ConflictException)
        {
            envelope = AdminMcpEnvelope.Error(ex.Message);
        }

        var json = AdminMcpJson.Element(envelope).GetRawText();
        var result = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = json
            }),
            ["structuredContent"] = JsonNode.Parse(json),
            ["isError"] = envelope.IsError
        };
        if (spec is { ReadOnlyHint: true } && !envelope.IsError)
        {
            result["_meta"] = new JsonObject
            {
                ["ttlMs"] = 15_000,
                ["cacheScope"] = "private"
            };
        }

        return result;
    }

    private static JsonElement ReadParams(JsonElement request) =>
        request.TryGetProperty("params", out var parameters) ? parameters.Clone() : JsonSerializer.SerializeToElement(new { });

    private static string? ReadParamString(JsonElement request, string name)
    {
        if (!request.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!parameters.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static JsonElement ReadParamObject(JsonElement request, string name)
    {
        if (request.TryGetProperty("params", out var parameters) &&
            parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return value.Clone();
        }

        return JsonSerializer.SerializeToElement(new { });
    }

    private static object Error(JsonElement id, bool hasId, int code, string message) => new
    {
        jsonrpc = "2.0",
        id = hasId ? CloneId(id) : null,
        error = new { code, message }
    };

    private static object? CloneId(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.Number when id.TryGetInt64(out var number) => number,
        JsonValueKind.String => id.GetString(),
        JsonValueKind.Null => null,
        _ => JsonNode.Parse(id.GetRawText())
    };
}
