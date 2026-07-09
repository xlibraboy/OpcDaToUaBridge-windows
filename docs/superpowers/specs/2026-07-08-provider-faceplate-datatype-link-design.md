# DA Links Subsystem Design

## Goal

Separate OPC DA provider-consumer communication from the OPC DA -> OPC UA bridge as its own subsystem, with its own tab, APIs, persistence, and validation, while reusing the existing OPC DA source runtime underneath.

## Problem

The current connected-tags feature works by attaching provider fields directly to UA tag mappings:

- provider link data lives on `TagMapping`
- link editing is exposed through the existing dashboard mapping surfaces
- runtime forwarding is described as an extension of mapped tags instead of as its own DA-to-DA feature

That creates the wrong product boundary.

Provider-consumer behavior is not really part of UA publishing. It is a separate DA-to-DA automation feature that happens to reuse the same DA source connections. Keeping it embedded in UA mapping makes the UI harder to understand, couples link rules to UA tag configuration, and limits future growth.

## Scope

In scope:
- create a first-class `DA Links` subsystem in the dashboard
- give it its own menu/tab, persistence model, API, and runtime validation
- keep provider and consumer on different OPC DA sources / servers when valid
- reuse the current DA source connections, polling/subscriptions, and write queues
- validate link compatibility using DA-native type metadata where available
- remove provider-consumer editing from the UA tag faceplate workflow
- document the new subsystem in help text

Out of scope:
- creating separate OPC DA client instances just for links
- routing DA links through OPC UA nodes
- many-provider-to-one-consumer conflict resolution in v1
- arbitrary type coercion between incompatible values
- replacing the existing DA->UA mapping subsystem

## Existing System

### Current runtime model

- DA sources already exist as the communication boundary.
- The bridge already maintains per-source DA reads/subscriptions and per-source write queues.
- Cross-source forwarding already works because the consumer write is scheduled on the consumer source queue.

### Current link model

- `src/OpcBridge.Core/TagMapping.cs` carries `ProviderSourceId` and `ProviderDaItemId`.
- `src/OpcBridge.App/BridgeWorker.cs` builds a provider-to-consumer index from mappings and forwards provider values into consumer writes.
- The dashboard exposes this through the current `Links` tab and through faceplate editing.

### Current DA type information

The DA layer already exposes native type metadata suitable for validation:
- `OpcDaClient` receives `CanonicalDataType` from OPC item results.
- The DA stack already maps requested data types through COM `VarEnum` values.

That means the new subsystem does not need to depend on UA mapping `DataType` as its compatibility rule.

## Proposed Design

### 1. Create a separate top-level `DA Links` subsystem

Treat DA provider-consumer links as a first-class feature beside, not inside, the DA->UA bridge.

Dashboard shape:
- keep `Tags` focused on DA->UA mappings
- add or repurpose a top-level tab named `DA Links`
- remove per-tag provider editing from the faceplate path

The `DA Links` tab is the operator surface for:
- creating a provider-consumer rule
- enabling/disabling a rule
- viewing current rule status
- removing a rule
- seeing last error / last propagated value / last activity later if needed

This is the key design decision: the feature boundary moves from “tag mapping option” to “independent DA automation subsystem”.

### 2. Reuse the existing DA runtime

Do not create a second OPC DA communication engine.

The new subsystem must reuse:
- existing DA source connections
- existing polling/subscription updates
- existing per-source write queues
- existing access-rights knowledge gathered by the DA layer

Runtime flow:

```text
OPC DA source change/read
  -> shared DA runtime publishes provider value event
  -> DA Links subsystem matches enabled rules for that provider
  -> consumer write request enqueued on consumer source write queue
  -> OPC DA write executed against consumer source
```

This preserves the clean subsystem separation at the feature level without duplicating COM/DCOM sessions, reads, or subscriptions.

### 3. Introduce an independent persistence model

Provider-consumer links should no longer live on `TagMapping`.

Add a dedicated model, e.g. `DaLinkRule`, persisted separately from UA mappings.

Suggested fields:
- `id`
- `providerSourceId`
- `providerItemId`
- `consumerSourceId`
- `consumerItemId`
- `enabled`
- `providerCanonicalType`
- `consumerCanonicalType`
- optional operator label later

The important point is not the exact field names. The important point is that DA links become their own stored object graph instead of piggybacking on UA mappings.

Persistence should be separate from `mappings.json`, for example a dedicated `links.json` file.

### 4. Use DA-native type compatibility

For this subsystem, the best compatibility rule is the DA item's native or canonical type, not the UA mapping `DataType`.

Reason:
- the feature is now independent from UA mapping
- the DA layer already exposes canonical type metadata
- native DA type compatibility is the direct contract for DA write safety

Validation rule:
- provider and consumer must have the same compatible canonical DA type

Initial v1 rule should be strict and boring:
- exact canonical type match required
- no automatic coercion
- no numeric-family compatibility shortcuts

This gives deterministic operator behavior and avoids hidden conversions.

### 5. Centralize validation in the new links API

Every rule create/update path must validate:
- provider exists
- consumer exists
- provider and consumer are not the same source+item
- provider is readable
- consumer is writable
- provider and consumer canonical DA types are compatible
- consumer is not already driven by another provider in v1

Chosen v1 constraint:
- allow one provider -> many consumers
- reject many providers -> one consumer

Reason:
- one-consumer-many-providers needs arbitration semantics that the current system does not have
- delaying that decision keeps the first subsystem correct and predictable

### 6. UI behavior in the `DA Links` tab

The tab should contain:
- provider source selector
- provider item selector
- consumer source selector
- consumer item selector
- rule create/apply button
- active rules list
- inline validation/status message area

Each active rule row should show:
- provider source / item
- consumer source / item
- enabled state
- canonical type
- last error or status indicator

Optional future columns:
- last propagated timestamp
- last propagated value
- propagation count

### 7. Remove provider editing from the faceplate workflow

The faceplate should stop being an editing surface for DA provider-consumer rules.

Reason:
- it belongs to UA mapping workflow, not link workflow
- the new subsystem has its own identity and should not be split across two control surfaces
- one place to edit rules avoids drift between tag-local and global editing models

### 8. Migration approach

Existing provider fields on `TagMapping` should be treated as old-format configuration.

Migration strategy:
- read old provider fields at startup if present
- convert them into `DaLinkRule` records once
- persist the converted rules into the new link store
- stop writing provider fields back into `TagMapping`

After migration:
- UA mappings remain only UA mappings
- DA links remain only DA links

Whether the old fields are left temporarily for compatibility or removed immediately is an implementation detail, but the design target is clean separation.

## Validation Rules

For a rule `Provider -> Consumer`:

| Rule | Required |
|------|----------|
| Provider exists | Yes |
| Consumer exists | Yes |
| Same source+item | Must be false |
| Provider readable | Yes |
| Consumer writable | Yes |
| Canonical DA type match | Yes |
| Consumer already has another provider | No |

Readable provider:
- DA access rights include read

Writable consumer:
- DA access rights include write

## API and Persistence Behavior

Recommended API shape:
- `GET /api/da-links`
- `POST /api/da-links`
- `PUT /api/da-links/{id}`
- `DELETE /api/da-links/{id}`
- optional `POST /api/da-links/validate`

Persistence:
- add dedicated `links.json`
- keep `mappings.json` for DA->UA mappings only
- do not store new DA links on `TagMapping`

## Files Affected

### Modify
- `src/OpcBridge.App/DashboardPage.cs`
  - make `DA Links` a first-class subsystem UI
  - remove provider editing from the faceplate workflow
- `src/OpcBridge.App/Program.cs`
  - add DA links API endpoints
  - wire link store and migration path
- `src/OpcBridge.App/BridgeWorker.cs`
  - consume independent link rules instead of provider fields on mappings
- `src/OpcBridge.App/HelpContent.cs`
  - document DA Links as a separate subsystem
- `src/OpcBridge.App/MappingStore.cs`
  - stop being the home for new provider-link persistence after migration

### Add
- `src/OpcBridge.App/DaLinkRule.cs`
  - independent DA link model
- `src/OpcBridge.App/DaLinkStore.cs`
  - load/save `links.json`
- `src/OpcBridge.App/DaLinkRequests.cs`
  - request DTOs for create/update

### Confirm existing DA metadata sources
- `src/OpcBridge.Da/OpcDaClient.cs`
- `src/OpcBridge.Da/OpcTagBrowser.cs`

## Testing Strategy

### Migration tests
- startup converts old provider fields into `DaLinkRule` records
- migrated rules persist into the new store
- UA mappings remain intact after migration

### Backend validation tests
- same-type cross-source rule succeeds
- different-type rule fails
- unreadable provider fails
- unwritable consumer fails
- self-link fails
- second provider for same consumer fails

### Runtime behavior tests
- provider update writes to consumer on another source
- disabled rule does not propagate
- invalid rule is never persisted or scheduled

### Dashboard behavior
- faceplate no longer edits provider-consumer links
- `DA Links` tab can create and delete rules
- validation messages are explicit and local to the tab

### Verification commands
- `node --check` on extracted dashboard script
- `dotnet build OpcDaToUaBridge.sln`
- targeted tests for migration and DA link validation
- Windows publish for deploy target

## Tradeoffs

Chosen approach:
- independent DA Links subsystem
- shared DA runtime underneath

Benefits:
- clear product boundary
- lower runtime risk than a second DA engine
- better room for future automation features
- link validation no longer depends on UA mapping decisions

Rejected approach:
- separate direct DA engine just for links

Reason rejected:
- duplicate COM/DCOM sessions
- duplicate reads/subscriptions
- more load on OPC DA servers
- harder conflict and failure handling

## Acceptance Criteria

1. DA provider-consumer rules are managed from a dedicated top-level `DA Links` subsystem, not the tag faceplate.
2. DA->UA mappings remain a separate feature and data model.
3. The DA Links subsystem reuses the existing DA source runtime and write queues.
4. Cross-source provider-consumer rules are allowed.
5. A rule can be saved only when provider and consumer have compatible native canonical DA types.
6. A consumer cannot have more than one provider in v1.
7. New DA links persist outside `TagMapping` and outside `mappings.json`.
8. Existing provider fields can be migrated into the new subsystem.
9. Help text explains DA Links as an independent feature from the OPC DA -> OPC UA bridge.
