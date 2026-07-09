using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using OpcBridge.Core;

namespace OpcBridge.Mqtt;

public sealed class MqttBridge : IMqttBridge, IAsyncDisposable
{
    private readonly ILogger<MqttBridge> logger_;
    private readonly object sync_ = new();
    private IManagedMqttClient? client_;
    private Func<MqttInboundMessage, Task>? message_sink_;
    private MqttConnectionState state_ = MqttConnectionState.Disconnected;

    public MqttBridge(ILogger<MqttBridge> logger)
    {
        logger_ = logger;
    }

    public MqttConnectionState State
    {
        get { lock (sync_) { return state_; } }
    }

    public event Action<MqttConnectionState>? StateChanged;

    public void SetMessageSink(Func<MqttInboundMessage, Task> onMessage)
    {
        message_sink_ = onMessage;
    }

    public async Task ConnectAsync(MqttBrokerOptions options, CancellationToken ct)
    {
        await DisconnectAsync(ct).ConfigureAwait(false);

        string brokerUrl = options.BrokerUrl.Trim();
        bool useTls = options.Tls || brokerUrl.StartsWith("mqtts://", StringComparison.OrdinalIgnoreCase);

        MqttClientOptionsBuilder clientBuilder = new MqttClientOptionsBuilder()
            .WithClientId(options.ClientId)
            .WithTcpServer(HostFromUrl(brokerUrl), PortFromUrl(brokerUrl, useTls));

        if (!string.IsNullOrWhiteSpace(options.UserName))
        {
            clientBuilder = clientBuilder.WithCredentials(options.UserName, options.Password);
        }

        if (useTls)
        {
            clientBuilder = clientBuilder.WithTlsOptions(o =>
            {
                if (options.IgnoreCertErrors)
                {
                    o.WithCertificateValidationHandler(_ => true);
                }
            });
        }

        ManagedMqttClientOptions managedOptions = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(clientBuilder.Build())
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .Build();

        IManagedMqttClient client = new MqttFactory().CreateManagedMqttClient();
        client.ApplicationMessageReceivedAsync += OnMessageReceived;
        client.ConnectedAsync += _ => { SetState(MqttConnectionState.Connected); return Task.CompletedTask; };
        client.DisconnectedAsync += e => { SetState(e.Exception is null ? MqttConnectionState.Disconnected : MqttConnectionState.Faulted); return Task.CompletedTask; };
        client.ConnectingFailedAsync += _ => { SetState(MqttConnectionState.Faulted); return Task.CompletedTask; };

        SetState(MqttConnectionState.Connecting);
        await client.StartAsync(managedOptions).ConfigureAwait(false);

        string topicFilter = $"{(string.IsNullOrWhiteSpace(options.TopicPrefix) ? "bridge/tags" : options.TopicPrefix.Trim().Trim('/'))}/#";
        await client.SubscribeAsync(topicFilter).ConfigureAwait(false);

        lock (sync_)
        {
            client_ = client;
        }
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        IManagedMqttClient? client;
        lock (sync_)
        {
            client = client_;
            client_ = null;
        }

        if (client is not null)
        {
            try
            {
                await client.StopAsync().ConfigureAwait(false);
                client.Dispose();
            }
            catch (Exception ex)
            {
                logger_.LogWarning(ex, "MQTT disconnect failed");
            }
        }

        SetState(MqttConnectionState.Disconnected);
    }

    public async Task PublishAsync(string topic, string payload, CancellationToken ct)
    {
        IManagedMqttClient? client;
        lock (sync_) { client = client_; }
        if (client is null || client.IsStarted == false) return;

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(false)
            .Build();

        await client.EnqueueAsync(message).ConfigureAwait(false);
    }

    private Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
    {
        Func<MqttInboundMessage, Task>? sink = message_sink_;
        if (sink is null) return Task.CompletedTask;

        string payloadText = e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;
        (string? rawValue, DateTime? ts) = MqttPayload.Parse(payloadText);
        MqttInboundMessage inbound = new(e.ApplicationMessage.Topic, rawValue, ts);
        return sink(inbound);
    }

    private void SetState(MqttConnectionState state)
    {
        lock (sync_) { state_ = state; }
        StateChanged?.Invoke(state);
    }

    private static string StripScheme(string url)
    {
        int idx = url.IndexOf("://", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? url[(idx + 3)..] : url;
    }

    private static string HostFromUrl(string url)
    {
        string authority = StripScheme(url);
        if (authority.StartsWith("[", StringComparison.Ordinal))
        {
            int close = authority.IndexOf(']');
            if (close >= 0)
            {
                return authority.Substring(1, close - 1);
            }
        }

        int colon = authority.LastIndexOf(':');
        if (colon >= 0 && authority.IndexOf('/', colon) < 0)
        {
            authority = authority.Substring(0, colon);
        }

        return authority;
    }

    private static int PortFromUrl(string url, bool useTls)
    {
        string authority = StripScheme(url);
        if (authority.StartsWith("[", StringComparison.Ordinal))
        {
            int close = authority.IndexOf(']');
            if (close >= 0 && close + 1 < authority.Length && authority[close + 1] == ':'
                && int.TryParse(authority[(close + 2)..], out int ipv6Port))
            {
                return ipv6Port;
            }

            return useTls ? 8883 : 1883;
        }

        int colon = authority.LastIndexOf(':');
        if (colon >= 0 && authority.IndexOf('/', colon) < 0 && int.TryParse(authority[(colon + 1)..], out int port))
        {
            return port;
        }

        return useTls ? 8883 : 1883;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
