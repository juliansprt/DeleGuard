using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Deleguard.Application.Abstractions;

namespace Deleguard.Application.Identity;

public static class AdminMcpJson
{
    public static readonly JsonSerializerOptions Snake = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, Snake);
}

public static class AdminMcpSchema
{
    public const string Draft = "https://json-schema.org/draft/2020-12/schema";

    public static JsonElement Envelope { get; } = Element(Object(
        new JsonObject
        {
            ["result_type"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("allow", "deny", "require_approval", "error")
            },
            ["decision"] = NullableString("ALLOW, DENY, REQUIRE_APPROVAL, or null when the call never reached the PDP."),
            ["decision_id"] = NullableString("Persisted decision id when the PDP ran."),
            ["approval_id"] = NullableString("Pending approval id when decision is REQUIRE_APPROVAL."),
            ["reason"] = NullableString("Human-readable policy or validation reason."),
            ["next_action"] = NullableString("What the caller should do next. Never an approve/deny tool."),
            ["data"] = new JsonObject
            {
                ["description"] = "ALLOW payload. Null on deny, pending, or validation errors."
            }
        },
        ["result_type", "decision", "decision_id", "approval_id", "reason", "next_action", "data"],
        "Administration MCP tool envelope."));

    public static JsonObject Object(JsonObject properties, IReadOnlyList<string>? required = null, string? description = null)
    {
        var schema = new JsonObject
        {
            ["$schema"] = Draft,
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties
        };
        if (required is { Count: > 0 })
        {
            var array = new JsonArray();
            foreach (var name in required)
            {
                array.Add(name);
            }

            schema["required"] = array;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            schema["description"] = description;
        }

        return schema;
    }

    public static JsonObject Empty(string? description = null) => Object(new JsonObject(), null, description);

    public static JsonObject Page(string description, bool includeApplication = false)
    {
        var properties = new JsonObject
        {
            ["limit"] = Integer("Page size from 1 to 50.", AdminMcpPaging.MinLimit, AdminMcpPaging.MaxLimit),
            ["cursor"] = String("Opaque next_cursor from the previous page.")
        };
        if (includeApplication)
        {
            properties["application_id"] = ApplicationRef();
        }

        return Object(properties, null, description);
    }

    public static JsonObject ApplicationRef() =>
        String("Product application id (app_…) or slug. Defaults to the default product application. Never Administration.");

    public static JsonObject AgentIds() =>
        StringArray("Agent ids (agt_…) in this application. Authorize DENYs callers that are not assigned.");

    public static JsonObject String(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description
    };

    public static JsonObject Integer(string description, int minimum, int maximum) => new()
    {
        ["type"] = "integer",
        ["description"] = description,
        ["minimum"] = minimum,
        ["maximum"] = maximum
    };

    public static JsonObject Boolean(string description) => new()
    {
        ["type"] = "boolean",
        ["description"] = description
    };

    public static JsonObject Number(string description) => new()
    {
        ["type"] = "number",
        ["description"] = description,
        ["minimum"] = 0
    };

    public static JsonObject StringArray(string description) => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject { ["type"] = "string" },
        ["description"] = description
    };

    public static JsonObject StringEnum(string description, params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = array
        };
    }

    public static JsonObject StringEnumArray(string description, params string[] values) => new()
    {
        ["type"] = "array",
        ["items"] = StringEnum("Allowed value.", values),
        ["description"] = description
    };

    public static JsonElement Element(JsonObject node) => JsonSerializer.SerializeToElement(node);

    private static JsonObject NullableString(string description) => new()
    {
        ["type"] = new JsonArray("string", "null"),
        ["description"] = description
    };

    public static void Validate(AdminMcpToolSpec spec, JsonElement args)
    {
        if (args.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            args = JsonSerializer.SerializeToElement(new { });
        }

        if (args.ValueKind != JsonValueKind.Object)
        {
            throw new RequestValidationException("Tool arguments must be an object.");
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        if (spec.InputSchema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                allowed.Add(property.Name);
            }
        }

        if (spec.InputSchema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False)
        {
            foreach (var property in args.EnumerateObject())
            {
                if (!allowed.Contains(property.Name))
                {
                    throw new RequestValidationException($"Unknown argument '{property.Name}'.");
                }
            }
        }

        if (spec.InputSchema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var nameNode in required.EnumerateArray())
            {
                var name = nameNode.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!args.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    throw new RequestValidationException($"{name} is required.");
                }
            }
        }
    }
}

public sealed record AdminMcpEnvelope(
    string ResultType,
    string? Decision,
    string? DecisionId,
    string? ApprovalId,
    string? Reason,
    string? NextAction,
    object? Data)
{
    public const string PendingConsoleHint =
        "Approve in the console at /approvals/{id}. Do not call an approve tool.";

    [JsonIgnore]
    public bool IsError =>
        string.Equals(ResultType, "deny", StringComparison.Ordinal) ||
        string.Equals(ResultType, "error", StringComparison.Ordinal);

    public static AdminMcpEnvelope FromOutcome<T>(AdminActionOutcome<T> outcome, object? data = null)
    {
        if (outcome.Allowed)
        {
            return new("allow", outcome.Decision, outcome.DecisionId, outcome.ApprovalId, outcome.Reason, null, data ?? outcome.Value);
        }

        if (outcome.Pending)
        {
            var next = string.IsNullOrWhiteSpace(outcome.ApprovalId)
                ? "Approve in the console at /approvals. Do not call an approve tool."
                : PendingConsoleHint.Replace("{id}", outcome.ApprovalId, StringComparison.Ordinal);
            return new("require_approval", outcome.Decision, outcome.DecisionId, outcome.ApprovalId, outcome.Reason, next, null);
        }

        return new("deny", outcome.Decision, outcome.DecisionId, outcome.ApprovalId, outcome.Reason, null, null);
    }

    public static AdminMcpEnvelope Error(string reason) =>
        new("error", null, null, null, reason, null, null);
}

public sealed record AdminMcpPage<T>(IReadOnlyList<T> Items, string? NextCursor, int Limit, int Offset);

public static class AdminMcpPaging
{
    public const int DefaultLimit = 50;
    public const int MinLimit = 1;
    public const int MaxLimit = 50;

    public static (int Limit, int Offset) Read(JsonElement args)
    {
        var limit = DefaultLimit;
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("limit", out var limitNode) &&
            limitNode.ValueKind == JsonValueKind.Number && limitNode.TryGetInt32(out var parsed))
        {
            limit = parsed;
        }

        limit = Math.Clamp(limit, MinLimit, MaxLimit);
        var offset = 0;
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("cursor", out var cursorNode) &&
            cursorNode.ValueKind == JsonValueKind.String)
        {
            offset = Decode(cursorNode.GetString());
        }

        return (limit, Math.Max(0, offset));
    }

    public static AdminMcpPage<T> Slice<T>(IReadOnlyList<T> items, int limit, int offset)
    {
        var page = items.Skip(offset).Take(limit).ToList();
        var next = offset + page.Count < items.Count ? Encode(offset + page.Count) : null;
        return new AdminMcpPage<T>(page, next, limit, offset);
    }

    public static object AsData<T>(AdminMcpPage<T> page) => new
    {
        items = page.Items,
        next_cursor = page.NextCursor,
        limit = page.Limit
    };

    public static string Encode(int offset)
    {
        var bytes = BitConverter.GetBytes(offset);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static int Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return 0;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            var bytes = Convert.FromBase64String(padded);
            return bytes.Length >= 4 ? BitConverter.ToInt32(bytes, 0) : 0;
        }
        catch (FormatException)
        {
            throw new RequestValidationException("cursor is invalid.");
        }
    }
}
