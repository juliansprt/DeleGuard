using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Deleguard.AiGateway;

public static class TokenEstimator
{
    public static int EstimateTokens(JsonNode? messages)
    {
        var text = Flatten(messages);
        if (text.Length == 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    public static string MessageHash(JsonNode? messages)
    {
        var canonical = Flatten(messages);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string PrefixHash(JsonNode? messages)
    {
        if (messages is not JsonArray array)
        {
            return MessageHash(messages);
        }

        var prefix = new StringBuilder();
        foreach (var item in array)
        {
            var role = item?["role"]?.GetValue<string>();
            if (!string.Equals(role, "system", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "developer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            prefix.Append(Flatten(item?["content"]));
        }

        var text = prefix.ToString();
        if (text.Length == 0)
        {
            return MessageHash(messages);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public static string Flatten(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        return node switch
        {
            JsonValue value => value.ToString(),
            JsonArray array => string.Concat(array.Select(Flatten)),
            JsonObject obj when obj.TryGetPropertyValue("text", out var text) => Flatten(text),
            JsonObject obj => string.Concat(obj.Select(pair => Flatten(pair.Value))),
            _ => node.ToJsonString()
        };
    }
}
