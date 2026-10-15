using System.Text.Json.Nodes;

namespace Deleguard.AiGateway;

public static class GatewayToolFilter
{
    public static void Apply(JsonNode body, GatewayConfigSnapshot config)
    {
        if (body["tools"] is not JsonArray tools || tools.Count == 0)
        {
            return;
        }

        var allowed = config.AuthorizedTools.Select(item => item.Tool).ToHashSet(StringComparer.Ordinal);
        var kept = new JsonArray();
        foreach (var tool in tools)
        {
            var name = tool?["function"]?["name"]?.GetValue<string>() ?? tool?["name"]?.GetValue<string>();
            if (name is not null && (allowed.Contains(name) || allowed.Any(item => item.EndsWith("__" + name, StringComparison.Ordinal))))
            {
                kept.Add(tool!.DeepClone());
            }
        }

        body["tools"] = kept;
    }
}
