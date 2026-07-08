using OpcBridge.Core;

namespace OpcBridge.Mqtt;

public enum MqttConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted
}

public interface IMqttBridge
{
    /// <summary>Connect (or reconnect) using the given options. Subscribes to {TopicPrefix}/#.</summary>
    Task ConnectAsync(MqttBrokerOptions options, CancellationToken ct);

    Task DisconnectAsync(CancellationToken ct);

    /// <summary>Publish a payload string to a topic. Non-blocking from the caller's perspective.</summary>
    Task PublishAsync(string topic, string payload, CancellationToken ct);

    /// <summary>Register the callback invoked for every inbound message.</summary>
    void SetMessageSink(Func<MqttInboundMessage, Task> onMessage);

    /// <summary>Connection-state change notifications (auto-reconnect, fault).</summary>
    event Action<MqttConnectionState>? StateChanged;

    MqttConnectionState State { get; }
}
