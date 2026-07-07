# Manual OPC UA Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make fresh installs require manual OPC UA endpoint entry in the dashboard, keep the UA server stopped until configured, and persist endpoint changes through the existing UA settings path.

**Architecture:** Reuse the existing `UaServerHost` settings API and persistence flow instead of creating a second endpoint configuration system. Tighten `UaServerHost` so blank endpoints mean `Not configured`, update the dashboard to edit the endpoint in Connection, and make Monitor/help/status reflect the configured vs missing state.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, embedded HTML/CSS/JS in `DashboardPage.cs`, xUnit, Dockerized `dotnet` build/publish

## Global Constraints

- Require manual endpoint entry on first install or whenever the host has no saved endpoint.
- Treat blank or missing endpoint as `Not configured`.
- Do not auto-generate an endpoint from host IP.
- Do not add multi-endpoint support.
- Persist endpoint changes to host configuration using the existing UA settings persistence path.
- Keep config export/import carrying `endpointUrl`.
- Validate endpoint as non-empty absolute URI with scheme `opc.tcp`.
- Saving invalid values must not overwrite the last saved good value.
- Verification must include `node --check`, Linux `dotnet build OpcDaToUaBridge.sln`, and Windows publish with `0 Warning(s), 0 Error(s)`.

---

## File Map

- `src/OpcBridge.Ua/UaServerOptions.cs`
  - Remove the baked-in endpoint default so blank is representable.
- `src/OpcBridge.Ua/UaServerHost.cs`
  - Centralize endpoint validation, configured/not-configured state, persistence, and immediate UA restart behavior.
- `src/OpcBridge.App/Program.cs`
  - Tighten `/api/ua/settings` payload, validate endpoint writes, and return runtime status after save.
- `src/OpcBridge.App/UaEndpointRequest.cs`
  - DTO for endpoint update API.
- `src/OpcBridge.App/DashboardPage.cs`
  - Add editable Connection-tab endpoint controls and status handling.
- `src/OpcBridge.App/HelpContent.cs`
  - Document first-run manual endpoint configuration.
- `src/OpcBridge.App/appsettings.json`
  - Blank the default endpoint for new installs.
- `tests/OpcBridge.LoadTest/LoadTest.cs`
  - Add targeted UA host and endpoint validation tests if no smaller test project is introduced.
- `tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj`
  - Add `OpcBridge.Ua` project reference so UA host tests can compile.

### Task 1: Make blank endpoint a first-class UA host state

**Files:**
- Modify: `src/OpcBridge.Ua/UaServerOptions.cs:3-12`
- Modify: `src/OpcBridge.Ua/UaServerHost.cs:9-231`
- Modify: `tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj:17-21`
- Test: `tests/OpcBridge.LoadTest/LoadTest.cs`

**Interfaces:**
- Consumes: `UaServerOptions`, `UaServerHost.GetStatus()`, `UaServerHost.UpdateOptions(UaServerOptions updated)`
- Produces:
  - `UaServerHost.IsEndpointConfigured(string? endpointUrl): bool`
  - `UaServerHost.ValidateEndpointOrThrow(string? endpointUrl): string`
  - `UaServerHost.UpdateOptionsAsync(UaServerOptions updated, IReadOnlyList<TagMapping> mappings, CancellationToken cancellationToken): Task<UaServerStatus>`
  - `UaServerStatus.State` can now be `Not configured`, `Stopped`, or `Running`

- [ ] **Step 1: Add failing tests for blank-endpoint behavior**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpcBridge.Ua;
using Xunit;

namespace OpcBridge.LoadTest;

public partial class LoadTest
{
    [Fact]
    public void UaServerHost_BlankEndpoint_IsReportedAsNotConfigured()
    {
        UaServerHost host = new(
            new OptionsWrapper<UaServerOptions>(new UaServerOptions
            {
                ApplicationName = "OpcDaToUaBridge",
                EndpointUrl = ""
            }),
            NullLogger<UaServerHost>.Instance,
            NullLoggerFactory.Instance);

        UaServerStatus status = host.GetStatus();

        Assert.Equal("Not configured", status.State);
        Assert.Equal(string.Empty, status.EndpointUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateEndpointOrThrow_RejectsBlankValues(string? endpointUrl)
    {
        Assert.Throws<InvalidOperationException>(() => UaServerHost.ValidateEndpointOrThrow(endpointUrl));
    }

    [Theory]
    [InlineData("http://host:4840/OpcDaToUaBridge")]
    [InlineData("4840/OpcDaToUaBridge")]
    [InlineData("opc.tcp://")]
    public void ValidateEndpointOrThrow_RejectsInvalidValues(string endpointUrl)
    {
        Assert.Throws<InvalidOperationException>(() => UaServerHost.ValidateEndpointOrThrow(endpointUrl));
    }

    [Fact]
    public void ValidateEndpointOrThrow_AcceptsAbsoluteOpcTcpUrl()
    {
        string endpoint = UaServerHost.ValidateEndpointOrThrow("opc.tcp://192.168.20.13:4840/OpcDaToUaBridge");
        Assert.Equal("opc.tcp://192.168.20.13:4840/OpcDaToUaBridge", endpoint);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj --filter "FullyQualifiedName~UaServerHost_BlankEndpoint_IsReportedAsNotConfigured|FullyQualifiedName~ValidateEndpointOrThrow"`
Expected: FAIL because `ValidateEndpointOrThrow` and `Not configured` behavior do not exist yet.

- [ ] **Step 3: Add UA host validation and not-configured state**

```csharp
public sealed class UaServerOptions
{
    public string ApplicationName { get; set; } = "OpcDaToUaBridge";
    public string EndpointUrl { get; set; } = string.Empty;
    public bool AutoAcceptUntrustedCertificates { get; set; } = true;
    public bool RequireAuthentication { get; set; } = false;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public List<string> AllowedIpAddresses { get; set; } = new();
}
```

```csharp
public sealed class UaServerHost : IAsyncDisposable
{
    public static bool IsEndpointConfigured(string? endpointUrl)
    {
        return !string.IsNullOrWhiteSpace(endpointUrl);
    }

    public static string ValidateEndpointOrThrow(string? endpointUrl)
    {
        string value = endpointUrl?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new InvalidOperationException("OPC UA endpoint is required.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException("OPC UA endpoint must be an absolute URI.");
        }

        if (!string.Equals(uri.Scheme, "opc.tcp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OPC UA endpoint must use the opc.tcp scheme.");
        }

        return uri.ToString();
    }

    public async Task<UaServerStatus> UpdateOptionsAsync(UaServerOptions updated, IReadOnlyList<TagMapping> mappings, CancellationToken cancellationToken)
    {
        options_ = updated;
        PersistSettings();

        await StopAsync(cancellationToken).ConfigureAwait(false);
        if (IsEndpointConfigured(updated.EndpointUrl))
        {
            await StartAsync(mappings, cancellationToken).ConfigureAwait(false);
        }

        return GetStatus();
    }

    public async Task StartAsync(IReadOnlyList<TagMapping> mappings, CancellationToken cancellationToken)
    {
        if (!IsEndpointConfigured(options_.EndpointUrl))
        {
            logger_.LogInformation("OPC UA server not started because no endpoint is configured.");
            server_ = null;
            return;
        }

        string endpointUrl = ValidateEndpointOrThrow(options_.EndpointUrl);
        options_.EndpointUrl = endpointUrl;
        // existing start path continues
    }

    public UaServerStatus GetStatus()
    {
        if (!IsEndpointConfigured(options_.EndpointUrl))
        {
            return new UaServerStatus("Not configured", string.Empty, 0, 0, null);
        }

        BridgeUaServer? server = server_;
        return new UaServerStatus(
            server is not null ? "Running" : "Stopped",
            options_.EndpointUrl,
            server?.GetConnectedSessionCount() ?? 0,
            server?.GetMappedNodeCount() ?? 0,
            server?.GetLastValueUpdateUtc());
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj --filter "FullyQualifiedName~UaServerHost_BlankEndpoint_IsReportedAsNotConfigured|FullyQualifiedName~ValidateEndpointOrThrow"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.Ua/UaServerOptions.cs src/OpcBridge.Ua/UaServerHost.cs tests/OpcBridge.LoadTest/OpcBridge.LoadTest.cs tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj
git commit -m "feat(ua): treat blank endpoint as not configured"
```

### Task 2: Tighten UA settings API around manual endpoint entry

**Files:**
- Create: `src/OpcBridge.App/UaEndpointRequest.cs`
- Modify: `src/OpcBridge.App/Program.cs:553-594`
- Test: `tests/OpcBridge.LoadTest/LoadTest.cs`

**Interfaces:**
- Consumes: `UaServerHost.GetOptions()`, `UaServerHost.GetStatus()`, `UaServerHost.ValidateEndpointOrThrow(...)`, `UaServerHost.UpdateOptionsAsync(...)`
- Produces:
  - `GET /api/ua/settings` response fields: `endpointUrl`, `configured`, `state`, `autoAcceptUntrustedCertificates`, `requireAuthentication`, `username`, `allowedIpAddresses`
  - `POST /api/ua/endpoint` request DTO: `UaEndpointRequest { string? EndpointUrl }`
  - `POST /api/ua/endpoint` response fields: `status`, `message`, `endpointUrl`, `configured`, `state`

- [ ] **Step 1: Add failing tests for API validation rules**

```csharp
using OpcBridge.App;
using OpcBridge.Ua;
using Xunit;

namespace OpcBridge.LoadTest;

public partial class LoadTest
{
    [Fact]
    public void UaEndpointRequest_BlankEndpoint_IsInvalidForSave()
    {
        UaEndpointRequest request = new() { EndpointUrl = "   " };
        Assert.Equal("   ", request.EndpointUrl);
        Assert.Throws<InvalidOperationException>(() => UaServerHost.ValidateEndpointOrThrow(request.EndpointUrl));
    }

    [Fact]
    public void UaSettings_GetConfiguredFlag_FollowsEndpointPresence()
    {
        Assert.False(UaServerHost.IsEndpointConfigured(""));
        Assert.True(UaServerHost.IsEndpointConfigured("opc.tcp://192.168.20.13:4840/OpcDaToUaBridge"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj --filter "FullyQualifiedName~UaEndpointRequest_BlankEndpoint_IsInvalidForSave|FullyQualifiedName~UaSettings_GetConfiguredFlag_FollowsEndpointPresence"`
Expected: FAIL because `UaEndpointRequest` and `configured` response contract do not exist yet.

- [ ] **Step 3: Implement endpoint-specific API and response contract**

```csharp
namespace OpcBridge.App;

public sealed class UaEndpointRequest
{
    public string? EndpointUrl { get; set; }
}
```

```csharp
app.MapGet("/api/ua/settings", (UaServerHost uaServer) =>
{
    UaServerOptions opts = uaServer.GetOptions();
    UaServerStatus status = uaServer.GetStatus();
    return Results.Json(new
    {
        endpointUrl = opts.EndpointUrl,
        configured = UaServerHost.IsEndpointConfigured(opts.EndpointUrl),
        state = status.State,
        autoAcceptUntrustedCertificates = opts.AutoAcceptUntrustedCertificates,
        requireAuthentication = opts.RequireAuthentication,
        username = opts.Username ?? string.Empty,
        allowedIpAddresses = opts.AllowedIpAddresses ?? new List<string>()
    });
});

app.MapPost("/api/ua/endpoint", async (UaEndpointRequest request, UaServerHost uaServer, MappingStore mappingStore, CancellationToken cancellationToken) =>
{
    string endpointUrl = UaServerHost.ValidateEndpointOrThrow(request.EndpointUrl);
    UaServerOptions current = uaServer.GetOptions();
    UaServerOptions updated = new()
    {
        ApplicationName = current.ApplicationName,
        EndpointUrl = endpointUrl,
        AutoAcceptUntrustedCertificates = current.AutoAcceptUntrustedCertificates,
        RequireAuthentication = current.RequireAuthentication,
        Username = current.Username,
        Password = current.Password,
        AllowedIpAddresses = current.AllowedIpAddresses
    };

    (IReadOnlyList<TagMapping> mappings, _) = mappingStore.GetSnapshot();
    UaServerStatus status = await uaServer.UpdateOptionsAsync(updated, mappings, cancellationToken);
    return Results.Json(new
    {
        status = "ok",
        message = "OPC UA endpoint saved.",
        endpointUrl = updated.EndpointUrl,
        configured = true,
        state = status.State
    });
});
```

- [ ] **Step 4: Run test to verify it passes**

Run: `docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj --filter "FullyQualifiedName~UaEndpointRequest_BlankEndpoint_IsInvalidForSave|FullyQualifiedName~UaSettings_GetConfiguredFlag_FollowsEndpointPresence"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.App/UaEndpointRequest.cs src/OpcBridge.App/Program.cs tests/OpcBridge.LoadTest/LoadTest.cs
git commit -m "feat(app): add manual OPC UA endpoint API"
```

### Task 3: Expose manual endpoint configuration in the dashboard

**Files:**
- Modify: `src/OpcBridge.App/DashboardPage.cs:339-406`
- Modify: `src/OpcBridge.App/DashboardPage.cs:1000-1660`
- Test: `src/OpcBridge.App/DashboardPage.cs` script extraction via `node --check`

**Interfaces:**
- Consumes: `GET /api/ua/settings`, `POST /api/ua/endpoint`, `/api/dashboard` UA status payload
- Produces:
  - Connection-tab controls: `cfgUaEndpoint`, `cfgUaApply`, `uaConfigMessage`
  - JS functions: `loadUaSettings()`, `saveUaEndpoint()`
  - Monitor-tab display behavior for `Not configured`

- [ ] **Step 1: Add a failing UI contract check by extracting the script and expecting missing symbols**

```bash
sed -n '/<script>/,/<\/script>/p' src/OpcBridge.App/DashboardPage.cs | sed '1d;$d' > /tmp/dashboard.js
node --check /tmp/dashboard.js
python - <<'PY'
from pathlib import Path
text = Path('src/OpcBridge.App/DashboardPage.cs').read_text()
assert 'id="cfgUaEndpoint"' in text, 'missing cfgUaEndpoint control'
assert "fetch('/api/ua/settings')" in text, 'missing UA settings load'
assert "fetch('/api/ua/endpoint'" in text, 'missing UA endpoint save'
PY
```

Expected: FAIL on the Python assertions because the UI controls and fetch calls do not exist yet.

- [ ] **Step 2: Run the UI contract check to verify it fails**

Run: the shell snippet from Step 1
Expected: `node --check` passes, Python assertions fail on missing endpoint UI/JS

- [ ] **Step 3: Add Connection-tab endpoint UI and JS**

```html
<div class="conn-section">
    <div class="conn-section-h">OPC UA Endpoint <span class="info" data-tip="Required on first install. Use the client-facing opc.tcp address that UA clients will connect to.">i</span></div>
    <div class="field">
        <label class="fl">Endpoint</label>
        <input id="cfgUaEndpoint" type="text" placeholder="opc.tcp://192.168.20.13:4840/OpcDaToUaBridge" style="flex:1">
        <button class="btn ghost" id="cfgUaApply" type="button">Save</button>
    </div>
    <div class="msg" id="uaConfigMessage">OPC UA endpoint required before the UA server can start.</div>
</div>
```

```javascript
async function loadUaSettings() {
    const r = await fetch('/api/ua/settings');
    const p = await r.json();
    el('cfgUaEndpoint').value = p.endpointUrl || '';
    el('uaConfigMessage').textContent = p.configured
        ? ('Current endpoint: ' + p.endpointUrl)
        : 'OPC UA endpoint required before the UA server can start.';
}

async function saveUaEndpoint() {
    const endpointUrl = el('cfgUaEndpoint').value;
    const r = await fetch('/api/ua/endpoint', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ endpointUrl })
    });
    const p = await r.json();
    if (!r.ok) throw new Error(p.error || ('HTTP ' + r.status));
    el('uaConfigMessage').textContent = p.message;
    await loadUaSettings();
    await refresh();
}
```

```javascript
el('cfgUaApply').addEventListener('click', () => saveUaEndpoint().catch(e => {
    el('uaConfigMessage').textContent = '✗ ' + e.message;
}));
```

```javascript
el('uaEndpoint').textContent = get(ua, 'state') === 'Not configured'
    ? 'Not configured'
    : (get(ua, 'endpointUrl') || '—');
```

- [ ] **Step 4: Run the UI contract check to verify it passes**

Run:
```bash
sed -n '/<script>/,/<\/script>/p' src/OpcBridge.App/DashboardPage.cs | sed '1d;$d' > /tmp/dashboard.js
node --check /tmp/dashboard.js
python - <<'PY'
from pathlib import Path
text = Path('src/OpcBridge.App/DashboardPage.cs').read_text()
assert 'id="cfgUaEndpoint"' in text
assert "fetch('/api/ua/settings')" in text
assert "fetch('/api/ua/endpoint'" in text
PY
```
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.App/DashboardPage.cs
git commit -m "feat(dashboard): add manual OPC UA endpoint editor"
```

### Task 4: Align shipped defaults and help text with manual setup

**Files:**
- Modify: `src/OpcBridge.App/appsettings.json:8-15`
- Modify: `src/OpcBridge.App/HelpContent.cs`
- Test: `tests/OpcBridge.LoadTest/LoadTest.cs`

**Interfaces:**
- Consumes: new `Not configured` semantics from `UaServerHost`, Connection-tab endpoint UI behavior
- Produces:
  - blank shipped `Ua:EndpointUrl`
  - help text that says first install requires manual endpoint setup

- [ ] **Step 1: Add failing tests for shipped default and help guidance**

```csharp
using System.Text.Json;
using Xunit;

namespace OpcBridge.LoadTest;

public partial class LoadTest
{
    [Fact]
    public void AppSettings_ShipsWithBlankUaEndpoint()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText("src/OpcBridge.App/appsettings.json"));
        string endpoint = doc.RootElement.GetProperty("Ua").GetProperty("EndpointUrl").GetString() ?? "<null>";
        Assert.Equal(string.Empty, endpoint);
    }

    [Fact]
    public void HelpContent_ExplainsManualEndpointSetup()
    {
        Assert.Contains("manual OPC UA endpoint", HelpContent.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first install", HelpContent.Markdown, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj --filter "FullyQualifiedName~AppSettings_ShipsWithBlankUaEndpoint|FullyQualifiedName~HelpContent_ExplainsManualEndpointSetup"`
Expected: FAIL because the shipped endpoint is still populated and help text does not yet mention the first-install requirement.

- [ ] **Step 3: Blank the shipped endpoint and update help**

```json
"Ua": {
  "ApplicationName": "OpcDaToUaBridge",
  "EndpointUrl": "",
  "AutoAcceptUntrustedCertificates": true,
  "RequireAuthentication": false,
  "Username": "",
  "Password": "",
  "AllowedIpAddresses": []
}
```

```markdown
On first install, the OPC UA server stays unconfigured until you enter and save a manual OPC UA endpoint in Connection -> OPC UA Endpoint.
Use a client-facing `opc.tcp://host:port/path` address, then reconnect UA clients after saving.
```

- [ ] **Step 4: Run test to verify it passes**

Run: `docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj --filter "FullyQualifiedName~AppSettings_ShipsWithBlankUaEndpoint|FullyQualifiedName~HelpContent_ExplainsManualEndpointSetup"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.App/appsettings.json src/OpcBridge.App/HelpContent.cs tests/OpcBridge.LoadTest/LoadTest.cs
git commit -m "docs(config): require manual OPC UA endpoint on first install"
```

### Task 5: Verify end-to-end build and publish behavior

**Files:**
- Verify only: `src/OpcBridge.App/DashboardPage.cs`
- Verify only: `src/OpcBridge.App/Program.cs`
- Verify only: `src/OpcBridge.Ua/UaServerHost.cs`
- Verify only: `src/OpcBridge.Ua/UaServerOptions.cs`
- Verify only: `src/OpcBridge.App/HelpContent.cs`
- Verify only: `src/OpcBridge.App/appsettings.json`
- Verify only: `tests/OpcBridge.LoadTest/LoadTest.cs`

**Interfaces:**
- Consumes: all prior tasks
- Produces: verified implementation with clean JS parse, clean Linux build, and clean Windows publish

- [ ] **Step 1: Run targeted test suite**

Run: `docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj`
Expected: PASS

- [ ] **Step 2: Validate embedded dashboard JavaScript**

Run:
```bash
sed -n '/<script>/,/<\/script>/p' src/OpcBridge.App/DashboardPage.cs | sed '1d;$d' > /tmp/dashboard.js
node --check /tmp/dashboard.js
```
Expected: no output

- [ ] **Step 3: Run full Linux solution build**

Run: `docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build OpcDaToUaBridge.sln`
Expected: `0 Warning(s)` and `0 Error(s)`

- [ ] **Step 4: Run Windows target publish**

Run: `docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet publish src/OpcBridge.App -c Release -o publish -r win-x86 --self-contained false`
Expected: `0 Warning(s)` and `0 Error(s)`

- [ ] **Step 5: Commit final verification-ready state**

```bash
git add src/OpcBridge.App/DashboardPage.cs src/OpcBridge.App/Program.cs src/OpcBridge.App/UaEndpointRequest.cs src/OpcBridge.App/HelpContent.cs src/OpcBridge.App/appsettings.json src/OpcBridge.Ua/UaServerHost.cs src/OpcBridge.Ua/UaServerOptions.cs tests/OpcBridge.LoadTest/LoadTest.cs tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj
git commit -m "feat(ua): require manual endpoint configuration"
```

## Self-Review

### Spec coverage
- Manual first-run endpoint entry: Task 3 + Task 4
- Blank endpoint means not configured and server stays stopped: Task 1
- Persist through existing UA settings path: Task 1 + Task 2
- Input validation and no overwrite on invalid values: Task 1 + Task 2
- Import/export still carries endpoint: preserved by reusing existing `Program.cs` config export/import path; no separate task required because no code path changes are planned there
- Help update: Task 4
- Clean verification: Task 5

### Placeholder scan
- No `TBD`, `TODO`, or “similar to” references remain.
- Each task has explicit files, interfaces, code, commands, and expected outcomes.

### Type consistency
- `UaEndpointRequest.EndpointUrl` is used consistently in API/UI tasks.
- `UaServerHost.ValidateEndpointOrThrow`, `IsEndpointConfigured`, and `UpdateOptionsAsync` are introduced in Task 1 and consumed in Task 2.
- `UaServerStatus.State` values are consistent across host, API, and UI tasks.
