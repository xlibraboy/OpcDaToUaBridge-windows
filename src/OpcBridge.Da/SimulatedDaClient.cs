using OpcBridge.Core;

namespace OpcBridge.Da;

public sealed class SimulatedDaClient : IDaClient
{
    private const int GoodQuality = 0xC0;
    private long sample_index_;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sample_index_ = 0;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BridgeValue>> ReadAsync(
        IReadOnlyList<TagMapping> mappings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long sample = Interlocked.Increment(ref sample_index_);
        DateTime timestamp = DateTime.UtcNow;
        BridgeValue[] values = new BridgeValue[mappings.Count];

        for (int i = 0; i < mappings.Count; i++)
        {
            TagMapping mapping = mappings[i];
            object value = CreateValue(mapping.DataType, sample, i);
            values[i] = new BridgeValue(
                mapping.DaItemId,
                value,
                timestamp,
                GoodQuality,
                QualityMapper.IsGoodDaQuality(GoodQuality));
        }

        return Task.FromResult<IReadOnlyList<BridgeValue>>(values);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private static object CreateValue(string dataType, long sample, int index)
    {
        return dataType.Trim().ToUpperInvariant() switch
        {
            "BOOL" or "BOOLEAN" => (sample + index) % 2 == 0,
            "BYTE" => (byte)((sample + index) % byte.MaxValue),
            "INT16" or "SHORT" => (short)(sample + index),
            "INT32" or "INT" => (int)(sample + index),
            "INT64" or "LONG" => sample + index,
            "FLOAT" or "SINGLE" => (float)(sample + (index / 10.0)),
            "DOUBLE" or "REAL8" => sample + (index / 10.0),
            "STRING" => $"SIM-{sample}-{index}",
            _ => sample + (index / 10.0)
        };
    }
}
