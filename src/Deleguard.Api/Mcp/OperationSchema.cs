using System.Text.Json;
using System.Text.Json.Nodes;
namespace Deleguard.Api.Mcp;
public static class OperationSchema
{
    public const string Field = "_deleguard_operation_id";
    public static JsonElement WithIdentity(JsonElement schema)
    {
        var node = JsonNode.Parse(schema.GetRawText())!.AsObject();
        var properties = node["properties"] as JsonObject ?? new JsonObject();
        if (node["properties"] is null) node["properties"] = properties;
        properties[Field] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 100,
            ["description"] = "Unique operation identity; reuse unchanged on retries and after approval. Use a new value only for a new operation." };
        var required = node["required"] as JsonArray ?? new JsonArray();
        if (node["required"] is null) node["required"] = required;
        required.Add(Field);
        return JsonSerializer.SerializeToElement(node);
    }
}
