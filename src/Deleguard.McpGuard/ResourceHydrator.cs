using System.Text.Json;

namespace Deleguard.McpGuard;

public static class ResourceHydrator
{
    public static bool TryReadString(JsonElement root, string path, out string value)
    {
        value = string.Empty;
        if (!TryRead(root, path, out var node))
        {
            return false;
        }

        value = node.ValueKind switch
        {
            JsonValueKind.String => node.GetString() ?? string.Empty,
            JsonValueKind.Number => node.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => node.GetRawText()
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    public static Dictionary<string, JsonElement> ReadAttributes(
        JsonElement root,
        IReadOnlyDictionary<string, string> paths)
    {
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in paths)
        {
            if (TryRead(root, pair.Value, out var node) && node.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            {
                attributes[pair.Key] = node.Clone();
            }
        }

        return attributes;
    }

    public static bool TryRead(JsonElement root, string path, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(path) || root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        value = current;
        return true;
    }
}
