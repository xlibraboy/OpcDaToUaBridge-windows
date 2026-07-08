namespace OpcBridge.Mqtt;

/// <summary>
/// A message received from the broker. The topic is the raw MQTT topic;
/// <see cref="RawValue"/> is the string form of the published "v" field (or the
/// whole payload when the payload is not JSON). Timestamp is parsed from "t" when present.
/// </summary>
public sealed record MqttInboundMessage(
    string Topic,
    string? RawValue,
    DateTime? TimestampUtc);
