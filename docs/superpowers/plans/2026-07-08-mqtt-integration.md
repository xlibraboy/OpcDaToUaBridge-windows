# MQTT Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add MQTT publish/subscribe as an OPC UA peer plus a new "MQTT" dashboard tab, scoped entirely to the UA layer.

**Architecture:** New `OpcBridge.Mqtt` project (`net8.0`) with an `IMqttBridge` seam over MQTTnet's `ManagedMqttClient`. `BridgeWorker` (in `OpcBridge.App`) wires publish from `BridgeState` value updates and subscribe-in through the existing UA write path (`WriteQueue`). Broker config lives in `MqttRuntimeSettings` (persisted to `mqtt.json`), seeded from `appsettings.json` `Mqtt:`.

**Tech Stack:** .NET 8, MQTTnet ~v4.x (`ManagedMqttClient`), ASP.NET Core minimal API, single-page HTML dashboard (`DashboardPage.FullHtml`).

## Global Constraints

- New project `OpcBridge.Mqtt` targets `net8.0`; `Mqtt → Core` only (never reference `OpcBridge.Da`).
- MQTT consumes the `BridgeState` UA mirror; it NEVER calls `OpcDaClient` / touches DCOM.
- Topic scheme: `{TopicPrefix}/{SourceId}/{DaItemId}`, or per-mapping `MqttTopic` override (default prefix `bridge/tags`).
- Payload: minimal JSON, field-selectable; `MqttPayloadField.Value | MqttPayloadField.Timestamp` default. Selectable keys: `v` (value), `t` (timestamp ISO-8601), `q` (quality), `sourceId`, `itemId`, `displayName`, `dataType`.
- Per-mapping flag: `TagMapping.MqttEnabled` (bool) + `TagMapping.MqttTopic` (string?).
- Broker config: runtime + persisted (`mqtt.json` in `AppContext.BaseDirectory`); seeded from `appsettings.json` `Mqtt:`.
- Security: `tcp://` and `mqtts://`, optional username/password, `IgnoreCertErrors` for dev.
- Subscribe-in writes via the existing UA write path (`WriteQueue` / `OnWriteValue` seam) — same as a UA client write.
- **Verification deviation (repo convention):** this repo has NO test project; verification is *clean build (both platforms)* + *manual runtime check on the Windows host*. Per-task verification steps below use `dotnet build` (Linux Docker) as the gate; runtime MQTT checks are called out as manual Windows steps. Do not add a test project.
- Zero-warning build bar on both platforms (Linux Docker SDK + Windows user-profile dotnet).
- Conventional commits (`feat(mqtt): ...`).

---

## Task 1: Core — MQTT contract types

**Files:**
- Modify: `src/OpcBridge.Core/TagMapping.cs`
- Create: `src/OpcBridge.Core/MqttBrokerOptions.cs`
- Create: `src/OpcBridge.Core/MqttPayloadField.cs`

**Interfaces:**
- Produces: `MqttPayloadField` (flags enum), `MqttBrokerOptions` (class), and two new fields on `TagMapping` consumed by every later task.

- [ ] **Step 1: Add the `MqttPayloadField` flags enum**

Create `src/OpcBridge.Core/MqttPayloadField.cs`:
```csharp
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
```

- [ ] **Step 2: Add `MqttBrokerOptions`**

Create `src/OpcBridge.Core/MqttBrokerOptions.cs`:
```csharp
namespace OpcBridge.Core;

public sealed class MqttBrokerOptions
{
    public bool Enabled { get; set; }
    public string BrokerUrl { get; set; } = "tcp://localhost:1883";
    public string ClientId { get; set; } = "OpcDaToUaBridge";
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public bool Tls { get; set; }
    public bool IgnoreCertErrors { get; set; }
    public string TopicPrefix { get; set; } = "bridge/tags";
    public MqttPayloadField PayloadFields { get; set; } = MqttPayloadField.Value | MqttPayloadField.Timestamp;
}
```

- [ ] **Step 3: Extend `TagMapping` with MQTT fields**

In `src/OpcBridge.Core/TagMapping.cs`, add two properties to the `TagMapping` class (after `AccessRights`):
```csharp
    public bool MqttEnabled { get; set; }
    public string? MqttTopic { get; set; }
```

- [ ] **Step 4: Build to verify Core compiles**

Run (from repo root, Linux):
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build src/OpcBridge.Core/OpcBridge.Core.csproj
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).`

- [ ] **Step 5: Commit**
```bash
git add src/OpcBridge.Core/TagMapping.cs src/OpcBridge.Core/MqttBrokerOptions.cs src/OpcBridge.Core/MqttPayloadField.cs
git commit -m "feat(mqtt): add MQTT contract types and per-mapping MQTT fields to Core"
```

---

## Task 2: Core — carry MQTT fields through `MappingStore`

**Files:**
- Modify: `src/OpcBridge.App/MappingStore.cs`

**Interfaces:**
- Consumes: `TagMapping.MqttEnabled`, `TagMapping.MqttTopic` (Task 1).
- Produces: MQTT fields preserved in persisted `mappings.json` and returned from `GET /api/mappings`.

`MappingStore.Normalize` (lines 171-202) reconstructs a `TagMapping` and currently drops `MqttEnabled`/`MqttTopic`. Add them to the rebuilt object:

- [ ] **Step 1: Persist MQTT fields in `Normalize`**

In `src/OpcBridge.App/MappingStore.cs`, inside the `return new TagMapping { ... }` block (after `AccessRights = accessRights`), add:
```csharp
            MqttEnabled = tag.MqttEnabled,
            MqttTopic = string.IsNullOrWhiteSpace(tag.MqttTopic) ? null : tag.MqttTopic.Trim()
```

- [ ] **Step 2: Build to verify**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build src/OpcBridge.App/OpcBridge.App.csproj
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).`

- [ ] **Step 3: Commit**
```bash
git add src/OpcBridge.App/MappingStore.cs
git commit -m "feat(mqtt): preserve per-mapping MQTT fields in MappingStore"
```

---

## Task 3: Mqtt project scaffold + `IMqttBridge`

**Files:**
- Create: `src/OpcBridge.Mqtt/OpcBridge.Mqtt.csproj`
- Create: `src/OpcBridge.Mqtt/IMqttBridge.cs`
- Create: `src/OpcBridge.Mqtt/MqttInboundMessage.cs`
- Modify: `src/OpcBridge.App/OpcBridge.App.csproj` (add ProjectReference)

**Interfaces:**
- Produces: `IMqttBridge` seam, `MqttInboundMessage` DTO, and the new project reference consumed by `BridgeWorker` (Task 7) and `Program.cs` (Task 9).

- [ ] **Step 1: Create the csproj**

Create `src/OpcBridge.Mqtt/OpcBridge.Mqtt.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\OpcBridge.Core\OpcBridge.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="MQTTnet" Version="4.3.7.1207" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Create `MqttInboundMessage`**

Create `src/OpcBridge.Mqtt/MqttInboundMessage.cs`:
```csharp
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
```

- [ ] **Step 3: Create `IMqttBridge`**

Create `src/OpcBridge.Mqtt/IMqttBridge.cs`:
```csharp
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
```
Note: `using OpcBridge.Core;` is required because `ConnectAsync` takes `MqttBrokerOptions`.

- [ ] **Step 4: Add project reference in App csproj**

In `src/OpcBridge.App/OpcBridge.App.csproj`, add inside the existing `<ItemGroup>` of `ProjectReference` elements (after the `OpcBridge.Ua` reference):
```xml
    <ProjectReference Include="..\OpcBridge.Mqtt\OpcBridge.Mqtt.csproj" />
```

- [ ] **Step 5: Build the Mqtt project**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build src/OpcBridge.Mqtt/OpcBridge.Mqtt.csproj
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).` (The `IMqttBridge` implementation arrives in Task 4; the interface alone compiles.)

- [ ] **Step 6: Commit**
```bash
git add src/OpcBridge.Mqtt/OpcBridge.Mqtt.csproj src/OpcBridge.Mqtt/IMqttBridge.cs src/OpcBridge.Mqtt/MqttInboundMessage.cs src/OpcBridge.App/OpcBridge.App.csproj
git commit -m "feat(mqtt): scaffold OpcBridge.Mqtt project and IMqttBridge seam"
```

---

## Task 4: `MqttBridge` implementation (MQTTnet `ManagedMqttClient`)

**Files:**
- Create: `src/OpcBridge.Mqtt/MqttBridge.cs`
- Create: `src/OpcBridge.Mqtt/MqttPayload.cs` (topic builder + payload serialize/parse)

**Interfaces:**
- Consumes: `MqttBrokerOptions`, `MqttPayloadField`, `MqttInboundMessage`, `IMqttBridge` (Tasks 1, 3).
- Produces: the concrete `MqttBridge` used by `BridgeWorker` and `Program.cs`.

- [ ] **Step 1: Create `MqttPayload` (topic + payload helpers)**

Create `src/OpcBridge.Mqtt/MqttPayload.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using OpcBridge.Core;

namespace OpcBridge.Mqtt;

internal static class MqttPayload
{
    public static string BuildTopic(MqttBrokerOptions options, string sourceId, string daItemId, string? overrideTopic)
    {
        if (!string.IsNullOrWhiteSpace(overrideTopic))
        {
            return overrideTopic!.Trim();
        }

        string prefix = string.IsNullOrWhiteSpace(options.TopicPrefix) ? "bridge/tags" : options.TopicPrefix.Trim().Trim('/');
        return $"{prefix}/{sourceId.Trim()}/{daItemId.Trim()}";
    }

    public static string Serialize(BridgeValue value, MqttPayloadField fields)
    {
        JsonObject obj = new();

        if (fields.HasFlag(MqttPayloadField.Value))
        {
            obj["v"] = JsonSerializer.SerializeToElement(value.Value);
        }

        if (fields.HasFlag(MqttPayloadField.Timestamp))
        {
            obj["t"] = value.TimestampUtc.ToString("o");
        }

        if (fields.HasFlag(MqttPayloadField.Quality))
        {
            obj["q"] = value.IsGood ? "Good" : "Bad";
        }

        if (fields.HasFlag(MqttPayloadField.SourceId))
        {
            obj["sourceId"] = value.SourceId;
        }

        if (fields.HasFlag(MqttPayloadField.ItemId))
        {
            obj["itemId"] = value.DaItemId;
        }

        if (fields.HasFlag(MqttPayloadField.DisplayName))
        {
            obj["displayName"] = value.SourceId; // placeholder; BridgeWorker overrides display name
        }

        if (fields.HasFlag(MqttPayloadField.DataType))
        {
            obj["dataType"] = value.Value?.GetType().Name ?? "null";
        }

        return obj.ToJsonString();
    }

    /// <summary>Parse an inbound payload. Returns the raw "v" string (or whole payload), and a timestamp if "t" is present.</summary>
    public static (string? RawValue, DateTime? TimestampUtc) Parse(string payload)
    {
        ReadOnlySpan<char> span = payload.AsSpan().Trim();
        if (span.Length == 0)
        {
            return (null, null);
        }

        // Try JSON object with "v"/"t".
        try
        {
            using JsonDocument doc = JsonDocument.Parse(span.ToString());
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                JsonElement v = doc.RootElement.GetProperty("v");
                string? raw = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
                DateTime? ts = null;
                if (doc.RootElement.TryGetProperty("t", out JsonElement t) && t.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(t.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed))
                    {
                        ts = parsed;
                    }
                }

                return (raw, ts);
            }
        }
        catch (JsonException)
        {
            // Not JSON — treat whole payload as the value.
        }

        return (span.ToString(), null);
    }
}
```
Note: `DisplayName` in `Serialize` is set to `SourceId` as a placeholder; `BridgeWorker` (Task 7) passes the mapping's display name by post-processing. To keep the contract clean, change `Serialize` to also accept an optional `displayName` parameter. Update the signature:

```csharp
    public static string Serialize(BridgeValue value, MqttPayloadField fields, string? displayName = null)
    {
        ...
        if (fields.HasFlag(MqttPayloadField.DisplayName))
        {
            obj["displayName"] = displayName ?? value.SourceId;
        }
        ...
    }
```

- [ ] **Step 2: Create `MqttBridge`**

Create `src/OpcBridge.Mqtt/MqttBridge.cs`:
```csharp
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
            .WithTcpServer(StripScheme(brokerUrl), PortFromUrl(brokerUrl, useTls));

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
                    o.CertificateValidationHandler = _ => true;
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
        client.ConnectionFailedAsync += _ => { SetState(MqttConnectionState.Faulted); return Task.CompletedTask; };

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

        await client.EnqueueAsync(message, ct).ConfigureAwait(false);
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

    private static int PortFromUrl(string url, bool useTls)
    {
        string host = StripScheme(url);
        int colon = host.LastIndexOf(':');
        if (colon >= 0 && int.TryParse(host[(colon + 1)..], out int port))
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
```

- [ ] **Step 3: Build the Mqtt project**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build src/OpcBridge.Mqtt/OpcBridge.Mqtt.csproj
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).`

- [ ] **Step 4: Commit**
```bash
git add src/OpcBridge.Mqtt/MqttBridge.cs src/OpcBridge.Mqtt/MqttPayload.cs
git commit -m "feat(mqtt): implement MqttBridge over MQTTnet ManagedMqttClient"
```

---

## Task 5: App — `MqttRuntimeSettings` + `MqttTrafficStore`

**Files:**
- Create: `src/OpcBridge.App/MqttRuntimeSettings.cs`
- Create: `src/OpcBridge.App/MqttTrafficStore.cs`

**Interfaces:**
- Consumes: `MqttBrokerOptions` (Task 1).
- Produces: `MqttRuntimeSettings` (singleton holding options + state + counters, persisted to `mqtt.json`) and `MqttTrafficStore` (ring buffer) consumed by `BridgeWorker` (Task 7) and `Program.cs` (Task 9).

- [ ] **Step 1: Create `MqttRuntimeSettings`**

Create `src/OpcBridge.App/MqttRuntimeSettings.cs`:
```csharp
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpcBridge.Core;

namespace OpcBridge.App;

public sealed class MqttRuntimeSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object sync_ = new();
    private readonly string persist_path_;
    private MqttBrokerOptions options_;
    private string state_ = "Disconnected";
    private string? last_error_;
    private long published_count_;
    private long received_count_;

    public MqttRuntimeSettings(IOptions<MqttBrokerOptions> options)
    {
        persist_path_ = Path.Combine(AppContext.BaseDirectory, "mqtt.json");
        MqttBrokerOptions? loaded = LoadFromDisk();
        options_ = loaded ?? options.Value;
    }

    public MqttBrokerOptions GetOptions()
    {
        lock (sync_) { return options_; }
    }

    public MqttRuntimeSnapshot GetSnapshot()
    {
        lock (sync_)
        {
            return new MqttRuntimeSnapshot(state_, last_error_, published_count_, received_count_, options_);
        }
    }

    public void UpsertOptions(MqttBrokerOptions updated)
    {
        lock (sync_)
        {
            options_ = updated;
            Persist();
        }
    }

    public void SetState(string state, string? lastError = null)
    {
        lock (sync_)
        {
            state_ = state;
            last_error_ = lastError;
        }
    }

    public void IncrementPublished() { lock (sync_) { published_count_++; } }
    public void IncrementReceived() { lock (sync_) { received_count_++; } }

    private void Persist()
    {
        try
        {
            lock (sync_)
            {
                string json = JsonSerializer.Serialize(options_, JsonOptions);
                File.WriteAllText(persist_path_, json);
            }
        }
        catch
        {
        }
    }

    private MqttBrokerOptions? LoadFromDisk()
    {
        try
        {
            if (!File.Exists(persist_path_)) return null;
            return JsonSerializer.Deserialize<MqttBrokerOptions>(File.ReadAllText(persist_path_));
        }
        catch
        {
            return null;
        }
    }
}

public sealed record MqttRuntimeSnapshot(
    string State,
    string? LastError,
    long PublishedCount,
    long ReceivedCount,
    MqttBrokerOptions Options);
```

- [ ] **Step 2: Create `MqttTrafficStore` (ring buffer)**

Create `src/OpcBridge.App/MqttTrafficStore.cs`:
```csharp
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
```

- [ ] **Step 3: Build to verify**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build src/OpcBridge.App/OpcBridge.App.csproj
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).`

- [ ] **Step 4: Commit**
```bash
git add src/OpcBridge.App/MqttRuntimeSettings.cs src/OpcBridge.App/MqttTrafficStore.cs
git commit -m "feat(mqtt): add MqttRuntimeSettings (persisted mqtt.json) and traffic store"
```

---

## Task 6: Core/App — `BridgeState` value-update event

**Files:**
- Modify: `src/OpcBridge.App/BridgeState.cs`

**Interfaces:**
- Produces: `event Action<BridgeValue>? ValueUpdated` raised on every `SetValue`, consumed by `BridgeWorker` (Task 7).

`BridgeState.SetValue` (lines 126-135) is the single funnel for all value updates (poll, subscription, manual). Raise the event there.

- [ ] **Step 1: Add the event field**

In `src/OpcBridge.App/BridgeState.cs`, add near the top of the class (after the `values_by_key_` field):
```csharp
    public event Action<BridgeValue>? ValueUpdated;
```

- [ ] **Step 2: Raise it in `SetValue`**

In `SetValue`, after the `values_by_key_[...] = new BridgeValueSnapshot(...)` assignment, add:
```csharp
        ValueUpdated?.Invoke(value);
```

- [ ] **Step 3: Build to verify**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build src/OpcBridge.App/OpcBridge.App.csproj
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).`

- [ ] **Step 4: Commit**
```bash
git add src/OpcBridge.App/BridgeState.cs
git commit -m "feat(mqtt): raise ValueUpdated event on BridgeState.SetValue"
```

---

## Task 7: App — `BridgeWorker` MQTT wiring

**Files:**
- Modify: `src/OpcBridge.App/BridgeWorker.cs`

**Interfaces:**
- Consumes: `IMqttBridge`, `MqttRuntimeSettings`, `MqttTrafficStore` (Tasks 3, 5), `BridgeState.ValueUpdated` (Task 6), `WriteQueue`/`WriteRequest` (existing), `MqttPayload` (Task 4), `TagMapping.MqttEnabled`/`MqttTopic` (Task 1).
- Produces: runtime MQTT publish/subscribe behavior; `ApplyUaWriteAsync` used by the subscribe callback.

- [ ] **Step 1: Add fields and constructor parameters**

Add `using OpcBridge.Mqtt;` and `using System.Threading.Channels;` near the top of `BridgeWorker.cs`.

Add private fields (near `write_queue_`):
```csharp
    private readonly IMqttBridge mqtt_bridge_;
    private readonly MqttRuntimeSettings mqtt_settings_;
    private readonly MqttTrafficStore mqtt_traffic_;
    private readonly Channel<BridgeValue> mqtt_publish_channel_ = Channel.CreateBounded<BridgeValue>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private HashSet<string> mqtt_enabled_keys_ = new(StringComparer.OrdinalIgnoreCase);
```

Extend the constructor to accept the three new services (add parameters after `ILogger<BridgeWorker> logger`):
```csharp
        IMqttBridge mqttBridge,
        MqttRuntimeSettings mqttSettings,
        MqttTrafficStore mqttTraffic,
```
and assign them:
```csharp
        mqtt_bridge_ = mqttBridge;
        mqtt_settings_ = mqttSettings;
        mqtt_traffic_ = mqttTraffic;
```

- [ ] **Step 2: Set up MQTT in `ExecuteAsync` (after `write_queue_` is created)**

Immediately after the `ua_server_.SetWriteHandler(...)` block (around line 69), add:
```csharp
        mqtt_bridge_.SetMessageSink(OnMqttInboundAsync);
        mqtt_bridge_.StateChanged += state =>
        {
            mqtt_settings_.SetState(state.ToString(), state == MqttConnectionState.Faulted ? "MQTT broker connection failed." : null);
        };
        bridge_state_.ValueUpdated += OnBridgeValueUpdated;
        _ = Task.Run(() => MqttPublishDrainAsync(pollerCts.Token), pollerCts.Token);

        if (mqtt_settings_.GetOptions().Enabled)
        {
            _ = ConnectMqttAsync(pollerCts.Token);
        }
```

- [ ] **Step 3: Maintain the MQTT-enabled key set in the coordinator loop**

In the coordinator loop, where mappings are re-evaluated (`if (mappingVersion != uaMappingVersion)`), after `activeMappings = ...` rebuild the enabled-key set. Add right after `activeMappings = cacheHolder.Cache.GetActiveMappings();` (line 102):
```csharp
                            mqtt_enabled_keys_ = new HashSet<string>(
                                activeMappings.Where(m => m.MqttEnabled).Select(m => NormalizeKey(m.SourceId, m.DaItemId)),
                                StringComparer.OrdinalIgnoreCase);
```
Also do this on the initial setup before the loop (after line 51 `activeMappings = ...`):
```csharp
        mqtt_enabled_keys_ = new HashSet<string>(
            activeMappings.Where(m => m.MqttEnabled).Select(m => NormalizeKey(m.SourceId, m.DaItemId)),
            StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 4: Add the helper methods**

Add these methods to `BridgeWorker` (e.g., after `OnSubscriptionValues`):
```csharp
    private static string NormalizeKey(string sourceId, string daItemId)
    {
        return string.Concat(sourceId.Trim(), "::", daItemId.Trim());
    }

    private void OnBridgeValueUpdated(BridgeValue value)
    {
        if (!mqtt_enabled_keys_.Contains(NormalizeKey(value.SourceId, value.DaItemId)))
        {
            return;
        }

        _ = mqtt_publish_channel_.Writer.WriteAsync(value);
    }

    private async Task ConnectMqttAsync(CancellationToken ct)
    {
        try
        {
            mqtt_settings_.SetState("Connecting");
            await mqtt_bridge_.ConnectAsync(mqtt_settings_.GetOptions(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            mqtt_settings_.SetState("Faulted", ex.Message);
            logger_.LogWarning(ex, "MQTT connect failed");
        }
    }

    private async Task MqttPublishDrainAsync(CancellationToken ct)
    {
        MqttBrokerOptions options = mqtt_settings_.GetOptions();
        await foreach (BridgeValue value in mqtt_publish_channel_.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                string topic = MqttPayload.BuildTopic(options, value.SourceId, value.DaItemId,
                    ResolveMqttTopicOverride(value.SourceId, value.DaItemId));
                string payload = MqttPayload.Serialize(value, options.PayloadFields, ResolveDisplayName(value.SourceId, value.DaItemId));
                await mqtt_bridge_.PublishAsync(topic, payload, ct).ConfigureAwait(false);
                mqtt_settings_.IncrementPublished();
                mqtt_traffic_.Add("PUB", topic, payload);
            }
            catch (Exception ex)
            {
                logger_.LogWarning(ex, "MQTT publish failed for {SourceId}/{ItemId}", value.SourceId, value.DaItemId);
            }
        }
    }

    private async Task OnMqttInboundAsync(MqttInboundMessage message)
    {
        mqtt_settings_.IncrementReceived();
        mqtt_traffic_.Add("SUB", message.Topic, message.RawValue);

        (string? sourceId, string? daItemId) = ResolveTopicToMapping(message.Topic);
        if (sourceId is null || daItemId is null)
        {
            logger_.LogDebug("MQTT inbound topic has no matching mapping: {Topic}", message.Topic);
            return;
        }

        (_ , IReadOnlyList<TagMapping> mappings, _) = mapping_store_.GetSnapshot();
        TagMapping? mapping = mappings.FirstOrDefault(m =>
            string.Equals(m.SourceId, sourceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.DaItemId, daItemId, StringComparison.OrdinalIgnoreCase));
        if (mapping is null || !mapping.MqttEnabled)
        {
            return;
        }

        object? converted = ConvertIncoming(mapping, message.RawValue);
        bool ok = await ApplyUaWriteAsync(sourceId, daItemId, converted, message.TimestampUtc ?? DateTime.UtcNow, CancellationToken.None).ConfigureAwait(false);
        if (!ok)
        {
            logger_.LogWarning("MQTT inbound write rejected for {SourceId}/{ItemId}", sourceId, daItemId);
        }
    }

    private (string? SourceId, string? DaItemId) ResolveTopicToMapping(string topic)
    {
        MqttBrokerOptions options = mqtt_settings_.GetOptions();
        string prefix = (string.IsNullOrWhiteSpace(options.TopicPrefix) ? "bridge/tags" : options.TopicPrefix.Trim().Trim('/')) + "/";
        if (!topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return (null, null);

        string remainder = topic[prefix.Length..];
        int slash = remainder.IndexOf('/');
        if (slash < 0) return (null, null);
        return (remainder[..slash], remainder[(slash + 1)..]);
    }

    private string? ResolveMqttTopicOverride(string sourceId, string daItemId)
    {
        (_ , IReadOnlyList<TagMapping> mappings, _) = mapping_store_.GetSnapshot();
        TagMapping? mapping = mappings.FirstOrDefault(m =>
            string.Equals(m.SourceId, sourceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.DaItemId, daItemId, StringComparison.OrdinalIgnoreCase));
        return mapping?.MqttTopic;
    }

    private string? ResolveDisplayName(string sourceId, string daItemId)
    {
        (_ , IReadOnlyList<TagMapping> mappings, _) = mapping_store_.GetSnapshot();
        TagMapping? mapping = mappings.FirstOrDefault(m =>
            string.Equals(m.SourceId, sourceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.DaItemId, daItemId, StringComparison.OrdinalIgnoreCase));
        return mapping?.DisplayName;
    }

    private static object? ConvertIncoming(TagMapping mapping, string? rawValue)
    {
        if (rawValue is null) return null;
        string text = rawValue.Trim();
        if (string.Equals(mapping.DataType, "String", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (bool.TryParse(text, out bool b)) return b;
        if (long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long l)) return l;
        if (double.TryParse(text, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out double d)) return d;
        return text;
    }

    /// <summary>Write a value through the existing UA write path (WriteQueue → per-source consumer → DA). Same seam a UA client write uses.</summary>
    public async Task<bool> ApplyUaWriteAsync(string sourceId, string daItemId, object? value, DateTime timestampUtc, CancellationToken ct)
    {
        if (write_queue_ is null) return false;

        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await write_queue_.EnqueueAsync(new WriteRequest(sourceId, daItemId, value, tcs), ct).ConfigureAwait(false);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            tcs.TrySetResult(false);
            return false;
        }
    }
```
Note: `mapping_store_` and `logger_` are already in scope. `NormalizeKey` is named differently from `BridgeState.NormalizeKey` (private there) — this is a separate private helper, which is fine.

- [ ] **Step 5: Build to verify**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build src/OpcBridge.App/OpcBridge.App.csproj
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).` (Compile-only verification; runtime MQTT behavior is validated in Task 11 on Windows.)

- [ ] **Step 6: Commit**
```bash
git add src/OpcBridge.App/BridgeWorker.cs
git commit -m "feat(mqtt): wire BridgeWorker publish/subscribe through IMqttBridge and UA write path"
```

---

## Task 8: App — API endpoints in `Program.cs`

**Files:**
- Modify: `src/OpcBridge.App/Program.cs`

**Interfaces:**
- Consumes: `MqttRuntimeSettings`, `MqttTrafficStore`, `IMqttBridge` (Tasks 3, 5), `MqttBrokerOptions`, `MqttPayloadField`, `TagMapping.MqttEnabled`/`MqttTopic` (Task 1).
- Produces: HTTP API for MQTT config/connect/status/logs and MQTT fields on mapping endpoints.

- [ ] **Step 1: Register services + bind config**

In `Program.cs`, after the `builder.Services.Configure<UaServerOptions>(...)` line (line 20), add:
```csharp
builder.Services.Configure<MqttBrokerOptions>(builder.Configuration.GetSection("Mqtt"));
builder.Services.AddSingleton<MqttRuntimeSettings>();
builder.Services.AddSingleton<MqttTrafficStore>();
builder.Services.AddSingleton<IMqttBridge, MqttBridge>();
```

- [ ] **Step 2: Add MQTT endpoints**

Before `app.MapGet("/health", ...)` (line 595), add the following endpoints:
```csharp
app.MapGet("/api/mqtt/config", (MqttRuntimeSettings settings) =>
{
    MqttRuntimeSnapshot snapshot = settings.GetSnapshot();
    return Results.Json(new
    {
        enabled = snapshot.Options.Enabled,
        brokerUrl = snapshot.Options.BrokerUrl,
        clientId = snapshot.Options.ClientId,
        userName = snapshot.Options.UserName,
        password = snapshot.Options.Password,
        tls = snapshot.Options.Tls,
        ignoreCertErrors = snapshot.Options.IgnoreCertErrors,
        topicPrefix = snapshot.Options.TopicPrefix,
        payloadFields = snapshot.Options.PayloadFields.ToString()
    });
});
app.MapPost("/api/mqtt/config", (MqttConfigRequest request, MqttRuntimeSettings settings) =>
{
    MqttBrokerOptions options = settings.GetOptions();
    MqttBrokerOptions updated = new()
    {
        Enabled = request.Enabled,
        BrokerUrl = string.IsNullOrWhiteSpace(request.BrokerUrl) ? options.BrokerUrl : request.BrokerUrl.Trim(),
        ClientId = string.IsNullOrWhiteSpace(request.ClientId) ? options.ClientId : request.ClientId.Trim(),
        UserName = request.UserName,
        Password = request.Password,
        Tls = request.Tls,
        IgnoreCertErrors = request.IgnoreCertErrors,
        TopicPrefix = string.IsNullOrWhiteSpace(request.TopicPrefix) ? options.TopicPrefix : request.TopicPrefix.Trim(),
        PayloadFields = ParsePayloadFields(request.PayloadFields) ?? options.PayloadFields
    };
    settings.UpsertOptions(updated);
    return Results.Json(new { status = "ok" });
});
app.MapPost("/api/mqtt/connect", async (MqttRuntimeSettings settings, IMqttBridge bridge) =>
{
    try
    {
        await bridge.ConnectAsync(settings.GetOptions(), CancellationToken.None);
        return Results.Json(new { status = "ok", state = settings.GetSnapshot().State });
    }
    catch (Exception ex)
    {
        settings.SetState("Faulted", ex.Message);
        return Results.Json(new { status = "error", error = ex.Message });
    }
});
app.MapPost("/api/mqtt/disconnect", async (MqttRuntimeSettings settings, IMqttBridge bridge) =>
{
    await bridge.DisconnectAsync(CancellationToken.None);
    settings.SetState("Disconnected");
    return Results.Json(new { status = "ok" });
});
app.MapGet("/api/mqtt/status", (MqttRuntimeSettings settings) =>
{
    MqttRuntimeSnapshot snapshot = settings.GetSnapshot();
    return Results.Json(new
    {
        state = snapshot.State,
        lastError = snapshot.LastError,
        publishedCount = snapshot.PublishedCount,
        receivedCount = snapshot.ReceivedCount,
        enabled = snapshot.Options.Enabled
    });
});
app.MapGet("/api/mqtt/logs", (MqttTrafficStore traffic, int? limit) =>
{
    IReadOnlyList<MqttTrafficEntry> entries = traffic.GetEntries(limit ?? 200);
    return Results.Json(new
    {
        entries = entries.Select(e => new
        {
            direction = e.Direction,
            topic = e.Topic,
            detail = e.Detail,
            timestampUtc = e.TimestampUtc
        })
    });
});
```

- [ ] **Step 3: Add request DTO + helpers**

Add these near the bottom of `Program.cs` (after the existing `TryParseLogLevel` static method):
```csharp
record MqttConfigRequest(
    bool Enabled,
    string? BrokerUrl,
    string? ClientId,
    string? UserName,
    string? Password,
    bool Tls,
    bool IgnoreCertErrors,
    string? TopicPrefix,
    string? PayloadFields);

static MqttPayloadField? ParsePayloadFields(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return null;
    return Enum.TryParse<MqttPayloadField>(value.Trim(), ignoreCase: true, out MqttPayloadField result)
        ? result
        : null;
}
```

- [ ] **Step 4: Carry MQTT fields on mapping endpoints**

In `MapPost("/api/mappings/add")` (the `Select` at lines 274-290) and `/api/mappings/update` (lines 330-344), add to the `new TagMapping { ... }` initializer:
```csharp
        MqttEnabled = tag.MqttEnabled ?? false,
        MqttTopic = string.IsNullOrWhiteSpace(tag.MqttTopic) ? null : tag.MqttTopic
```
In `/api/mappings/bulk-add` (lines 302-317) add the same two lines. (These come through as camelCase `mqttEnabled`/`mqttTopic` from the dashboard in Task 10.)

Also the mapping rows returned by `GET /api/mappings` already serialize the full `TagMapping` (which now includes `MqttEnabled`/`MqttTopic`), so no change is needed there.

- [ ] **Step 5: Build to verify**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build OpcDaToUaBridge.sln
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).`

- [ ] **Step 6: Commit**
```bash
git add src/OpcBridge.App/Program.cs
git commit -m "feat(mqtt): add MQTT config/connect/status/logs API endpoints"
```

---

## Task 9: Dashboard — MQTT tab + mapping MQTT fields

**Files:**
- Modify: `src/OpcBridge.App/DashboardPage.cs`

**Interfaces:**
- Consumes: `/api/mqtt/*` (Task 8), `/api/mappings` (now carries `mqttEnabled`/`mqttTopic`, Task 2).
- Produces: a new "MQTT" tab (broker form, connect/disconnect, status, traffic monitor) and MQTT fields in the Mappings faceplate + mapping row badge.

This file is a single raw-string HTML (`DashboardPage.Html`). Edits are string insertions; match the existing CSS/JS conventions (helpers `esc`, `attr`, `el`, `fetch`, `showTab`).

- [ ] **Step 1: Add the tab button**

After the `Logs` tab button (line 238), add:
```html
    <button class="tabbtn" data-tab="mqtt" onclick="showTab('mqtt')">MQTT</button>
```

- [ ] **Step 2: Add the `loadMqtt` / `saveMqtt` / `connectMqtt` / `disconnectMqtt` JS**

Add these functions near `loadMappings` (after line 1192):
```javascript
async function loadMqtt() {
    try {
        const cfg = await (await fetch('/api/mqtt/config', { cache: 'no-store' })).json();
        el('mqttEnabled').checked = !!cfg.enabled;
        el('mqttBrokerUrl').value = cfg.brokerUrl || '';
        el('mqttClientId').value = cfg.clientId || '';
        el('mqttUser').value = cfg.userName || '';
        el('mqttPass').value = cfg.password || '';
        el('mqttTls').checked = !!cfg.tls;
        el('mqttIgnoreCert').checked = !!cfg.ignoreCertErrors;
        el('mqttPrefix').value = cfg.topicPrefix || 'bridge/tags';
        el('mqttFields').value = cfg.payloadFields || 'Value, Timestamp';
        const st = await (await fetch('/api/mqtt/status', { cache: 'no-store' })).json();
        el('mqttState').textContent = st.state || 'Disconnected';
        el('mqttState').className = 'v ' + (st.state === 'Connected' ? 'badge good' : 'badge bad');
        el('mqttLastError').textContent = st.lastError || 'No errors';
        el('mqttPublished').textContent = String(st.publishedCount || 0);
        el('mqttReceived').textContent = String(st.receivedCount || 0);
    } catch (e) { el('mqttMessage').textContent = '✗ ' + e.message; }
}
async function saveMqtt() {
    const body = {
        enabled: el('mqttEnabled').checked,
        brokerUrl: el('mqttBrokerUrl').value.trim(),
        clientId: el('mqttClientId').value.trim(),
        userName: el('mqttUser').value.trim() || null,
        password: el('mqttPass').value || null,
        tls: el('mqttTls').checked,
        ignoreCertErrors: el('mqttIgnoreCert').checked,
        topicPrefix: el('mqttPrefix').value.trim(),
        payloadFields: el('mqttFields').value.trim()
    };
    const r = await fetch('/api/mqtt/config', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const p = await r.json();
    el('mqttMessage').textContent = p.status === 'ok' ? 'MQTT config saved.' : ('✗ ' + (p.error || 'save failed'));
    await loadMqtt();
}
async function connectMqtt() {
    el('mqttMessage').textContent = 'Connecting...';
    const r = await fetch('/api/mqtt/connect', { method: 'POST' });
    const p = await r.json();
    el('mqttMessage').textContent = p.status === 'ok' ? 'Connected.' : ('✗ ' + (p.error || 'connect failed'));
    await loadMqtt();
}
async function disconnectMqtt() {
    await fetch('/api/mqtt/disconnect', { method: 'POST' });
    el('mqttMessage').textContent = 'Disconnected.';
    await loadMqtt();
}
async function loadMqttLogs() {
    try {
        const p = await (await fetch('/api/mqtt/logs?limit=200', { cache: 'no-store' })).json();
        el('mqttTraffic').innerHTML = (p.entries || []).map(e =>
            `<div class="li"><span class="badge ${e.direction === 'PUB' ? 'good' : 'partial'}">${esc(e.direction)}</span>` +
            `<span class="mono">${esc(e.topic)}</span>` +
            `<span class="p">${esc(e.detail || '')}</span>` +
            `<span class="s">${esc(new Date(e.timestampUtc).toLocaleTimeString())}</span></div>`).join('') ||
            '<span class="msg">No MQTT traffic yet.</span>';
    } catch (e) { el('mqttTraffic').innerHTML = '<span class="msg">✗ ' + esc(e.message) + '</span>'; }
}
```

- [ ] **Step 3: Wire `loadMqtt`/`loadMqttLogs` into `showTab`**

In `showTab` (lines 712-721), add after the `help` line:
```javascript
    if (name === 'mqtt') { await loadMqtt(); await loadMqttLogs(); }
```

- [ ] **Step 4: Add the MQTT view markup**

Add a new view `<div class="view" id="view-mqtt">` (place it right after the closing `</div>` of the `view-help` or any view; insert before `</div>` that closes `.content`). Minimal structure:
```html
<div class="view" id="view-mqtt">
    <div class="grid2">
        <div class="box">
            <div class="box-h">MQTT Broker</div>
            <div class="box-b">
                <div class="field"><label class="fl">Enabled</label><input type="checkbox" id="mqttEnabled"></div>
                <div class="field"><label class="fl">Broker URL</label><input type="text" id="mqttBrokerUrl" placeholder="tcp://localhost:1883"></div>
                <div class="field"><label class="fl">Client ID</label><input type="text" id="mqttClientId"></div>
                <div class="field"><label class="fl">Username</label><input type="text" id="mqttUser"></div>
                <div class="field"><label class="fl">Password</label><input type="password" id="mqttPass"></div>
                <div class="field"><label class="fl">TLS</label><input type="checkbox" id="mqttTls"></div>
                <div class="field"><label class="fl">Ignore Cert</label><input type="checkbox" id="mqttIgnoreCert"></div>
                <div class="field"><label class="fl">Topic Prefix</label><input type="text" id="mqttPrefix" placeholder="bridge/tags"></div>
                <div class="field"><label class="fl">Payload Fields</label>
                    <select id="mqttFields">
                        <option>Value, Timestamp</option>
                        <option>Value, Timestamp, Quality</option>
                        <option>Value, Timestamp, Quality, SourceId, ItemId</option>
                        <option>Value, Timestamp, SourceId, ItemId, DisplayName, DataType</option>
                    </select>
                </div>
                <div class="field">
                    <button class="btn" onclick="saveMqtt()">Save Config</button>
                    <button class="btn ghost" onclick="connectMqtt()">Connect</button>
                    <button class="btn ghost" onclick="disconnectMqtt()">Disconnect</button>
                </div>
                <div class="msg" id="mqttMessage"></div>
            </div>
        </div>
        <div class="box">
            <div class="box-h">Connection</div>
            <div class="box-b">
                <div class="stat"><div class="k">State</div><div class="v" id="mqttState">Disconnected</div><div class="s" id="mqttLastError">No errors</div></div>
                <div class="stat"><div class="k">Published</div><div class="v" id="mqttPublished">0</div></div>
                <div class="stat"><div class="k">Received</div><div class="v" id="mqttReceived">0</div></div>
            </div>
        </div>
    </div>
    <div class="box" style="margin-top:14px">
        <div class="box-h">Traffic Monitor <span class="msg" style="margin-left:auto"><button class="btn ghost" onclick="loadMqttLogs()">Refresh</button></span></div>
        <div class="box-b"><div class="list" id="mqttTraffic"><span class="msg">No MQTT traffic yet.</span></div></div>
    </div>
</div>
```

- [ ] **Step 5: Add MQTT fields to the Mappings faceplate**

In `openFaceplate` (around lines 651-669) add, after `el('fpDeadband').value = ...`:
```javascript
    el('fpMqttEnabled').checked = (mapping.mqttEnabled ?? mapping.MqttEnabled) === true;
    el('fpMqttTopic').value = String(mapping.mqttTopic ?? mapping.MqttTopic ?? '');
```
In `updateMapping` (the `payload` object, lines 1251-1265), add:
```javascript
        mqttEnabled: el('fpMqttEnabled').checked,
        mqttTopic: el('fpMqttTopic').value.trim() || null,
```

- [ ] **Step 6: Add MQTT inputs to the faceplate markup**

Find the faceplate markup (the `<input id="fpDeadband">` element) and add after it:
```html
<div class="field"><label class="fl">MQTT</label><input type="checkbox" id="fpMqttEnabled"> <span class="msg">publish/subscribe this tag</span></div>
<div class="field"><label class="fl">MQTT Topic</label><input type="text" id="fpMqttTopic" placeholder="override topic (optional)"></div>
```

- [ ] **Step 7: Add an MQTT badge to mapping rows**

In `renderMappingRow` (line 629 return), append an MQTT indicator inside the badge cluster. Change the `accessBadge` line to also include:
```javascript
    const mqttOn = (mapping.mqttEnabled ?? mapping.MqttEnabled) === true;
    const mqttBadge = mqttOn ? `<span class="pill" style="padding:1px 6px;font-size:10px">MQTT</span>` : '';
```
and change the final `return` to include `${mqttBadge}` alongside the other badges.

- [ ] **Step 8: Build + manual dashboard smoke test**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build OpcDaToUaBridge.sln
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).`

Manual (Windows host): start the app, open the MQTT tab, confirm the form loads, Save/Connect transitions state to `Connected` against a local `mosquitto` broker.

- [ ] **Step 9: Commit**
```bash
git add src/OpcBridge.App/DashboardPage.cs
git commit -m "feat(mqtt): add MQTT dashboard tab, traffic monitor, and per-mapping MQTT toggles"
```

---

## Task 10: App — `appsettings.json` Mqtt section + Help docs

**Files:**
- Modify: `src/OpcBridge.App/appsettings.json`
- Modify: `src/OpcBridge.App/HelpContent.cs`

**Interfaces:**
- Consumes: `MqttBrokerOptions` fields (Task 1).
- Produces: seeded default broker config and user-facing MQTT documentation.

- [ ] **Step 1: Add the `Mqtt` section to `appsettings.json`**

In `src/OpcBridge.App/appsettings.json`, add a top-level `Mqtt` section (after the `Bridge` section's closing `}` at line 48, before the final `}`):
```json
  ,
  "Mqtt": {
    "Enabled": false,
    "BrokerUrl": "tcp://localhost:1883",
    "ClientId": "OpcDaToUaBridge",
    "UserName": null,
    "Password": null,
    "Tls": false,
    "IgnoreCertErrors": false,
    "TopicPrefix": "bridge/tags",
    "PayloadFields": "Value, Timestamp"
  }
```

- [ ] **Step 2: Document MQTT in `HelpContent.Markdown`**

Add an MQTT section near the "OPC UA Server" section (after line 251, before "## Subscriptions & Deadband"):
```markdown
---

# MQTT (OPC UA ↔ Broker)

The bridge can publish OPC UA tag values to an MQTT broker and accept writes from it. MQTT is scoped to the **OPC UA layer** — it reads the mirrored UA tag values and writes through the same UA write path a UA client uses.

## Topics

- Publish: `{TopicPrefix}/{SourceId}/{DaItemId}` (default prefix `bridge/tags`), or a per-tag `MqttTopic` override set in the tag faceplate.
- Subscribe: the bridge subscribes to `{TopicPrefix}/#` and resolves inbound topics to tags the same way.

## Payload

Minimal JSON. Selectable fields (Broker tab → Payload Fields): `v` (value), `t` (timestamp), `q` (quality), `sourceId`, `itemId`, `displayName`, `dataType`. Default is `v` + `t`.

```json
{ "v": 12.3, "t": "2026-07-08T12:00:00.0000000Z" }
```

## Enable a tag

Open a tag's faceplate (Tags tab) → check **MQTT** to publish and accept inbound writes for that tag. Set **MQTT Topic** to override the auto topic.

## Broker connection

MQTT tab → enter Broker URL (`tcp://host:port` or `mqtts://host:port`), optional credentials, TLS + Ignore Cert (dev), topic prefix, and payload fields. Save, then Connect. Connection state and a live traffic monitor are shown in the tab. Config persists to `mqtt.json`.

## Notes

- Publish and subscribe are failure-resilient: a broker outage does not stop the bridge; the client auto-reconnects.
- Inbound writes to a tag flow through the UA write path (same as a UA client write) and are rejected if the tag is read-only.
```

- [ ] **Step 3: Build to verify**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build OpcDaToUaBridge.sln
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).`

- [ ] **Step 4: Commit**
```bash
git add src/OpcBridge.App/appsettings.json src/OpcBridge.App/HelpContent.cs
git commit -m "docs(mqtt): add Mqtt appsettings section and MQTT help documentation"
```

---

## Task 11: End-to-end build + manual runtime verification

**Files:** none (verification only).

**Interfaces:** Validates the whole feature.

- [ ] **Step 1: Clean build on Linux (Docker)**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build OpcDaToUaBridge.sln
```
Expected: `Build succeeded. 0 Warning(s), 0 Error(s).`

- [ ] **Step 2: Clean build on Windows (user-profile dotnet)**

On the Windows host (manual):
```powershell
"%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" build OpcDaToUaBridge.sln
```
Expected: `0 Warning(s), 0 Error(s)`.

- [ ] **Step 3: Manual runtime round-trip (Windows host + local broker)**

1. Start a local MQTT broker (e.g. `mosquitto -c mosquitto.conf` with a listener on 1883).
2. Start the bridge; open the dashboard → **MQTT** tab.
3. Enable MQTT: set Broker URL `tcp://localhost:1883`, Save, Connect → state becomes **Connected**.
4. Open a tag faceplate → check **MQTT**. Subscribe a listener: `mosquitto_sub -t 'bridge/tags/#'` → confirm published JSON `{ "v": ..., "t": ... }` at the tag's update rate.
5. Publish a value: `mosquitto_pub -t 'bridge/tags/{source}/{item}' -m '{"v":42}'` → confirm it writes through the UA path (visible in Live Values / a UA client) for a writeable tag, and is ignored for read-only/unmapped topics.
6. Toggle Payload Fields to include `Quality`/`sourceId` → confirm payload shape changes; default remains `{v,t}`.
7. Stop the broker → app stays alive, state shows Faulted/Disconnected, auto-reconnects on restart.
8. `GET /health` → `{"status":"ok"}`; `GET /api/mqtt/status` → correct counters/state.

- [ ] **Step 4: Commit (if any verification fixes were needed)**

Only if fixes were required:
```bash
git add -A
git commit -m "fix(mqtt): address build/runtime issues found during verification"
```
If no fixes were needed, skip this step.

---

## Self-review notes (author)

- Spec coverage: every spec section (both-direction, prefix+path, per-mapping flag, runtime+persisted, TLS+auth, new Mqtt project, minimal JSON payload with field selector, BridgeState UA-mirror source, UA write path for subscribe-in, MQTT tab + Mappings-tab toggles, error resilience) maps to a task.
- Payload field names in the `MqttPayloadField` enum match the dashboard select options (`Value, Timestamp`, etc.) via `Enum.ToString()` / `Enum.TryParse`.
- `ApplyUaWriteAsync` reuses `WriteQueue`/`WriteRequest` exactly as the existing UA write handler (the `SetWriteHandler` lambda in `BridgeWorker.ExecuteAsync`), so subscribe-in is genuinely the UA write path.
- Topic resolution for subscribe-in strips the configured prefix and splits on the first `/`; per-mapping `MqttTopic` overrides are honored on publish (`ResolveMqttTopicOverride`).
- No DA/COM reference is introduced into `OpcBridge.Mqtt` (project reference is Core only).
