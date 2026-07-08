# MQTT Integration Design — OpcDaToUaBridge

- **Date:** 2026-07-08
- **Branch:** `feature/MQTT-implement` (git worktree at `.worktrees/MQTT-implement`, forked from `main`)
- **Status:** Approved design, pending implementation plan

## 1. Goal

Add MQTT support to the bridge as a peer of the OPC UA layer, plus a new **MQTT tab** in the web dashboard.

- **Publish-out:** emit OPC UA tag values to an MQTT broker.
- **Subscribe-in:** receive values from the broker and write them into OPC UA tags (via the existing UA write path).

MQTT is scoped to the **OPC UA layer only** — it consumes the already-mirrored UA tag values and writes through the UA write seam. It never touches `OpcBridge.Da` or DCOM directly.

## 2. Decisions (from brainstorming)

| Topic | Decision |
|---|---|
| Direction | Both: publish-out + subscribe-in |
| Topic scheme | Prefix + path: `{TopicPrefix}/{SourceId}/{DaItemId}`, or per-mapping `MqttTopic` override |
| Tag scope | Per-mapping flag: `MqttEnabled` + optional `MqttTopic` on `TagMapping` |
| Broker config | Runtime + persisted (`MqttRuntimeSettings`, seeded from `appsettings` `Mqtt:`, persisted to `mqtt.json`) |
| Security | TLS + auth: `tcp://` and `mqtts://`, optional username/password, `IgnoreCertErrors` for dev |
| Library | `MQTTnet` (~v4.x), `ManagedMqttClient` for auto-reconnect |
| Dashboard tab | Broker config form + TLS/ignore-cert + topic prefix + payload-field checkboxes + enabled + Connect/Disconnect + status/last-error + live traffic monitor. Per-mapping `MqttEnabled`/`MqttTopic` toggles live in the existing **Mappings tab** |
| Payload | Minimal JSON, **field-selectable**, default `{ "v": <value>, "t": <iso8601> }`. Selectable fields: `value` (v), `timestamp` (t), `quality` (q), `sourceId`, `itemId`, `displayName`, `dataType` |
| Publish source | `BridgeState` UA mirror (no DA/DCOM coupling) |
| Subscribe-in target | UA write path (`WriteQueue` / `OnWriteValue`, same seam UA clients use) |
| Architecture | New `OpcBridge.Mqtt` project, peer of `Da`/`Ua` |

## 3. Architecture

New project `OpcBridge.Mqtt` (`net8.0`). Reference graph:

```
App → {Core, Da, Ua, Mqtt}
Mqtt → Core          (Mqtt depends only on Core, never on Da)
Da → Core
Ua → Core
```

`OpcBridge.App` remains the composition root: `BridgeWorker` wires `IMqttBridge` into the existing value pipeline; `Program.cs` adds the MQTT API; `DashboardPage` adds the tab.

## 4. Components

### 4.1 `OpcBridge.Core` (contract additions)

- `MqttBrokerOptions` (record/class):
  - `Enabled` (bool)
  - `BrokerUrl` (string, `tcp://host:port` or `mqtts://host:port`)
  - `ClientId` (string)
  - `UserName` (string?, optional)
  - `Password` (string?, optional)
  - `Tls` (bool) — implied by `mqtts://` but explicit toggle for clarity
  - `IgnoreCertErrors` (bool) — dev convenience, loose validation when true
  - `TopicPrefix` (string, default `"bridge/tags"`)
  - `PayloadFields` (flags enum `MqttPayloadField`: `Value | Timestamp | Quality | SourceId | ItemId | DisplayName | DataType`; default `Value | Timestamp`)
  - `PublishRateLimitPerSec` (optional, int?) — cap publish throughput; if omitted, unbounded (coalescing only).
- Extend `TagMapping` with:
  - `MqttEnabled` (bool, default false)
  - `MqttTopic` (string?, optional explicit topic override)

No other Core types change. `BridgeValue` already carries everything needed (`SourceId`, `DaItemId`, `Value`, `TimestampUtc`, `DaQuality`, `IsGood`).

### 4.2 `OpcBridge.Mqtt`

- `IMqttBridge` (seam):
  - `Task ConnectAsync(MqttBrokerOptions options, CancellationToken ct)`
  - `Task DisconnectAsync(CancellationToken ct)`
  - `Task PublishAsync(string topic, string payload, CancellationToken ct)`
  - `void SetPublishSink(Func<MqttInboundMessage, Task> onMessage)` — callback for subscribe-in
  - `MqttConnectionState State` (property, observable)
  - `IAsyncDisposable`
- `MqttBridge : IMqttBridge` — wraps `MQTTnet` `ManagedMqttClient`:
  - Handles TLS (`mqtts://` / `Tls=true`) and username/password.
  - `IgnoreCertErrors=true` → `ClientOptions.ValidateServerCertificate = false` (dev only).
  - `ManagedMqttClient` provides automatic reconnect with backoff.
  - On connect, subscribes to `{TopicPrefix}/#`.
- `MqttMessage` / `MqttInboundMessage` records: `Topic`, `Payload`, plus parsed `SourceId`/`DaItemId`/`Value`/`TimestampUtc` after resolution.
- Topic builder: `BuildTopic(options, sourceId, itemId)` → `MqttTopic` override if set, else `{TopicPrefix}/{SourceId}/{DaItemId}`.
- Payload serializer: `Serialize(BridgeValue, PayloadFields)` → minimal JSON containing only selected fields:
  - `v` = value (always available when `Value` selected)
  - `t` = `TimestampUtc` ISO-8601
  - `q` = quality string (derive from `IsGood`/`DaQuality`)
  - `sourceId`, `itemId`, `displayName`, `dataType` as selected.
- Payload parser (subscribe-in): `Parse(payload)` → loose JSON or raw-value detection; maps to `MqttInboundMessage`.

### 4.3 `OpcBridge.App`

- **`MqttRuntimeSettings`** (singleton, mirrors `DaRuntimeSettings`):
  - Holds current `MqttBrokerOptions` + `MqttConnectionState` + `LastError`.
  - Seeded once from `appsettings` `Mqtt:` at startup.
  - `UpsertOptions`, `SetState`, `SetLastError`.
  - **Persisted to `mqtt.json`** in `AppContext.BaseDirectory` (same pattern as `mappings.json`); reload on change.
- **`BridgeWorker` integration:**
  - On startup, if `MqttBrokerOptions.Enabled`, connect via `IMqttBridge`.
  - **Publish-out:** subscribe to `BridgeState` value-update events. For each updated `BridgeValue` whose mapping `MqttEnabled`, build topic + payload and enqueue to a **bounded `Channel<MqttPublishItem>`** (capacity ~1024, drop-oldest on overflow). A dedicated publisher task drains the channel → `IMqttBridge.PublishAsync`. This keeps pollers non-blocking.
    - Coalescing: if a tag updates faster than publish drain, keep only the latest queued item per `(SourceId, DaItemId)` to avoid backlog (optional; can be gated by `PublishRateLimitPerSec`).
  - **Subscribe-in:** `IMqttBridge.SetPublishSink` callback parses the message, resolves topic → `TagMapping` (match `MqttTopic` override or `{prefix}/{source}/{item}`), and if `MqttEnabled`, invokes the **UA write path** (`BridgeNodeManager` write / `WriteQueue`) — the same seam a UA client write uses. Unmapped/disabled/unknown topics are logged and ignored.
- **API endpoints (Program.cs):**
  - `GET /api/mqtt/config` — current `MqttBrokerOptions` + state
  - `POST /api/mqtt/config` — upsert `MqttBrokerOptions` (persists to `mqtt.json`)
  - `POST /api/mqtt/connect` / `POST /api/mqtt/disconnect`
  - `GET /api/mqtt/status` — connection state + last error + published/received counters
  - `GET /api/mqtt/logs?limit=` — traffic monitor ring buffer (published + received messages, ~500 entries)
  - Mapping endpoints (`/api/mappings/add|update`) gain `mqttEnabled` / `mqttTopic` fields; `GET /api/mappings` returns them.
- **Dashboard (`DashboardPage.FullHtml`):**
  - New **MQTT tab**: broker config form (URL, client ID, username, password, TLS toggle, ignore-cert toggle, topic prefix, payload-field checkboxes, enabled), Connect/Disconnect buttons, connection status + last-error, and a **live message traffic monitor** (published & received) fed by `/api/mqtt/logs`.
  - Existing **Mappings tab**: add `MqttEnabled` checkbox + `MqttTopic` text input per mapping row (reuse the mapping UI rendering pattern).
- **`HelpContent`**: document MQTT broker config, topic scheme, payload fields, and the new tab.
- **`appsettings.json`**: add `Mqtt:` section (seeded defaults: disabled, `tcp://localhost:1883`, prefix `bridge/tags`, default payload fields).

## 5. Data flow

**Publish-out:**
```
DA poller → BridgeState (UA mirror) ──value-update event──► bounded Channel
                                                                    │
                                                                    ▼
                                                          MqttBridge.PublishAsync → broker
```
MQTT reads the `BridgeState` UA mirror only; no `OpcDaClient`/DCOM access.

**Subscribe-in:**
```
broker → MqttBridge callback → parse + resolve topic→mapping
                                          │ (if MqttEnabled)
                                          ▼
                              UA write path (WriteQueue → OnWriteValue → DA if writeable)
```

## 6. Error handling (failure-resilient, consistent with DA)

- **Connect failure:** surfaced in dashboard status + `LastError`; app stays alive. `ManagedMqttClient` auto-reconnects with backoff.
- **Publish failure / overflow:** logged and dropped at the bounded channel; never blocks pollers.
- **Subscribe-in to unknown/disabled topic:** logged and ignored.
- **UA write reject:** reuses existing `BadNoCommunication` / `BadRequestTimeout` handling from the write path.
- Broker config errors (bad URL, auth fail) are non-fatal and shown in the tab.

## 7. Build & conventions

- **Zero-warning build bar** on both platforms (Linux Docker SDK + Windows user-profile dotnet). MQTTnet is cross-platform, so no CA1416 platform guards expected; add `[SupportedOSPlatform]` only if needed.
- Per-mapping, single conventional-commit style (`feat(mqtt): ...`).
- Persisted runtime file `mqtt.json` lives in `BaseDirectory`; preserve during deploy cutover (like `mappings.json`).
- No new test project (per repo convention).

## 8. Verification

- Clean build on Linux (Docker `dotnet build OpcDaToUaBridge.sln`) and Windows (user-profile dotnet).
- Manual runtime on Windows host with a local broker (e.g. `mosquitto`):
  - Enable MQTT, connect → status `Connected`.
  - Verify published messages on `bridge/tags/{source}/{item}` with selected payload fields.
  - Publish a value to a topic for an `MqttEnabled` mapping → confirm it writes through the UA path (visible in Live Values / UA client).
  - Toggle payload fields → confirm payload shape changes; default `{v,t}`.
  - Disconnect / bad broker → app stays alive, status reflects error.
- `GET /health` and `GET /api/mqtt/status` respond correctly.

## 9. Out of scope (YAGNI)

- MQTT→DA direct writes (subscribe-in uses the UA write path only).
- Retained-message / will-message policy tuning (use MQTTnet defaults; revisit if needed).
- Schema registry / binary payloads (JSON only).
- Multiple simultaneous broker connections (single broker per instance).
