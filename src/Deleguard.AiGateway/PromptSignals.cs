using System.Text.Json.Nodes;

namespace Deleguard.AiGateway;

public static class GatewayRouteReasons
{
    public const string Static = "static";
    public const string LengthCheap = "length_cheap";
    public const string LengthStrong = "length_strong";
    public const string Tools = "tools";
    public const string JsonSchema = "json_schema";
    public const string Image = "image";
    public const string CascadeSkipLength = "cascade_skip_length";
    public const string CascadeEscalate = "cascade_escalate";
}

public sealed record PromptSignals(
    int EstimatedInputTokens,
    bool HasTools,
    int ToolCount,
    bool WantsJson,
    bool WantsJsonSchema,
    bool HasImage,
    int MessageCount,
    int? RequestedMaxTokens)
{
    public bool ForceStrong => HasImage || WantsJsonSchema || HasTools;

    public static PromptSignals LengthOnly(int estimatedInputTokens) =>
        new(Math.Max(1, estimatedInputTokens), false, 0, false, false, false, 1, null);

    public static PromptSignals FromRequest(JsonNode body, int estimatedInputTokens)
    {
        var format = ReadString(body["response_format"]?["type"]);
        var wantsSchema = string.Equals(format, "json_schema", StringComparison.OrdinalIgnoreCase);
        var wantsJson = wantsSchema ||
                        string.Equals(format, "json_object", StringComparison.OrdinalIgnoreCase);
        var messages = body["messages"] ?? body["input"];
        var toolCount = CountArray(body["tools"]);
        if (toolCount == 0)
        {
            toolCount = CountArray(body["functions"]);
        }

        return new PromptSignals(
            Math.Max(1, estimatedInputTokens),
            toolCount > 0,
            toolCount,
            wantsJson,
            wantsSchema,
            ContainsImage(messages) || ContainsImage(body["content"]),
            CountArray(messages) is var count && count > 0 ? count : 1,
            ReadPositiveInt(body["max_tokens"]) ?? ReadPositiveInt(body["max_completion_tokens"]));
    }

    public static int DispatchMax(
        PromptSignals signals,
        string routeReason,
        IReadOnlyList<GatewayTokenBudget> tokenBudgetClasses,
        int heuristicCheapMaxTokens,
        int cappedMax)
    {
        if (cappedMax < 1)
        {
            return 1;
        }

        if (signals.ForceStrong ||
            string.Equals(routeReason, GatewayRouteReasons.CascadeSkipLength, StringComparison.Ordinal))
        {
            return cappedMax;
        }

        var shortMax = ClassMax(tokenBudgetClasses, "short", 256);
        var standardMax = ClassMax(tokenBudgetClasses, "standard", 2048);
        var hint = signals.EstimatedInputTokens <= heuristicCheapMaxTokens ? shortMax : standardMax;
        return Math.Min(cappedMax, hint);
    }

    private static int ClassMax(IReadOnlyList<GatewayTokenBudget> classes, string name, int fallback) =>
        classes.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal))?.MaxOutputTokens
        is int tokens and > 0
            ? tokens
            : fallback;

    private static int CountArray(JsonNode? node) =>
        node is JsonArray array ? array.Count : 0;

    private static int? ReadPositiveInt(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<int>(out var number) && number > 0)
        {
            return number;
        }

        return null;
    }

    private static bool ContainsImage(JsonNode? node) =>
        node switch
        {
            JsonObject obj => ObjectIsImage(obj) || obj.Select(pair => pair.Value).Any(ContainsImage),
            JsonArray array => array.Any(ContainsImage),
            _ => false
        };

    private static bool ObjectIsImage(JsonObject obj)
    {
        if (obj.ContainsKey("image_url") || obj.ContainsKey("input_image"))
        {
            return true;
        }

        var type = ReadString(obj["type"]);
        return string.Equals(type, "image_url", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "input_image", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "image", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
