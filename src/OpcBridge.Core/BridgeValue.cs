namespace OpcBridge.Core;

public sealed record BridgeValue(
    string SourceId,
    string DaItemId,
    object? Value,
    DateTime TimestampUtc,
    int DaQuality,
    bool IsGood);