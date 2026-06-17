using System.Collections.Concurrent;
using OpcBridge.Core;

namespace OpcBridge.App;

public sealed class BridgeState
{
    private readonly ConcurrentDictionary<string, BridgeValueSnapshot> values_by_da_item_ = new(StringComparer.OrdinalIgnoreCase);
    private readonly object status_lock_ = new();
    private BridgeRuntimeStatus status_ = BridgeRuntimeStatus.Empty;

    public void Configure(string daMode, int updateRateMs, int mappingCount)
    {
        lock (status_lock_)
        {
            status_ = status_ with
            {
                BridgeState = "Starting",
                DaMode = daMode,
                DaConnectionState = "Disconnected",
                UpdateRateMs = updateRateMs,
                MappingCount = mappingCount,
                LastDaReadUtc = null,
                LastDaReadCount = 0,
                LastUaWriteUtc = null,
                LastUaWriteCount = 0,
                LastError = null
            };
        }
    }

    public void SetDaMode(string daMode)
    {
        lock (status_lock_)
        {
            status_ = status_ with { DaMode = daMode };
        }
    }

    public void ClearValues()
    {
        values_by_da_item_.Clear();
    }

    public void SetBridgeState(string bridgeState)
    {
        lock (status_lock_)
        {
            status_ = status_ with { BridgeState = bridgeState };
        }
    }

    public void SetDaConnectionState(string connectionState)
    {
        lock (status_lock_)
        {
            status_ = status_ with { DaConnectionState = connectionState };
        }
    }

    public void SetError(Exception exception)
    {
        lock (status_lock_)
        {
            status_ = status_ with
            {
                BridgeState = "Faulted",
                LastError = exception.Message
            };
        }
    }

    public void UpdateDaRead(IReadOnlyList<BridgeValue> values)
    {
        DateTime readTime = DateTime.UtcNow;

        for (int i = 0; i < values.Count; i++)
        {
            BridgeValue value = values[i];
            values_by_da_item_[value.DaItemId] = new BridgeValueSnapshot(
                value.DaItemId,
                value.Value,
                value.TimestampUtc,
                value.DaQuality,
                value.IsGood);
        }

        lock (status_lock_)
        {
            status_ = status_ with
            {
                BridgeState = "Running",
                DaConnectionState = "Connected",
                LastDaReadUtc = readTime,
                LastDaReadCount = values.Count,
                LastError = null
            };
        }
    }

    public void MarkUaWrite(int valueCount)
    {
        lock (status_lock_)
        {
            status_ = status_ with
            {
                LastUaWriteUtc = DateTime.UtcNow,
                LastUaWriteCount = valueCount
            };
        }
    }

    public IReadOnlyList<BridgeValueSnapshot> GetValues()
    {
        return values_by_da_item_
            .Values
            .OrderBy(value => value.DaItemId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public BridgeRuntimeStatus GetStatus()
    {
        lock (status_lock_)
        {
            return status_;
        }
    }
}

public sealed record BridgeRuntimeStatus(
    string BridgeState,
    string DaMode,
    string DaConnectionState,
    int UpdateRateMs,
    int MappingCount,
    DateTime? LastDaReadUtc,
    int LastDaReadCount,
    DateTime? LastUaWriteUtc,
    int LastUaWriteCount,
    string? LastError)
{
    public static BridgeRuntimeStatus Empty { get; } = new(
        "Stopped",
        "Unknown",
        "Disconnected",
        0,
        0,
        null,
        0,
        null,
        0,
        null);
}

public sealed record BridgeValueSnapshot(
    string DaItemId,
    object? Value,
    DateTime TimestampUtc,
    int DaQuality,
    bool IsGood);
