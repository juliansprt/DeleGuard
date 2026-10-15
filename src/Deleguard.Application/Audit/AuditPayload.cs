using System.Text.Json;

namespace Deleguard.Application.Audit;

public static class AuditPayload
{
    public const string PlaygroundSource = "playground";

    public static bool IsPlaygroundEvent(string eventType, string? payloadJson)
    {
        if (!string.Equals(eventType, "authorization.decided", StringComparison.Ordinal)
            && !string.Equals(eventType, "execution.reported", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty("source", out var source)
                   && source.ValueKind == JsonValueKind.String
                   && string.Equals(source.GetString(), PlaygroundSource, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
