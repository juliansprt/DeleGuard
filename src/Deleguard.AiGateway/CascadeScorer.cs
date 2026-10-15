using System.Text.Json;
using System.Text.Json.Nodes;

namespace Deleguard.AiGateway;

public static class CascadeScorer
{
    public static bool ShouldEscalate(
        string routingMode,
        JsonNode request,
        string responseJson,
        int attempts,
        int plannedCount) =>
        EscalateReason(routingMode, request, responseJson, attempts, plannedCount) is not null;

    public static string? EscalateReason(
        string routingMode,
        JsonNode request,
        string responseJson,
        int attempts,
        int plannedCount)
    {
        if (attempts >= 1 || plannedCount < 2)
        {
            return null;
        }

        if (!string.Equals(routingMode, "cascade", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return NeedsStrongReason(request, responseJson);
    }

    public static bool NeedsStrong(JsonNode request, string responseJson) =>
        NeedsStrongReason(request, responseJson) is not null;

    public static string? NeedsStrongReason(JsonNode request, string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return "empty";
        }

        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            var content = ReadContent(root);
            var finish = ReadFinishReason(root);
            if (string.Equals(finish, "length", StringComparison.OrdinalIgnoreCase))
            {
                return "length";
            }

            if (string.Equals(finish, "content_filter", StringComparison.OrdinalIgnoreCase))
            {
                return "content_filter";
            }

            if (!string.IsNullOrWhiteSpace(ReadRefusal(root)))
            {
                return "refusal";
            }

            if (WantsJson(request) && !LooksLikeJson(content))
            {
                return "json";
            }

            if (RequestHasTools(request) && !HasToolCalls(root) && string.IsNullOrWhiteSpace(content))
            {
                return "tools";
            }

            if (string.IsNullOrWhiteSpace(content) && !HasToolCalls(root))
            {
                return "empty";
            }

            return null;
        }
        catch (JsonException)
        {
            return "parse_error";
        }
    }

    private static bool WantsJson(JsonNode request)
    {
        var format = request["response_format"]?["type"]?.GetValue<string>();
        return string.Equals(format, "json_object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(format, "json_schema", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequestHasTools(JsonNode request)
    {
        if (request["tools"] is JsonArray tools && tools.Count > 0)
        {
            return true;
        }

        return request["functions"] is JsonArray functions && functions.Count > 0;
    }

    private static bool LooksLikeJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var trimmed = content.Trim();
        if (trimmed[0] is not '{' and not '[')
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadFinishReason(JsonElement root)
    {
        if (!TryChoice(root, out var choice))
        {
            return null;
        }

        return choice.TryGetProperty("finish_reason", out var reason) ? reason.GetString() : null;
    }

    private static string? ReadRefusal(JsonElement root)
    {
        if (root.TryGetProperty("refusal", out var rootRefusal))
        {
            var value = rootRefusal.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (!TryChoice(root, out var choice) || !choice.TryGetProperty("message", out var message))
        {
            return null;
        }

        return message.TryGetProperty("refusal", out var refusal) ? refusal.GetString() : null;
    }

    private static bool HasToolCalls(JsonElement root)
    {
        if (!TryChoice(root, out var choice))
        {
            return false;
        }

        if (choice.TryGetProperty("finish_reason", out var finish) &&
            string.Equals(finish.GetString(), "tool_calls", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!choice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("tool_calls", out var calls) ||
            calls.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return calls.GetArrayLength() > 0;
    }

    private static bool TryChoice(JsonElement root, out JsonElement choice)
    {
        choice = default;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        choice = choices[0];
        return true;
    }

    private static string? ReadContent(JsonElement root)
    {
        if (!TryChoice(root, out var choice))
        {
            return null;
        }

        if (choice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
        {
            return ReadText(content);
        }

        return choice.TryGetProperty("text", out var text) ? text.GetString() : null;
    }

    private static string? ReadText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                parts.Add(part.GetString() ?? string.Empty);
                continue;
            }

            if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text))
            {
                parts.Add(text.GetString() ?? string.Empty);
            }
        }

        var joined = string.Concat(parts);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}
