namespace OpcBridge.Core;

[Flags]
public enum MqttPayloadField
{
    None = 0,
    Value = 1,
    Timestamp = 2,
    Quality = 4,
    SourceId = 8,
    ItemId = 16,
    DisplayName = 32,
    DataType = 64
}
