using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Deleguard.Domain.Common;

public static class CanonicalJson
{
    public static string Serialize(JsonNode? node)
    {
        var normalized = Normalize(node);
        return normalized is null
            ? "null"
            : normalized.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    public static string HashHex(JsonNode? node)
    {
        var canonical = Serialize(node);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string HashHex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static JsonNode? Normalize(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject obj)
        {
            var ordered = new JsonObject();
            foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                ordered[property.Key] = Normalize(property.Value)?.DeepClone();
            }

            return ordered;
        }

        if (node is JsonArray array)
        {
            var copy = new JsonArray();
            foreach (var item in array)
            {
                copy.Add(Normalize(item)?.DeepClone());
            }

            return copy;
        }

        return node.DeepClone();
    }
}
