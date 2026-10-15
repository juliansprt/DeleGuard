using System.Text;
using System.Text.Json;

namespace Deleguard.Application.Notifications;

public static class WebhookEnvelope
{
    public static string Create(string messageId, string eventType, DateTimeOffset timestamp, JsonElement data)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", messageId);
            writer.WriteString("type", eventType);
            writer.WriteString("timestamp", timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"));
            writer.WritePropertyName("data");
            data.WriteTo(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
