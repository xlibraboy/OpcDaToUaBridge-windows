using System.Collections.Concurrent;

namespace OpcBridge.App;

/// <summary>Latest-value registry of published/received MQTT topics for the dashboard's Traffic Monitor. Keyed by topic + direction so a published (PUB) and a received (SUB) entry for the same topic are tracked separately.</summary>
public sealed class MqttValueStore
{
    private readonly ConcurrentDictionary<string, MqttValueEntry> values_ = new(StringComparer.OrdinalIgnoreCase);

    private static string KeyOf(string direction, string topic) => topic + "\u0001" + direction;

    public void Set(string direction, string topic, string? value)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;
        values_[KeyOf(direction, topic)] = new MqttValueEntry(direction, topic, value, DateTime.UtcNow);
    }

    public MqttValuePage GetEntries(string? direction, string? topic, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 2000 : pageSize;

        IEnumerable<MqttValueEntry> query = values_.Values;

        if (!string.IsNullOrWhiteSpace(direction))
        {
            var dir = direction.Trim();
            query = query.Where(e => string.Equals(e.Direction, dir, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(topic))
        {
            var t = topic.Trim();
            query = query.Where(e => (e.Topic ?? string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        int total = query.Count();
        MqttValueEntry[] items = query
            .OrderBy(e => e.Topic, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Direction, StringComparer.OrdinalIgnoreCase)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return new MqttValuePage(items, total);
    }
}

public sealed record MqttValueEntry(string Direction, string Topic, string? Value, DateTime TimestampUtc);

public sealed record MqttValuePage(IReadOnlyList<MqttValueEntry> Items, int Total);
