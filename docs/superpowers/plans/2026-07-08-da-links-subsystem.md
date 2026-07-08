# DA Links Subsystem Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a first-class `DA Links` subsystem that manages OPC DA provider-consumer rules separately from DA->UA mappings, while reusing the existing DA source runtime and write queues.

**Architecture:** Split provider-consumer links out of `TagMapping` into a dedicated `DaLinkRule` + `DaLinkStore` flow. The backend validates and executes links against DA-native metadata and the existing per-source runtime; the dashboard gets a dedicated `DA Links` surface and the tag faceplate stops editing provider relationships.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, xUnit, existing dashboard HTML/CSS/vanilla JS in `DashboardPage.cs`, existing OPC DA COM/DCOM runtime in `OpcBridge.Da`

## Global Constraints

- Reuse the existing DA source connections, polling/subscriptions, and write queues; do not create a second OPC DA engine.
- Keep DA provider-consumer rules separate from DA->UA mappings at the product, API, and persistence levels.
- Allow cross-source / cross-server links when validation passes.
- Validate links using DA-native canonical type and access metadata, not UA mapping `DataType`.
- v1 supports one provider -> many consumers and rejects many providers -> one consumer.
- Do not silently clear invalid rules; reject invalid create/update operations with explicit errors.
- Migrate existing `TagMapping.ProviderSourceId` / `ProviderDaItemId` data into the new store.
- Keep `Tags` focused on DA->UA mapping; `DA Links` is the only editing surface for provider-consumer rules.

---

## File Responsibilities

- `src/OpcBridge.App/DaLinkRule.cs`
  - New link model and any small normalization helpers specific to DA links.
- `src/OpcBridge.App/DaLinkStore.cs`
  - Load/save `links.json`, normalize rules, perform startup migration from legacy mapping fields, expose snapshots and update operations.
- `src/OpcBridge.App/DaLinkRequests.cs`
  - DTOs for create/update/delete requests.
- `src/OpcBridge.App/Program.cs`
  - Register the new store, add `/api/da-links` endpoints, wire migration, keep mapping endpoints free of new provider writes.
- `src/OpcBridge.App/BridgeWorker.cs`
  - Consume `DaLinkStore` snapshots instead of provider fields on `TagMapping`, validate runtime propagation path against rule data.
- `src/OpcBridge.App/DashboardPage.cs`
  - Replace current connected-tags UI with a dedicated `DA Links` workflow; remove provider editing from the faceplate.
- `src/OpcBridge.App/HelpContent.cs`
  - Update docs for the new subsystem and remove stale faceplate-provider guidance.
- `src/OpcBridge.App/MappingRequests.cs`
  - Remove provider fields from new mapping writes if they are no longer part of the long-term public contract.
- `src/OpcBridge.App/MappingStore.cs`
  - Keep legacy provider fields readable for migration, but stop being the place for new provider-link persistence.
- `src/OpcBridge.Da/OpcTagBrowser.cs`
  - Extend browse results if needed to expose native type/access metadata for the new UI and validation prechecks.
- `tests/OpcBridge.LoadTest/ConnectedTagsTests.cs`
  - Replace legacy mapping-linked tests with migration coverage or move them to new link-store tests.
- `tests/OpcBridge.LoadTest/DaLinkStoreTests.cs`
  - New focused tests for normalization, migration, persistence, and conflict rules.
- `tests/OpcBridge.LoadTest/DaLinkApiTests.cs`
  - New focused tests for API validation.

### Task 1: Surface DA-native metadata for link candidates

**Files:**
- Modify: `src/OpcBridge.Da/OpcTagBrowser.cs`
- Modify: `src/OpcBridge.App/Program.cs`
- Test: `tests/OpcBridge.LoadTest/DaBrowseMetadataTests.cs`

**Interfaces:**
- Consumes: existing `OpcTagBrowseResult`, existing `/api/da/tags` endpoint
- Produces:
  - `public sealed record OpcTagNode(string Name, string ItemId, short? CanonicalDataType = null, int? AccessRights = null)`
  - `/api/da/tags` JSON includes `canonicalDataType` and `accessRights` for tags when known

- [ ] **Step 1: Write the failing test**

```csharp
using OpcBridge.Da;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class DaBrowseMetadataTests
{
    [Fact]
    public void OpcTagNode_CarriesNativeMetadata()
    {
        var node = new OpcTagNode(
            Name: "TagA",
            ItemId: "Channel.Device.TagA",
            CanonicalDataType: 5,
            AccessRights: 3);

        Assert.Equal((short)5, node.CanonicalDataType);
        Assert.Equal(3, node.AccessRights);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter DaBrowseMetadataTests`

Expected: FAIL because `OpcTagNode` does not expose `CanonicalDataType` and `AccessRights`.

- [ ] **Step 3: Write minimal implementation**

```csharp
public sealed record OpcTagNode(
    string Name,
    string ItemId,
    short? CanonicalDataType = null,
    int? AccessRights = null);
```

Also update the browse endpoint projection in `Program.cs` so the serialized `tags` array carries:

```csharp
return Results.Json(new
{
    branches = result.Branches,
    tags = result.Tags.Select(tag => new
    {
        name = tag.Name,
        itemId = tag.ItemId,
        canonicalDataType = tag.CanonicalDataType,
        accessRights = tag.AccessRights
    })
});
```

If the current browser cannot populate these values yet, thread nullable values through first and fill them in the same task from the DA browse/read path that already knows canonical type and access rights.

- [ ] **Step 4: Run test to verify it passes**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter DaBrowseMetadataTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.Da/OpcTagBrowser.cs src/OpcBridge.App/Program.cs tests/OpcBridge.LoadTest/DaBrowseMetadataTests.cs
git commit -m "feat: expose DA browse metadata for link validation"
```

### Task 2: Add `DaLinkRule` and `DaLinkStore` with legacy migration

**Files:**
- Create: `src/OpcBridge.App/DaLinkRule.cs`
- Create: `src/OpcBridge.App/DaLinkStore.cs`
- Modify: `src/OpcBridge.App/MappingStore.cs`
- Test: `tests/OpcBridge.LoadTest/DaLinkStoreTests.cs`

**Interfaces:**
- Consumes: `MappingStore.GetSnapshot()`, legacy `TagMapping.ProviderSourceId`, `TagMapping.ProviderDaItemId`
- Produces:
  - `public sealed record DaLinkRule(...)`
  - `public sealed class DaLinkStore`
  - `public (IReadOnlyList<DaLinkRule> Rules, long Version) GetSnapshot()`
  - `public long SetAll(IEnumerable<DaLinkRule> rules)`
  - `public bool TryAdd(DaLinkRule rule, out long version, out string? error)`
  - `public bool TryUpdate(DaLinkRule rule, out long version, out string? error)`
  - `public bool TryRemove(Guid id, out long version)`
  - `public int MigrateFromMappings(IEnumerable<TagMapping> mappings)`

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Options;
using OpcBridge.App;
using OpcBridge.Core;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class DaLinkStoreTests
{
    [Fact]
    public void MigrateFromMappings_CreatesOneRulePerLegacyProviderLink()
    {
        var mappings = new[]
        {
            new TagMapping
            {
                SourceId = "consumerA",
                DaItemId = "itemA",
                ProviderSourceId = "providerA",
                ProviderDaItemId = "itemP"
            }
        };

        var store = new DaLinkStore(Options.Create(new BridgeOptions()));

        int migrated = store.MigrateFromMappings(mappings);
        var (rules, _) = store.GetSnapshot();

        Assert.Equal(1, migrated);
        var rule = Assert.Single(rules);
        Assert.Equal("providerA", rule.ProviderSourceId);
        Assert.Equal("itemP", rule.ProviderItemId);
        Assert.Equal("consumerA", rule.ConsumerSourceId);
        Assert.Equal("itemA", rule.ConsumerItemId);
    }

    [Fact]
    public void TryAdd_RejectsSecondProviderForSameConsumer()
    {
        var store = new DaLinkStore(Options.Create(new BridgeOptions()));

        Assert.True(store.TryAdd(new DaLinkRule(
            Guid.NewGuid(), "p1", "itemP1", "consumerA", "itemA", true, 5, 5), out _, out _));

        bool ok = store.TryAdd(new DaLinkRule(
            Guid.NewGuid(), "p2", "itemP2", "consumerA", "itemA", true, 5, 5), out _, out string? error);

        Assert.False(ok);
        Assert.Equal("Consumer already has a provider.", error);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter DaLinkStoreTests`

Expected: FAIL because `DaLinkStore` and `DaLinkRule` do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace OpcBridge.App;

public sealed record DaLinkRule(
    Guid Id,
    string ProviderSourceId,
    string ProviderItemId,
    string ConsumerSourceId,
    string ConsumerItemId,
    bool Enabled,
    short? ProviderCanonicalType,
    short? ConsumerCanonicalType);
```

```csharp
public sealed class DaLinkStore
{
    private readonly object sync_ = new();
    private readonly string persist_path_;
    private List<DaLinkRule> rules_ = new();
    private long version_;

    public DaLinkStore(IOptions<BridgeOptions> options)
    {
        persist_path_ = Path.Combine(AppContext.BaseDirectory, "links.json");
    }

    public (IReadOnlyList<DaLinkRule> Rules, long Version) GetSnapshot()
    {
        lock (sync_) { return (rules_.ToArray(), version_); }
    }

    public int MigrateFromMappings(IEnumerable<TagMapping> mappings) { /* convert legacy provider fields */ }
    public bool TryAdd(DaLinkRule rule, out long version, out string? error) { /* normalize + conflict check + persist */ }
    public bool TryUpdate(DaLinkRule rule, out long version, out string? error) { /* normalize + persist */ }
    public bool TryRemove(Guid id, out long version) { /* remove + persist */ }
}
```

Keep `MappingStore` readable for migration, but do not add new link-writing behavior there.

- [ ] **Step 4: Run tests to verify they pass**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter DaLinkStoreTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.App/DaLinkRule.cs src/OpcBridge.App/DaLinkStore.cs src/OpcBridge.App/MappingStore.cs tests/OpcBridge.LoadTest/DaLinkStoreTests.cs
git commit -m "feat: add DA link store with legacy migration"
```

### Task 3: Add DA Links API and centralized validation

**Files:**
- Create: `src/OpcBridge.App/DaLinkRequests.cs`
- Modify: `src/OpcBridge.App/Program.cs`
- Test: `tests/OpcBridge.LoadTest/DaLinkApiTests.cs`

**Interfaces:**
- Consumes: `DaLinkStore`, DA browse/native metadata produced in Task 1
- Produces:
  - `public sealed record DaLinkDto(...)`
  - `public sealed record CreateDaLinkRequest(DaLinkDto Link)`
  - `GET /api/da-links`
  - `POST /api/da-links`
  - `PUT /api/da-links/{id}`
  - `DELETE /api/da-links/{id}`

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Options;
using OpcBridge.App;
using OpcBridge.Core;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class DaLinkApiTests
{
    [Fact]
    public void ValidateLink_RejectsTypeMismatch()
    {
        var store = new DaLinkStore(Options.Create(new BridgeOptions()));

        var request = new DaLinkDto(
            Id: Guid.NewGuid(),
            ProviderSourceId: "providerA",
            ProviderItemId: "itemP",
            ConsumerSourceId: "consumerA",
            ConsumerItemId: "itemC",
            Enabled: true,
            ProviderCanonicalType: 5,
            ConsumerCanonicalType: 3);

        string? error = DaLinkValidators.Validate(request, consumerHasProvider: false, providerReadable: true, consumerWritable: true);
        Assert.Equal("Provider and consumer must use the same native OPC DA type.", error);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter DaLinkApiTests`

Expected: FAIL because DTOs/validator/API do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
public sealed record DaLinkDto(
    Guid Id,
    string ProviderSourceId,
    string ProviderItemId,
    string ConsumerSourceId,
    string ConsumerItemId,
    bool Enabled,
    short? ProviderCanonicalType,
    short? ConsumerCanonicalType);
```

```csharp
internal static class DaLinkValidators
{
    public static string? Validate(
        DaLinkDto link,
        bool consumerHasProvider,
        bool providerReadable,
        bool consumerWritable)
    {
        if (string.Equals(link.ProviderSourceId, link.ConsumerSourceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(link.ProviderItemId, link.ConsumerItemId, StringComparison.OrdinalIgnoreCase))
            return "Provider and consumer cannot be the same tag.";
        if (!providerReadable)
            return "Provider tag must allow read.";
        if (!consumerWritable)
            return "Consumer tag must allow write.";
        if (consumerHasProvider)
            return "Consumer already has a provider.";
        if (link.ProviderCanonicalType != link.ConsumerCanonicalType)
            return "Provider and consumer must use the same native OPC DA type.";
        return null;
    }
}
```

Add minimal API endpoints in `Program.cs` that return explicit errors from the validator and persist through `DaLinkStore`.

- [ ] **Step 4: Run test to verify it passes**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter DaLinkApiTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.App/DaLinkRequests.cs src/OpcBridge.App/Program.cs tests/OpcBridge.LoadTest/DaLinkApiTests.cs
git commit -m "feat: add DA links API and validation"
```

### Task 4: Cut the runtime over from mapping-linked forwarding to `DaLinkStore`

**Files:**
- Modify: `src/OpcBridge.App/BridgeWorker.cs`
- Modify: `src/OpcBridge.App/Program.cs`
- Test: `tests/OpcBridge.LoadTest/ConnectedTagsTests.cs`

**Interfaces:**
- Consumes: `DaLinkStore.GetSnapshot()`, existing `WriteQueue`, existing `BridgeValue`
- Produces:
  - `BridgeWorker` provider->consumer propagation driven by `DaLinkRule`
  - removal of new runtime dependency on `TagMapping.ProviderSourceId` / `ProviderDaItemId`

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;

namespace OpcBridge.LoadTest;

public sealed partial class ConnectedTagsTests
{
    [Fact]
    public void RuntimeIndex_UsesDaLinkRules_NotMappingProviderFields()
    {
        var rules = new[]
        {
            new DaLinkRule(Guid.NewGuid(), "providerA", "itemP", "consumerA", "itemC", true, 5, 5)
        };

        var cache = SourceMappingCache.Build(
            mappings: new[]
            {
                new TagMapping { SourceId = "consumerA", DaItemId = "itemC", AccessRights = TagAccessRights.Write }
            },
            rules: rules);

        var consumers = cache.GetConsumersByProvider("providerA", "itemP");
        var consumer = Assert.Single(consumers);
        Assert.Equal("consumerA", consumer.SourceId);
        Assert.Equal("itemC", consumer.DaItemId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter ConnectedTagsTests`

Expected: FAIL because `SourceMappingCache.Build` still depends on mapping provider fields only.

- [ ] **Step 3: Write minimal implementation**

Update `BridgeWorker` so the provider index is built from `DaLinkRule` snapshots rather than from `TagMapping.ProviderSourceId` / `ProviderDaItemId`.

Core shape:

```csharp
public static SourceMappingCache Build(
    IReadOnlyList<TagMapping> mappings,
    IReadOnlyList<DaLinkRule> rules)
{
    Dictionary<string, List<TagMapping>> consumersByProvider = new(StringComparer.OrdinalIgnoreCase);

    foreach (DaLinkRule rule in rules.Where(r => r.Enabled))
    {
        TagMapping? consumer = mappings.FirstOrDefault(m =>
            string.Equals(m.SourceId, rule.ConsumerSourceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.DaItemId, rule.ConsumerItemId, StringComparison.OrdinalIgnoreCase));

        if (consumer is null)
            continue;

        string providerKey = GetMappingKey(rule.ProviderSourceId, rule.ProviderItemId);
        if (!consumersByProvider.TryGetValue(providerKey, out List<TagMapping>? consumers))
        {
            consumers = new List<TagMapping>();
            consumersByProvider[providerKey] = consumers;
        }

        consumers.Add(consumer);
    }

    return new SourceMappingCache(/* existing mapping groups */, consumersByProvider.ToDictionary(...));
}
```

At runtime, keep the existing write-queue execution path. Only the link source of truth changes.

- [ ] **Step 4: Run test to verify it passes**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter ConnectedTagsTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.App/BridgeWorker.cs src/OpcBridge.App/Program.cs tests/OpcBridge.LoadTest/ConnectedTagsTests.cs
git commit -m "refactor: drive DA propagation from link rules"
```

### Task 5: Replace the dashboard UI with a dedicated `DA Links` workflow

**Files:**
- Modify: `src/OpcBridge.App/DashboardPage.cs`
- Test: `tests/OpcBridge.LoadTest/DashboardPageTests.cs`

**Interfaces:**
- Consumes: `/api/da-links`, `/api/da/tags`, existing source selection state
- Produces:
  - `view-links` becomes `DA Links` editor/list surface
  - faceplate `Setup` pane no longer includes provider controls
  - JS helpers `loadDaLinks()`, `saveDaLink()`, `deleteDaLink()`

- [ ] **Step 1: Write the failing test**

```csharp
using OpcBridge.App;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class DashboardPageTests
{
    [Fact]
    public void ScriptAndMarkup_RemoveFaceplateProviderEditing()
    {
        Assert.DoesNotContain("id=\"fpProvider\"", DashboardPage.Html);
        Assert.DoesNotContain("Set up links from a tag's faceplate", DashboardPage.Html);
        Assert.Contains("DA Links", DashboardPage.Html);
        Assert.Contains("/api/da-links", DashboardPage.Script);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter DashboardPageTests`

Expected: FAIL because faceplate provider controls and old copy still exist.

- [ ] **Step 3: Write minimal implementation**

Update `DashboardPage.cs` markup and script:

```html
<div class="view" id="view-links">
    <div class="box">
        <div class="box-h">DA Links <span class="msg" id="linksCount" style="margin-left:auto"></span></div>
        <div class="box-b">
            <div class="hint" id="linksMessage" style="margin-bottom:10px">Create provider-consumer DA rules here. DA Links are separate from OPC UA tag mappings.</div>
            <!-- provider source/item selectors -->
            <!-- consumer source/item selectors -->
            <!-- create/update/delete buttons -->
            <div class="list" id="linksList"></div>
        </div>
    </div>
</div>
```

Remove from the faceplate `Setup` pane:

```html
<div class="field"><label class="fl">Provider</label><select id="fpProvider" data-action="tag-provider" style="flex:1"></select></div>
<div class="hint" style="margin-top:2px">Optional: another tag that feeds this tag's value...</div>
```

Add JS fetch helpers:

```javascript
async function loadDaLinks() {
  const data = await (await fetch('/api/da-links')).json();
  state.daLinks = data.rules || [];
  renderLinksView();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter DashboardPageTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.App/DashboardPage.cs tests/OpcBridge.LoadTest/DashboardPageTests.cs
git commit -m "feat: add dedicated DA links dashboard workflow"
```

### Task 6: Remove stale mapping-provider contract and update help/docs

**Files:**
- Modify: `src/OpcBridge.App/MappingRequests.cs`
- Modify: `src/OpcBridge.App/Program.cs`
- Modify: `src/OpcBridge.App/HelpContent.cs`
- Modify: `tests/OpcBridge.LoadTest/ConnectedTagsTests.cs`
- Test: `tests/OpcBridge.LoadTest/HelpContentTests.cs`

**Interfaces:**
- Consumes: new `DaLinkStore`/API/UI from Tasks 2-5
- Produces:
  - help and API docs aligned to `DA Links`
  - mapping update payloads no longer used for provider-link editing
  - regression coverage for legacy migration instead of stale `TagMapping` provider behavior

- [ ] **Step 1: Write the failing test**

```csharp
using OpcBridge.App;
using Xunit;

namespace OpcBridge.LoadTest;

public sealed class HelpContentTests
{
    [Fact]
    public void HelpText_DescribesDaLinksAsIndependentSubsystem()
    {
        Assert.Contains("DA Links", HelpContent.Markdown);
        Assert.Contains("separate subsystem", HelpContent.Markdown);
        Assert.DoesNotContain("faceplate → Setup → Provider", HelpContent.Markdown);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter HelpContentTests`

Expected: FAIL because help content still documents the old connected-tags model.

- [ ] **Step 3: Write minimal implementation**

- Remove provider fields from `MappingTagDto` if the mapping endpoints should no longer accept new provider edits.
- Keep compatibility logic only where needed for migration.
- Rewrite the help section from “Connected Tags” to “DA Links”, covering:
  - independent subsystem
  - shared DA runtime
  - cross-source support
  - canonical DA type compatibility
  - one-provider-per-consumer v1 rule

Example help text replacement:

```markdown
# DA Links

DA Links are separate from DA -> UA mappings. A provider change on one OPC DA source can write directly to a consumer on another OPC DA source through the bridge's shared DA runtime.
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `docker run --rm -v ~/OpcDaToUaBridge:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test tests/OpcBridge.LoadTest/OpcBridge.LoadTest.csproj -c Release --filter "HelpContentTests|ConnectedTagsTests"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpcBridge.App/MappingRequests.cs src/OpcBridge.App/Program.cs src/OpcBridge.App/HelpContent.cs tests/OpcBridge.LoadTest/ConnectedTagsTests.cs tests/OpcBridge.LoadTest/HelpContentTests.cs
git commit -m "refactor: remove stale mapping-linked provider workflow"
```

## Self-Review

### Spec coverage
- Separate top-level `DA Links` subsystem: Task 5
- Shared DA runtime underneath: Task 4
- Independent persistence outside `TagMapping`: Task 2
- Native canonical DA type validation: Tasks 1 and 3
- Cross-source allowed: Tasks 3 and 4
- One provider -> many consumers, reject many providers -> one consumer: Task 2 and Task 3
- Remove faceplate editing path: Task 5
- Legacy migration from mapping provider fields: Task 2 and Task 6
- Help text update: Task 6

No spec gaps found.

### Placeholder scan
- No `TBD`, `TODO`, or “similar to Task N” placeholders remain.
- Every task includes explicit files, interfaces, test code, commands, and commit steps.

### Type consistency
- `DaLinkRule` uses `ProviderItemId` / `ConsumerItemId` consistently.
- `DaLinkDto` mirrors the same provider/consumer naming.
- `DaLinkStore` is the persistence source of truth after migration.
- `BridgeWorker` consumes `IReadOnlyList<DaLinkRule>` snapshots rather than `TagMapping.Provider*` fields.
