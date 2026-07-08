using System.Collections.Concurrent;

namespace OpcBridge.App;

/// <summary>Fixed-size ring buffer of recent MQTT traffic for the dashboard monitor.</summary>
public sealed class MqttTrafficStore
{
    private readonly ConcurrentQueue<MqttTrafficEntry> entries_ = new();
    private const int Capacity = 500;

    public void Add(string direction, string topic, string? detail)
    {
        entries_.Enqueue(new MqttTrafficEntry(direction, topic, detail, DateTime.UtcNow));
        while (entries_.Count > Capacity)
        {
            entries_.TryDequeue(out _);
        }
    }

    public IReadOnlyList<MqttTrafficEntry> GetEntries(int limit)
    {
        return entries_
            .OrderByDescending(e => e.TimestampUtc)
            .Take(limit <= 0 ? Capacity : limit)
            .ToArray();
    }
}

public sealed record MqttTrafficEntry(string Direction, string Topic, string? Detail, DateTime TimestampUtc);
