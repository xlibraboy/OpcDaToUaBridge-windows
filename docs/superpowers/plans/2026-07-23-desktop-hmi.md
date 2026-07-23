# Desktop Operator HMI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose a LAN HMI API (REST snapshot + write + SignalR deltas) on the existing bridge Web Host, and ship a separate Avalonia Operator HMI (tag list + faceplate + writes).

**Architecture:** Keep `OpcBridge.App` as the factory process on `:8080`. Add `OpcBridge.Client` wire DTOs, `/api/hmi/*` endpoints, and SignalR hub `/hmi` fed from `BridgeState.ValueUpdated` + `MappingStore` version changes. New process `OpcBridge.Hmi` (Avalonia 11) talks only HTTP/SignalR — never COM/UA. Trends/Influx are out of scope (v1.1).

**Tech Stack:** .NET 8, ASP.NET Core minimal API + SignalR, Avalonia 11, xUnit (`tests/OpcBridge.LoadTest`), Docker SDK builds.

**Spec:** `docs/superpowers/specs/2026-07-23-desktop-hmi-design.md`

## Global Constraints

- Work only in worktree `/home/autoinst578/OpcDaToUaBridge/.worktrees/feature-desktop-hmi` on branch `feature/desktop-hmi`.
- Port stays **`http://0.0.0.0:8080`** — do not add port 5000.
- Write gate is **`TagMapping.Writeable == true`** (same as UA `OnWriteValue`), plus mapping must exist and `Enabled == true`.
- Writes must go through existing `BridgeWorker.ApplyUaWriteAsync` / `WriteQueue` — no second DA write path.
- Snapshot is REST-only (`GET /api/hmi/tags`); SignalR hub publishes **deltas only** (`values`) and **`mappingsChanged`**.
- HMI must never reference `OpcBridge.Da`, `OpcBridge.Ua`, or COM.
- Zero-warning / zero-error build bar. Linux Docker:
  ```bash
  docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
    dotnet build OpcDaToUaBridge.sln -c Release
  ```
- Tests:
  ```bash
  docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
    dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release
  ```
- Conventional commits: `feat(hmi):`, `test(hmi):`, `chore(hmi):`.
- Do not implement Influx/trends, auth, Android, or config UI in Avalonia.

## File map

| Path | Responsibility |
|---|---|
| `src/OpcBridge.Client/*` | Wire DTOs shared by App + HMI |
| `src/OpcBridge.App/Hmi/*` | Snapshot builder, hub, broadcast hosted service |
| `src/OpcBridge.App/Program.cs` | DI + Map hub + `/api/hmi/*` |
| `src/OpcBridge.App/MappingStore.cs` | `Changed` event on version bump |
| `src/OpcBridge.App/BridgeWorker.cs` | `TryHmiWriteAsync` gate + convert + write |
| `src/OpcBridge.Hmi/*` | Avalonia operator app |
| `tests/OpcBridge.LoadTest/HmiApiTests.cs` | REST snapshot + write gate tests |
| `OpcDaToUaBridge.sln` | Add Client + Hmi (+ Mqtt if still missing) |

---

### Task 1: `OpcBridge.Client` DTOs + solution wiring

**Files:**
- Create: `src/OpcBridge.Client/OpcBridge.Client.csproj`
- Create: `src/OpcBridge.Client/HmiTagDto.cs`
- Create: `src/OpcBridge.Client/HmiTagsResponse.cs`
- Create: `src/OpcBridge.Client/HmiWriteRequest.cs`
- Create: `src/OpcBridge.Client/HmiWriteResponse.cs`
- Create: `src/OpcBridge.Client/HmiValueDelta.cs`
- Create: `src/OpcBridge.Client/HmiMappingsChanged.cs`
- Modify: `OpcDaToUaBridge.sln` (via `dotnet sln add`)
- Modify: `src/OpcBridge.App/OpcBridge.App.csproj` (project reference)

**Interfaces:**
- Produces: DTOs used by Tasks 3–8 (names below are final).

- [ ] **Step 1: Create Client project file**

`src/OpcBridge.Client/OpcBridge.Client.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Add DTO types**

`src/OpcBridge.Client/HmiTagDto.cs`:
```csharp
namespace OpcBridge.Client;

public sealed class HmiTagDto
{
    public string SourceId { get; set; } = string.Empty;
    public string DaItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataType { get; set; } = "Double";
    public object? Value { get; set; }
    public DateTime? TimestampUtc { get; set; }
    public int? DaQuality { get; set; }
    public bool? IsGood { get; set; }
    public bool Writeable { get; set; }
}
```

`src/OpcBridge.Client/HmiTagsResponse.cs`:
```csharp
namespace OpcBridge.Client;

public sealed class HmiTagsResponse
{
    public long Version { get; set; }
    public IReadOnlyList<HmiTagDto> Tags { get; set; } = Array.Empty<HmiTagDto>();
}
```

`src/OpcBridge.Client/HmiWriteRequest.cs`:
```csharp
namespace OpcBridge.Client;

public sealed class HmiWriteRequest
{
    public string SourceId { get; set; } = string.Empty;
    public string DaItemId { get; set; } = string.Empty;
    public object? Value { get; set; }
}
```

`src/OpcBridge.Client/HmiWriteResponse.cs`:
```csharp
namespace OpcBridge.Client;

public sealed class HmiWriteResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
}
```

`src/OpcBridge.Client/HmiValueDelta.cs`:
```csharp
namespace OpcBridge.Client;

public sealed class HmiValueDelta
{
    public string SourceId { get; set; } = string.Empty;
    public string DaItemId { get; set; } = string.Empty;
    public object? Value { get; set; }
    public DateTime TimestampUtc { get; set; }
    public int DaQuality { get; set; }
    public bool IsGood { get; set; }
}
```

`src/OpcBridge.Client/HmiMappingsChanged.cs`:
```csharp
namespace OpcBridge.Client;

public sealed class HmiMappingsChanged
{
    public long Version { get; set; }
}
```

- [ ] **Step 3: Add projects to solution and App reference**

From worktree root:
```bash
dotnet sln OpcDaToUaBridge.sln add src/OpcBridge.Client/OpcBridge.Client.csproj
# If Mqtt is still missing from the solution file:
dotnet sln OpcDaToUaBridge.sln add src/OpcBridge.Mqtt/OpcBridge.Mqtt.csproj
```

In `src/OpcBridge.App/OpcBridge.App.csproj` ItemGroup of ProjectReferences, add:
```xml
    <ProjectReference Include="..\OpcBridge.Client\OpcBridge.Client.csproj" />
```

- [ ] **Step 4: Build Client**

```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet build src/OpcBridge.Client/OpcBridge.Client.csproj -c Release
```
Expected: `0 Warning(s), 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.Client OpcDaToUaBridge.sln src/OpcBridge.App/OpcBridge.App.csproj
git commit -m "feat(hmi): add OpcBridge.Client wire DTOs"
```

---

### Task 2: `MappingStore.Changed` event

**Files:**
- Modify: `src/OpcBridge.App/MappingStore.cs`

**Interfaces:**
- Produces: `event Action<long>? Changed` invoked with the new version after every successful mutation that increments `version_`.

- [ ] **Step 1: Add event field**

Near the top of `MappingStore` (after fields):
```csharp
    public event Action<long>? Changed;
```

- [ ] **Step 2: Raise after every `version_++` that persists**

In each mutation method that does `version_++` then `Persist()` (`Add`, `TryUpdate`, `Remove`, `RemoveSource`, `SetAll`), after the version increment and persist, capture version and raise outside the lock when possible.

Pattern for methods that already hold `lock (sync_)`:
```csharp
            long raisedVersion = 0;
            bool raise = false;
            lock (sync_)
            {
                // ... existing mutation ...
                if (changed) // or always on success path that incremented
                {
                    version_++;
                    Persist();
                    raisedVersion = version_;
                    raise = true;
                }
                // return as today
            }
            if (raise)
            {
                Changed?.Invoke(raisedVersion);
            }
```

Apply carefully so existing return values stay identical. For `TryUpdate` / `Remove` / etc., raise only when version actually increments.

- [ ] **Step 3: Build App**

```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet build src/OpcBridge.App/OpcBridge.App.csproj -c Release
```
Expected: `0 Warning(s), 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/OpcBridge.App/MappingStore.cs
git commit -m "feat(hmi): raise MappingStore.Changed on version bump"
```

---

### Task 3: HMI tag snapshot + `GET /api/hmi/tags`

**Files:**
- Create: `src/OpcBridge.App/Hmi/HmiTagSnapshot.cs`
- Modify: `src/OpcBridge.App/Program.cs`
- Create: `tests/OpcBridge.LoadTest/HmiApiTests.cs`

**Interfaces:**
- Consumes: `MappingStore.GetSnapshot()`, `BridgeState.GetValues()`, `HmiTagsResponse` / `HmiTagDto`
- Produces: `GET /api/hmi/tags` → `HmiTagsResponse` JSON (camelCase via default ASP.NET JSON)

- [ ] **Step 1: Write failing API test**

Create `tests/OpcBridge.LoadTest/HmiApiTests.cs`:
```csharp
using System.Net;
using System.Text.Json;
using Xunit;

namespace OpcBridge.LoadTest;

[Collection(nameof(DaLinkApiAppCollection))]
public sealed class HmiApiTests
{
    private static void WriteAppsettings(string dir, object? mappings = null)
    {
        var appsettings = new
        {
            Da = new { ProgId = "Matrikon.OPC.Simulation.1", Host = "localhost", UpdateRateMs = 1000, UseSubscriptions = true },
            Ua = new { ApplicationName = "OpcDaToUaBridge", EndpointUrl = "opc.tcp://0.0.0.0:4840/OpcBridge", AutoAcceptUntrustedCertificates = true, RequireAuthentication = false, Username = "", Password = "", AllowedIpAddresses = Array.Empty<string>() },
            Bridge = new
            {
                RateLimits = new { },
                ExpectedTagCount = 100,
                Mappings = mappings ?? new object[]
                {
                    new
                    {
                        SourceId = "default",
                        DaItemId = "Random.Int1",
                        DisplayName = "Int1",
                        DataType = "Int32",
                        UaNodeId = "",
                        Enabled = true,
                        Mode = "Source",
                        Writeable = true,
                        AccessRights = "Read-Write"
                    },
                    new
                    {
                        SourceId = "default",
                        DaItemId = "Random.Real4",
                        DisplayName = "Real4",
                        DataType = "Float",
                        UaNodeId = "",
                        Enabled = true,
                        Mode = "Source",
                        Writeable = false,
                        AccessRights = "Read"
                    },
                    new
                    {
                        SourceId = "default",
                        DaItemId = "Disabled.Tag",
                        DisplayName = "Disabled",
                        DataType = "Int32",
                        UaNodeId = "",
                        Enabled = false,
                        Mode = "Source",
                        Writeable = true,
                        AccessRights = "Read-Write"
                    }
                }
            },
            Mqtt = new { Enabled = false, BrokerUrl = "tcp://localhost:1883", ClientId = "OpcDaToUaBridge", UserName = (string?)null, Password = (string?)null, Tls = false, IgnoreCertErrors = false, TopicPrefix = "bridge/tags", PayloadFields = "Value, Timestamp" }
        };
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), JsonSerializer.Serialize(appsettings, new JsonSerializerOptions { WriteIndented = true }));
        // Ensure seed mappings are used: no pre-existing mappings.json
        string mapPath = Path.Combine(dir, "mappings.json");
        if (File.Exists(mapPath)) File.Delete(mapPath);
    }

    [Fact]
    public async Task HmiTags_ReturnsEnabledMappingsOnly()
    {
        await using var handle = await TestAppHandle.StartAsync(WriteAppsettings);

        using var doc = await handle.GetJsonAsync("/api/hmi/tags");
        Assert.True(doc.RootElement.TryGetProperty("version", out var version));
        Assert.True(version.GetInt64() >= 0);
        Assert.True(doc.RootElement.TryGetProperty("tags", out var tags));
        Assert.Equal(JsonValueKind.Array, tags.ValueKind);

        var list = tags.EnumerateArray().ToList();
        Assert.Equal(2, list.Count); // disabled excluded

        var int1 = list.Single(t => t.GetProperty("daItemId").GetString() == "Random.Int1");
        Assert.Equal("default", int1.GetProperty("sourceId").GetString());
        Assert.Equal("Int1", int1.GetProperty("displayName").GetString());
        Assert.True(int1.GetProperty("writeable").GetBoolean());

        var real4 = list.Single(t => t.GetProperty("daItemId").GetString() == "Random.Real4");
        Assert.False(real4.GetProperty("writeable").GetBoolean());
    }
}
```

- [ ] **Step 2: Run test — expect fail**

```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter "FullyQualifiedName~HmiTags_ReturnsEnabledMappingsOnly"
```
Expected: FAIL (404 or connection error — endpoint missing)

- [ ] **Step 3: Implement snapshot helper**

`src/OpcBridge.App/Hmi/HmiTagSnapshot.cs`:
```csharp
using OpcBridge.Client;
using OpcBridge.Core;

namespace OpcBridge.App.Hmi;

public static class HmiTagSnapshot
{
    public static HmiTagsResponse Build(MappingStore mappingStore, BridgeState bridgeState)
    {
        (IReadOnlyList<TagMapping> mappings, long version) = mappingStore.GetSnapshot();
        IReadOnlyList<BridgeValueSnapshot> values = bridgeState.GetValues();

        Dictionary<string, BridgeValueSnapshot> byKey = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < values.Count; i++)
        {
            BridgeValueSnapshot v = values[i];
            byKey[string.Concat(v.SourceId, "::", v.DaItemId)] = v;
        }

        List<HmiTagDto> tags = new();
        for (int i = 0; i < mappings.Count; i++)
        {
            TagMapping m = mappings[i];
            if (!m.Enabled)
            {
                continue;
            }

            byKey.TryGetValue(string.Concat(m.SourceId, "::", m.DaItemId), out BridgeValueSnapshot? snap);
            tags.Add(new HmiTagDto
            {
                SourceId = m.SourceId,
                DaItemId = m.DaItemId,
                DisplayName = string.IsNullOrWhiteSpace(m.DisplayName) ? m.DaItemId : m.DisplayName,
                DataType = m.DataType,
                Value = snap?.Value,
                TimestampUtc = snap?.TimestampUtc,
                DaQuality = snap?.DaQuality,
                IsGood = snap?.IsGood,
                Writeable = m.Writeable
            });
        }

        tags.Sort((a, b) =>
        {
            int c = string.Compare(a.SourceId, b.SourceId, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.DaItemId, b.DaItemId, StringComparison.OrdinalIgnoreCase);
        });

        return new HmiTagsResponse { Version = version, Tags = tags };
    }
}
```

- [ ] **Step 4: Map endpoint in `Program.cs`**

Add usings:
```csharp
using OpcBridge.App.Hmi;
using OpcBridge.Client;
```

After existing `/api/values` (or near other API maps):
```csharp
app.MapGet("/api/hmi/tags", (MappingStore mappingStore, BridgeState state) =>
    Results.Json(HmiTagSnapshot.Build(mappingStore, state)));
```

- [ ] **Step 5: Run test — expect pass**

Same docker test command as Step 2. Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/OpcBridge.App/Hmi/HmiTagSnapshot.cs src/OpcBridge.App/Program.cs tests/OpcBridge.LoadTest/HmiApiTests.cs
git commit -m "feat(hmi): add GET /api/hmi/tags snapshot"
```

---

### Task 4: `TryHmiWriteAsync` + `POST /api/hmi/write`

**Files:**
- Modify: `src/OpcBridge.App/BridgeWorker.cs`
- Modify: `src/OpcBridge.App/Program.cs`
- Modify: `tests/OpcBridge.LoadTest/HmiApiTests.cs`

**Interfaces:**
- Produces: `Task<(bool Ok, string? Error)> BridgeWorker.TryHmiWriteAsync(string sourceId, string daItemId, object? value, CancellationToken ct)`
- Produces: `POST /api/hmi/write` body `HmiWriteRequest` → `HmiWriteResponse`

- [ ] **Step 1: Write failing write-gate tests**

Append to `HmiApiTests.cs`:
```csharp
    [Fact]
    public async Task HmiWrite_RejectsReadOnlyTag()
    {
        await using var handle = await TestAppHandle.StartAsync(WriteAppsettings);

        using var content = new StringContent(
            """{"sourceId":"default","daItemId":"Random.Real4","value":1.5}""",
            System.Text.Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await handle.Client.PostAsync("/api/hmi/write", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task HmiWrite_RejectsUnknownTag()
    {
        await using var handle = await TestAppHandle.StartAsync(WriteAppsettings);

        using var content = new StringContent(
            """{"sourceId":"default","daItemId":"Does.Not.Exist","value":1}""",
            System.Text.Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await handle.Client.PostAsync("/api/hmi/write", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task HmiWrite_RejectsDisabledTag()
    {
        await using var handle = await TestAppHandle.StartAsync(WriteAppsettings);

        using var content = new StringContent(
            """{"sourceId":"default","daItemId":"Disabled.Tag","value":1}""",
            System.Text.Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await handle.Client.PostAsync("/api/hmi/write", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }
```

- [ ] **Step 2: Run tests — expect fail**

```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter "FullyQualifiedName~HmiWrite_"
```
Expected: FAIL (404)

- [ ] **Step 3: Add `TryHmiWriteAsync` on `BridgeWorker`**

Near `ApplyUaWriteAsync`:
```csharp
    public async Task<(bool Ok, string? Error)> TryHmiWriteAsync(
        string sourceId,
        string daItemId,
        object? value,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(daItemId))
        {
            return (false, "sourceId and daItemId are required");
        }

        (IReadOnlyList<TagMapping> mappings, _) = mapping_store_.GetSnapshot();
        TagMapping? mapping = mappings.FirstOrDefault(m =>
            string.Equals(m.SourceId, sourceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.DaItemId, daItemId, StringComparison.OrdinalIgnoreCase));

        if (mapping is null)
        {
            return (false, "Tag is not mapped");
        }

        if (!mapping.Enabled)
        {
            return (false, "Tag is disabled");
        }

        if (!mapping.Writeable)
        {
            return (false, "Tag is read-only");
        }

        if (write_queue_ is null)
        {
            return (false, "Bridge write path is not ready");
        }

        object? converted = ConvertHmiValue(mapping, value);
        bool ok = await ApplyUaWriteAsync(mapping.SourceId, mapping.DaItemId, converted, DateTime.UtcNow, ct)
            .ConfigureAwait(false);
        return ok ? (true, null) : (false, "Write failed or timed out");
    }

    private static object? ConvertHmiValue(TagMapping mapping, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement je)
        {
            value = je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.TryGetInt64(out long l) ? l : je.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => je.ToString()
            };
        }

        if (value is string s)
        {
            return ConvertIncoming(mapping, s);
        }

        return value;
    }
```

Add `using System.Text.Json;` if missing at top of `BridgeWorker.cs`.

- [ ] **Step 4: Map POST endpoint**

In `Program.cs`:
```csharp
app.MapPost("/api/hmi/write", async (HmiWriteRequest request, BridgeWorker worker, CancellationToken ct) =>
{
    (bool ok, string? error) = await worker.TryHmiWriteAsync(
        request.SourceId ?? string.Empty,
        request.DaItemId ?? string.Empty,
        request.Value,
        ct).ConfigureAwait(false);
    return Results.Json(new HmiWriteResponse { Ok = ok, Error = error });
});
```

- [ ] **Step 5: Run write + snapshot tests — expect pass**

```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter "FullyQualifiedName~HmiApiTests"
```
Expected: all `HmiApiTests` PASS  
Note: a successful DA write against Matrikon may fail at the queue if COM is unavailable in Docker — gate tests only assert reject paths. Do **not** add a “happy path write succeeds on DA” test in Docker.

- [ ] **Step 6: Commit**

```bash
git add src/OpcBridge.App/BridgeWorker.cs src/OpcBridge.App/Program.cs tests/OpcBridge.LoadTest/HmiApiTests.cs
git commit -m "feat(hmi): add POST /api/hmi/write with Writeable gate"
```

---

### Task 5: SignalR hub `/hmi` + broadcast service

**Files:**
- Create: `src/OpcBridge.App/Hmi/HmiHub.cs`
- Create: `src/OpcBridge.App/Hmi/HmiBroadcastService.cs`
- Modify: `src/OpcBridge.App/Program.cs`
- Modify: `src/OpcBridge.App/OpcBridge.App.csproj` (SignalR is included in Web SDK — no extra package required for server)

**Interfaces:**
- Produces: hub mapped at `/hmi`
- Produces: client events `values` (`IReadOnlyList<HmiValueDelta>` or single batch object `{ items: [...] }`) and `mappingsChanged` (`HmiMappingsChanged`)
- Consumes: `BridgeState.ValueUpdated`, `MappingStore.Changed`

**Event payload contract (final):**
- `values` → `HmiValueDelta[]` (array argument)
- `mappingsChanged` → `HmiMappingsChanged` object

- [ ] **Step 1: Add hub**

`src/OpcBridge.App/Hmi/HmiHub.cs`:
```csharp
using Microsoft.AspNetCore.SignalR;

namespace OpcBridge.App.Hmi;

public sealed class HmiHub : Hub
{
}
```

- [ ] **Step 2: Add broadcast hosted service**

`src/OpcBridge.App/Hmi/HmiBroadcastService.cs`:
```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using OpcBridge.Client;
using OpcBridge.Core;

namespace OpcBridge.App.Hmi;

public sealed class HmiBroadcastService : IHostedService
{
    private readonly BridgeState bridge_state_;
    private readonly MappingStore mapping_store_;
    private readonly IHubContext<HmiHub> hub_;
    private readonly object batch_lock_ = new();
    private readonly Dictionary<string, HmiValueDelta> pending_ = new(StringComparer.OrdinalIgnoreCase);
    private Timer? flush_timer_;

    public HmiBroadcastService(BridgeState bridgeState, MappingStore mappingStore, IHubContext<HmiHub> hub)
    {
        bridge_state_ = bridgeState;
        mapping_store_ = mappingStore;
        hub_ = hub;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        bridge_state_.ValueUpdated += OnValueUpdated;
        mapping_store_.Changed += OnMappingsChanged;
        flush_timer_ = new Timer(Flush, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        bridge_state_.ValueUpdated -= OnValueUpdated;
        mapping_store_.Changed -= OnMappingsChanged;
        flush_timer_?.Dispose();
        flush_timer_ = null;
        return Task.CompletedTask;
    }

    private void OnValueUpdated(BridgeValue value)
    {
        HmiValueDelta delta = new()
        {
            SourceId = value.SourceId,
            DaItemId = value.DaItemId,
            Value = value.Value,
            TimestampUtc = value.TimestampUtc,
            DaQuality = value.DaQuality,
            IsGood = value.IsGood
        };
        string key = string.Concat(value.SourceId, "::", value.DaItemId);
        lock (batch_lock_)
        {
            pending_[key] = delta;
        }
    }

    private void OnMappingsChanged(long version)
    {
        _ = hub_.Clients.All.SendAsync("mappingsChanged", new HmiMappingsChanged { Version = version });
    }

    private void Flush(object? state)
    {
        HmiValueDelta[] batch;
        lock (batch_lock_)
        {
            if (pending_.Count == 0)
            {
                return;
            }

            batch = pending_.Values.ToArray();
            pending_.Clear();
        }

        _ = hub_.Clients.All.SendAsync("values", batch);
    }
}
```

- [ ] **Step 3: Register in `Program.cs`**

After other service registrations:
```csharp
builder.Services.AddSignalR();
builder.Services.AddHostedService<HmiBroadcastService>();
```

After `WebApplication app = builder.Build();` routes, add:
```csharp
app.MapHub<HmiHub>("/hmi");
```

- [ ] **Step 4: Build + run existing tests**

```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
  bash -lc 'dotnet build OpcDaToUaBridge.sln -c Release && dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter "FullyQualifiedName~HmiApiTests"'
```
Expected: build 0w/0e, HmiApiTests PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.App/Hmi src/OpcBridge.App/Program.cs
git commit -m "feat(hmi): SignalR /hmi hub with value and mapping broadcasts"
```

---

### Task 6: Avalonia `OpcBridge.Hmi` project scaffold

**Files:**
- Create: `src/OpcBridge.Hmi/OpcBridge.Hmi.csproj`
- Create: `src/OpcBridge.Hmi/Program.cs`
- Create: `src/OpcBridge.Hmi/App.axaml`
- Create: `src/OpcBridge.Hmi/App.axaml.cs`
- Create: `src/OpcBridge.Hmi/Views/MainWindow.axaml`
- Create: `src/OpcBridge.Hmi/Views/MainWindow.axaml.cs`
- Create: `src/OpcBridge.Hmi/ViewModels/MainViewModel.cs` (stub)
- Modify: `OpcDaToUaBridge.sln`

**Interfaces:**
- Produces: runnable desktop project referencing `OpcBridge.Client` only.

- [ ] **Step 1: Create csproj**

`src/OpcBridge.Hmi/OpcBridge.Hmi.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.1" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.1" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.1" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.1" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.11" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpcBridge.Client\OpcBridge.Client.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Minimal Avalonia bootstrap**

`src/OpcBridge.Hmi/Program.cs`:
```csharp
using Avalonia;
using System;

namespace OpcBridge.Hmi;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

`src/OpcBridge.Hmi/App.axaml`:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="OpcBridge.Hmi.App"
             RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

`src/OpcBridge.Hmi/App.axaml.cs`:
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpcBridge.Hmi.Views;

namespace OpcBridge.Hmi;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

`src/OpcBridge.Hmi/Views/MainWindow.axaml`:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="OpcBridge.Hmi.Views.MainWindow"
        Title="OpcBridge HMI"
        Width="1100" Height="700">
  <TextBlock Margin="16" Text="OpcBridge HMI scaffold" />
</Window>
```

`src/OpcBridge.Hmi/Views/MainWindow.axaml.cs`:
```csharp
using Avalonia.Controls;

namespace OpcBridge.Hmi.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

`src/OpcBridge.Hmi/app.manifest` (minimal, Avalonia template style):
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="OpcBridge.Hmi"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

- [ ] **Step 3: Add to solution and build**

```bash
dotnet sln OpcDaToUaBridge.sln add src/OpcBridge.Hmi/OpcBridge.Hmi.csproj
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet build src/OpcBridge.Hmi/OpcBridge.Hmi.csproj -c Release
```
Expected: `0 Warning(s), 0 Error(s)`  
(If Avalonia restore fails offline, fix package versions to restore cleanly — do not drop the project.)

- [ ] **Step 4: Commit**

```bash
git add src/OpcBridge.Hmi OpcDaToUaBridge.sln
git commit -m "feat(hmi): scaffold Avalonia OpcBridge.Hmi project"
```

---

### Task 7: HMI client + view-models

**Files:**
- Create: `src/OpcBridge.Hmi/Services/BridgeApiClient.cs`
- Create: `src/OpcBridge.Hmi/Services/HmiHubClient.cs`
- Create: `src/OpcBridge.Hmi/ViewModels/TagItemViewModel.cs`
- Create: `src/OpcBridge.Hmi/ViewModels/MainViewModel.cs`
- Create: `tests/OpcBridge.LoadTest/HmiClientMergeTests.cs` (pure merge logic test — no UI)

**Interfaces:**
- `BridgeApiClient.GetTagsAsync(CancellationToken)` → `HmiTagsResponse`
- `BridgeApiClient.WriteAsync(HmiWriteRequest, CancellationToken)` → `HmiWriteResponse`
- `HmiHubClient.ConnectAsync(baseUrl, onValues, onMappingsChanged, ct)`
- `MainViewModel.ConnectAsync` / `Disconnect` / `WriteAsync` / `ApplySnapshot` / `ApplyDeltas`

- [ ] **Step 1: Write merge unit test (no host required)**

`tests/OpcBridge.LoadTest/HmiClientMergeTests.cs`:
```csharp
using OpcBridge.Client;
using Xunit;

namespace OpcBridge.LoadTest;

// Mirror of HMI merge rules — keep in sync with MainViewModel.ApplyDeltas / ApplySnapshot.
public sealed class HmiClientMergeTests
{
    [Fact]
    public void ApplyDelta_UpdatesMatchingTagValue()
    {
        var tags = new Dictionary<string, HmiTagDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["default::Random.Int1"] = new HmiTagDto
            {
                SourceId = "default",
                DaItemId = "Random.Int1",
                DisplayName = "Int1",
                Value = 1,
                Writeable = true
            }
        };

        HmiValueDelta delta = new()
        {
            SourceId = "default",
            DaItemId = "Random.Int1",
            Value = 42,
            TimestampUtc = DateTime.UtcNow,
            DaQuality = 192,
            IsGood = true
        };

        string key = $"{delta.SourceId}::{delta.DaItemId}";
        if (tags.TryGetValue(key, out HmiTagDto? tag))
        {
            tag.Value = delta.Value;
            tag.TimestampUtc = delta.TimestampUtc;
            tag.DaQuality = delta.DaQuality;
            tag.IsGood = delta.IsGood;
        }

        Assert.Equal(42, Convert.ToInt32(tags[key].Value));
        Assert.True(tags[key].IsGood);
    }
}
```

Note: once `MainViewModel` exists, prefer testing a small static `HmiTagCache` helper in `OpcBridge.Hmi` if LoadTest cannot reference Avalonia — either:
1. Keep this mirror test, or
2. Extract pure `HmiTagCache` into `OpcBridge.Client` and test that.

**Preferred:** put pure cache helper in Client:

`src/OpcBridge.Client/HmiTagCache.cs`:
```csharp
namespace OpcBridge.Client;

public sealed class HmiTagCache
{
    private readonly Dictionary<string, HmiTagDto> tags_ = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<HmiTagDto> Tags => tags_.Values;

    public void ReplaceAll(IEnumerable<HmiTagDto> tags)
    {
        tags_.Clear();
        foreach (HmiTagDto tag in tags)
        {
            tags_[Key(tag.SourceId, tag.DaItemId)] = tag;
        }
    }

    public void ApplyDeltas(IEnumerable<HmiValueDelta> deltas)
    {
        foreach (HmiValueDelta d in deltas)
        {
            if (!tags_.TryGetValue(Key(d.SourceId, d.DaItemId), out HmiTagDto? tag))
            {
                continue;
            }

            tag.Value = d.Value;
            tag.TimestampUtc = d.TimestampUtc;
            tag.DaQuality = d.DaQuality;
            tag.IsGood = d.IsGood;
        }
    }

    private static string Key(string sourceId, string daItemId) => string.Concat(sourceId, "::", daItemId);
}
```

Then rewrite `HmiClientMergeTests` to use `HmiTagCache` (add project reference from LoadTest to Client if missing).

- [ ] **Step 2: Implement `BridgeApiClient`**

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using OpcBridge.Client;

namespace OpcBridge.Hmi.Services;

public sealed class BridgeApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private HttpClient client_ = new();

    public void SetBaseAddress(string baseUrl)
    {
        client_.Dispose();
        client_ = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    }

    public async Task<HmiTagsResponse> GetTagsAsync(CancellationToken ct)
    {
        HmiTagsResponse? response = await client_.GetFromJsonAsync<HmiTagsResponse>("api/hmi/tags", JsonOptions, ct)
            .ConfigureAwait(false);
        return response ?? new HmiTagsResponse();
    }

    public async Task<HmiWriteResponse> WriteAsync(HmiWriteRequest request, CancellationToken ct)
    {
        using HttpResponseMessage http = await client_.PostAsJsonAsync("api/hmi/write", request, JsonOptions, ct)
            .ConfigureAwait(false);
        HmiWriteResponse? body = await http.Content.ReadFromJsonAsync<HmiWriteResponse>(JsonOptions, ct)
            .ConfigureAwait(false);
        return body ?? new HmiWriteResponse { Ok = false, Error = $"HTTP {(int)http.StatusCode}" };
    }

    public void Dispose() => client_.Dispose();
}
```

- [ ] **Step 3: Implement `HmiHubClient`**

```csharp
using Microsoft.AspNetCore.SignalR.Client;
using OpcBridge.Client;

namespace OpcBridge.Hmi.Services;

public sealed class HmiHubClient : IAsyncDisposable
{
    private HubConnection? connection_;

    public async Task ConnectAsync(
        string baseUrl,
        Func<HmiValueDelta[], Task> onValues,
        Func<HmiMappingsChanged, Task> onMappingsChanged,
        CancellationToken ct)
    {
        await DisposeAsync().ConfigureAwait(false);

        string hubUrl = baseUrl.TrimEnd('/') + "/hmi";
        connection_ = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        connection_.On<HmiValueDelta[]>("values", async batch => await onValues(batch).ConfigureAwait(false));
        connection_.On<HmiMappingsChanged>("mappingsChanged", async msg => await onMappingsChanged(msg).ConfigureAwait(false));

        connection_.Reconnected += async _ =>
        {
            // Caller should refresh snapshot on reconnect via MainViewModel.
            await Task.CompletedTask;
        };

        await connection_.StartAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (connection_ is not null)
        {
            await connection_.DisposeAsync().ConfigureAwait(false);
            connection_ = null;
        }
    }
}
```

- [ ] **Step 4: Implement `MainViewModel` (CommunityToolkit.Mvvm)**

`src/OpcBridge.Hmi/ViewModels/MainViewModel.cs` — must:
- Properties: `BaseUrl` (default `http://127.0.0.1:8080`), `ConnectionState`, `Filter`, `Tags` (`ObservableCollection<TagItemViewModel>`), `SelectedTag`, `WriteValue`, `StatusMessage`, `IsConnected`
- Commands: `ConnectCommand`, `DisconnectCommand`, `WriteCommand`
- On connect: `api.SetBaseAddress` → `GetTagsAsync` → fill cache/UI → `hub.ConnectAsync`
- On `values`: apply deltas on UI thread (`Dispatcher.UIThread.Post`)
- On `mappingsChanged` or hub reconnected: re-fetch snapshot
- On write: `POST` selected tag; set `StatusMessage` from `HmiWriteResponse`
- Disconnect disposes hub

`TagItemViewModel` wraps one `HmiTagDto` with bindable `ValueText`, `QualityText`, `Writeable`, etc.

- [ ] **Step 5: Run unit test + build Hmi**

```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
  bash -lc 'dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter "FullyQualifiedName~HmiClientMergeTests" && dotnet build src/OpcBridge.Hmi/OpcBridge.Hmi.csproj -c Release'
```
Expected: PASS + 0w/0e

- [ ] **Step 6: Commit**

```bash
git add src/OpcBridge.Client/HmiTagCache.cs src/OpcBridge.Hmi tests/OpcBridge.LoadTest/HmiClientMergeTests.cs tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj
git commit -m "feat(hmi): bridge API client, hub client, and main view-model"
```

---

### Task 8: MainWindow UI — connect, tag grid, faceplate

**Files:**
- Modify: `src/OpcBridge.Hmi/Views/MainWindow.axaml`
- Modify: `src/OpcBridge.Hmi/Views/MainWindow.axaml.cs`
- Modify: `src/OpcBridge.Hmi/App.axaml.cs` (set DataContext)

**UI layout (v1):**
```
[ Base URL textbox ] [ Connect ] [ Disconnect ]  Status: ...
Filter: [________]
+---------------------------+  +----------------------+
| DataGrid tags             |  | Faceplate            |
| Source | Name | Value | Q |  | Name / Item / Type   |
|                           |  | Value / Quality / Ts |
|                           |  | Write: [__] [Write]  |
|                           |  | Message              |
+---------------------------+  +----------------------+
```

- [ ] **Step 1: Wire DataContext**

In `MainWindow` constructor:
```csharp
DataContext = new MainViewModel();
```

- [ ] **Step 2: Build XAML bindings**

Use `TextBox` for BaseUrl/Filter/WriteValue, `Button` commands, `DataGrid` or `ListBox` for tags (`ItemsSource="{Binding FilteredTags}"`, `SelectedItem="{Binding SelectedTag}"`), faceplate `TextBlock`s bound to `SelectedTag.*`, write panel `IsEnabled="{Binding SelectedTag.Writeable}"`.

Keep styles minimal FluentTheme; no custom chrome required.

- [ ] **Step 3: Build Hmi project**

```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet build src/OpcBridge.Hmi/OpcBridge.Hmi.csproj -c Release
```
Expected: 0w/0e

- [ ] **Step 4: Commit**

```bash
git add src/OpcBridge.Hmi
git commit -m "feat(hmi): operator UI for connect, tag list, and faceplate write"
```

---

### Task 9: Docs + full verification

**Files:**
- Modify: `context.md` (HMI API + project map)
- Optional: short note in `HelpContent.cs` only if you want operators to see HMI endpoints — **not required for v1**; skip unless free.

- [ ] **Step 1: Update `context.md` project map**

Add rows:
- `OpcBridge.Client` — HMI/App wire DTOs
- `OpcBridge.Hmi` — Avalonia operator client

Add API bullets:
- `GET /api/hmi/tags`
- `POST /api/hmi/write`
- SignalR `/hmi` events `values`, `mappingsChanged`

Note: HMI is a separate process; trends deferred.

- [ ] **Step 2: Full solution build + tests**

```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 \
  bash -lc 'dotnet build OpcDaToUaBridge.sln -c Release && dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release'
```
Expected: 0w/0e, all tests pass (including pre-existing suite).

- [ ] **Step 3: Commit docs**

```bash
git add context.md
git commit -m "docs(hmi): document HMI API and Avalonia client in context.md"
```

- [ ] **Step 4: Manual Windows checklist (not automated)**

On operator/dev Windows machine with bridge running:
1. `dotnet run --project src/OpcBridge.Hmi`
2. Connect to `http://<bridge-host>:8080`
3. Confirm tag list fills from mappings
4. Confirm values update live (SignalR)
5. Write a `Writeable` tag; confirm DA/faceplate feedback
6. Attempt write on read-only tag; confirm error message

---

## Spec coverage checklist

| Spec requirement | Task |
|---|---|
| Same solution, separate HMI process | 1, 6 |
| `OpcBridge.Client` DTOs | 1 |
| `GET /api/hmi/tags` snapshot | 3 |
| `POST /api/hmi/write` + `Writeable` gate | 4 |
| SignalR `/hmi` deltas + mappingsChanged | 2, 5 |
| Reuse `WriteQueue` / `ApplyUaWriteAsync` | 4 |
| Port 8080 only | Global + 3–5 |
| Avalonia connect / grid / faceplate / write | 6–8 |
| No Influx/auth/config UI/Android | Global non-goals |
| Tests for snapshot + write rejects | 3–4 |
| context.md update | 9 |

## Self-review notes

- No TBD placeholders; event names and DTO property names are fixed.
- `UpdateDaRead` alone does not fire `ValueUpdated`; current `BridgeWorker` always calls `SetValue` after reads — broadcast hooks `ValueUpdated` only (matches production path).
- Docker cannot prove successful Matrikon COM writes; only gate rejects are automated.
- Avalonia GUI is build-verified in Docker; interactive UI is manual on Windows.
