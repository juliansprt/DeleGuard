using System.Text.Json;

namespace Deleguard.Domain.Notifications;

public static class WebhookEventFilter
{
    public static string Serialize(IReadOnlyList<string> eventTypes)
    {
        var unique = eventTypes
            .Where(WebhookEventTypes.IsSubscribable)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        return JsonSerializer.Serialize(unique);
    }

    public static IReadOnlyList<string> Parse(string eventTypesJson)
    {
        if (string.IsNullOrWhiteSpace(eventTypesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(eventTypesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static bool Accepts(string eventTypesJson, string eventType)
    {
        var types = Parse(eventTypesJson);
        return types.Count == 0 || types.Contains(eventType, StringComparer.Ordinal);
    }
}
