using System.Text;
using System.Text.Json;

namespace Deleguard.AiGateway;

public sealed record StreamReceipt(bool Complete, int? InputTokens, int? OutputTokens);

/// <summary>Forwards bytes unchanged and reads only the bounded SSE usage receipt, never storing prompts.</summary>
public static class ProviderStreamCopy
{
    public static async Task<StreamReceipt> CopyAsync(Stream source, Stream destination, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var line = new List<byte>();
        var done = false;
        int? input = null, output = null;
        while (true)
        {
            var length = await source.ReadAsync(buffer, ct);
            if (length == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, length), ct);
            await destination.FlushAsync(ct);
            for (var i = 0; i < length; i++)
            {
                if (buffer[i] != (byte)'\n')
                {
                    if (line.Count >= 65536) throw new InvalidDataException("SSE event exceeds supported size.");
                    line.Add(buffer[i]);
                    continue;
                }
                var text = Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r'); line.Clear();
                if (!text.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = text[5..].Trim();
                if (data == "[DONE]") { done = true; continue; }
                try
                {
                    using var json = JsonDocument.Parse(data);
                    if (json.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object &&
                        usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pi) && pi >= 0 &&
                        usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var co) && co >= 0)
                    { input = pi; output = co; }
                }
                catch (JsonException) { /* Content chunks are not accounting receipts. */ }
            }
        }
        return new(done, input, output);
    }
}
