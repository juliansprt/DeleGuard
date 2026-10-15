using System.Collections.Concurrent;

namespace Deleguard.AiGateway;

public sealed class ExactResponseCache
{
    private readonly ConcurrentDictionary<string, Entry> _items = new(StringComparer.Ordinal);
    private const int MaxItems = 256;

    public bool TryGet(string key, int ttlSeconds, out string json)
    {
        json = string.Empty;
        if (ttlSeconds <= 0)
        {
            return false;
        }

        if (!_items.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - entry.StoredAt > TimeSpan.FromSeconds(ttlSeconds))
        {
            _items.TryRemove(key, out _);
            return false;
        }

        json = entry.Json;
        return true;
    }

    public void Set(string key, string json)
    {
        if (_items.Count >= MaxItems)
        {
            foreach (var stale in _items.Where(item => DateTimeOffset.UtcNow - item.Value.StoredAt > TimeSpan.FromMinutes(5)).Take(32))
            {
                _items.TryRemove(stale.Key, out _);
            }
        }

        _items[key] = new Entry(json, DateTimeOffset.UtcNow);
    }

    private sealed record Entry(string Json, DateTimeOffset StoredAt);
}
