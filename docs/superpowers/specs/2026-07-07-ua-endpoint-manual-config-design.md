# Manual OPC UA Endpoint Configuration Design

## Goal

Require the user to manually enter and save the OPC UA endpoint on first install, or whenever the host has no saved endpoint, instead of silently using the built-in `opc.tcp://0.0.0.0:4840/OpcDaToUaBridge` default.

## Problem

Today the bridge always has a usable OPC UA endpoint because `UaServerOptions.EndpointUrl` and `appsettings.json` both carry a built-in default. That creates two problems:

1. A new install starts with an endpoint the user did not choose.
2. The dashboard exposes the endpoint as read-only status instead of editable configuration.

The requested behavior is stricter: if no endpoint is saved on the host, the user must enter one manually before the OPC UA server is considered configured.

## Scope

In scope:
- Make OPC UA endpoint editable from the dashboard.
- Persist endpoint changes to host `appsettings.json`.
- Treat missing/blank endpoint as not configured.
- Prevent UA server startup when endpoint is missing.
- Show clear first-run / missing-endpoint state in the dashboard.
- Keep config export/import carrying the endpoint.
- Update help text to describe the new first-run requirement.

Out of scope:
- Editing other UA settings beyond the endpoint.
- Multi-endpoint support.
- Automatic endpoint generation from host IP.
- Full onboarding wizard or modal flow.
- Changes to DA runtime settings behavior.

## Existing System

### Current configuration flow

- `src/OpcBridge.App/appsettings.json` defines `Ua:EndpointUrl` with a concrete default value.
- `src/OpcBridge.Ua/UaServerOptions.cs` also carries a concrete default value.
- `src/OpcBridge.App/Program.cs` already exposes UA status through `/api/status` and `/api/dashboard`.
- `src/OpcBridge.App/Program.cs` already exports and imports `endpointUrl` through `/api/config/export` and `/api/config/import`.
- `src/OpcBridge.App/DashboardPage.cs` shows the endpoint in Monitor as read-only text.
- `src/OpcBridge.Ua/UaServerHost.cs` starts the UA server using `options_.EndpointUrl` as a base address.

### Constraint from existing UX

The dashboard already acts as the operator control surface for DA sources, update rate, subscriptions, mappings, and config backup/restore. The endpoint setting should follow the same pattern instead of forcing file edits.

## Proposed Design

### 1. Endpoint becomes explicit dashboard configuration

Add an `OPC UA Endpoint` section to the existing Connection tab. The section contains:
- a text input for the endpoint URL
- a save/apply button
- an inline status message

The input is the source of truth for operators. It is preloaded from the current saved host configuration.

Behavior:
- If a saved endpoint exists, show it in the field.
- If no saved endpoint exists, show an empty field and a message that the endpoint must be configured before UA clients can connect.
- Saving a valid endpoint persists it immediately and refreshes displayed UA status.

### 2. Missing endpoint means “not configured”

A blank or whitespace-only endpoint is a valid persisted state for the application configuration, but it means the UA server is not configured.

When the endpoint is blank:
- `UaServerHost` must not start the UA server.
- UA status must report a non-running state that is distinct from a runtime failure, e.g. `Not configured`.
- The dashboard Monitor tab must not show a fake endpoint value.
- The dashboard Connection tab must instruct the user to save an endpoint.

This keeps first-run behavior honest. The server is unavailable because it has not been configured yet, not because it failed after trying a hidden fallback.

### 3. Remove baked-in endpoint fallback

The system should stop manufacturing a usable endpoint from defaults.

Required changes:
- Remove the concrete default endpoint from `UaServerOptions.EndpointUrl`.
- Remove the concrete default endpoint from `src/OpcBridge.App/appsettings.json` for new installs.
- Treat `null`, empty, or whitespace endpoint values as missing configuration.

The only way a usable endpoint appears is:
- the user saves one in the dashboard, or
- an imported config provides one, or
- the host file already contains one.

### 4. Add dedicated UA settings API

Add read/write endpoints specifically for UA settings.

#### GET `/api/ua/settings`
- `endpointUrl`
- `configured` boolean
- `state`

Use:
- preload the Connection tab field
- support first-run messaging without scraping Monitor state

#### POST `/api/ua/endpoint`
Request body:
```json
{ "endpointUrl": "opc.tcp://192.168.20.13:4840/OpcDaToUaBridge" }
```

Behavior:
- trim input
- validate required/non-empty
- validate absolute URI
- validate scheme is `opc.tcp`
- persist to `appsettings.json`
- restart or reinitialize only the UA server component
- return updated saved value and status

Failure behavior:
- reject blank values
- reject malformed URIs
- reject non-`opc.tcp` schemes
- preserve existing saved value on validation failure

## Persistence Rules

The host `appsettings.json` remains the persisted source of truth.

Rules:
- Saving endpoint from dashboard updates `Ua:EndpointUrl` in `appsettings.json`.
- Importing config continues to update `Ua:EndpointUrl` through the existing import pipeline.
- Exporting config continues to include `endpointUrl`.
- Blank imported endpoint leaves the app in not-configured state.

Password-style masking is not needed. Endpoint is not secret.

## Dashboard UX

### Connection tab

Add a new section in the existing Connection layout, directly below `DA Subscriptions` and above the save/reset/new/remove toolbar.

Field behavior:
- placeholder example: `opc.tcp://192.168.20.13:4840/OpcDaToUaBridge`
- empty on missing config
- current saved endpoint when configured

Status copy:
- missing: `OPC UA endpoint required before the UA server can start.`
- saved: `Saved. UA server restarting with new endpoint.`
- invalid: concrete parse/validation error

### Monitor tab

Monitor remains status-oriented.

Behavior:
- configured: show current endpoint normally
- not configured: show `Not configured` explicitly instead of `—` or a stale prior value
- diagnostics text must distinguish configuration-missing from runtime failure

## Validation

Accepted values must satisfy all of the following:
- non-empty after trimming
- valid absolute URI
- scheme exactly `opc.tcp`

Rejected examples:
- empty string
- `4840/OpcDaToUaBridge`
- `http://host:4840/OpcDaToUaBridge`
- `opc.tcp://`

Accepted example:
- `opc.tcp://192.168.20.13:4840/OpcDaToUaBridge`

This design does not add hostname reachability validation. Syntax validation is enough for save time; actual bind/start failures remain runtime concerns.

## Runtime Behavior

### First install / blank host config

1. App starts.
2. Dashboard loads.
3. UA settings API reports blank endpoint and `configured = false`.
4. Connection tab shows empty input and required message.
5. UA server remains stopped.
6. After user saves a valid endpoint, the app persists it and starts or restarts the UA server with that endpoint.

### Existing configured install

1. App starts with saved endpoint.
2. Connection tab preloads the current endpoint.
3. Monitor shows the active endpoint.
4. User may edit and resave it.
5. The UA server restarts onto the new endpoint.

## Error Handling

Configuration errors:
- blank endpoint -> HTTP 400 with explicit message
- invalid URI -> HTTP 400 with explicit message
- wrong scheme -> HTTP 400 with explicit message

Runtime restart errors after a syntactically valid save:
- persist the new endpoint first
- attempt UA server restart immediately after persistence
- if restart fails, return the failure and expose it in status/logs while keeping the saved endpoint intact

This mirrors operator intent better than silently rolling back the file.

## Files Affected

### Modify
- `src/OpcBridge.App/DashboardPage.cs`
  - add Connection-tab endpoint UI
  - load/save endpoint via JS
  - show missing-config messaging
- `src/OpcBridge.App/Program.cs`
  - add `/api/ua/settings`
  - add `/api/ua/endpoint`
  - reuse existing appsettings persistence pattern
- `src/OpcBridge.Ua/UaServerHost.cs`
  - skip startup when endpoint missing
  - expose not-configured status
  - restart on endpoint update
- `src/OpcBridge.Ua/UaServerOptions.cs`
  - remove concrete default endpoint
- `src/OpcBridge.App/HelpContent.cs`
  - document manual endpoint setup on first install
- `src/OpcBridge.App/appsettings.json`
  - blank out `Ua:EndpointUrl` for new installs

### Add
- `src/OpcBridge.App/UaEndpointRequest.cs`
  - request DTO for endpoint update API

## Testing Strategy

### API behavior
- GET `/api/ua/settings` returns blank endpoint and `configured = false` when config is missing.
- POST `/api/ua/endpoint` rejects blank input.
- POST `/api/ua/endpoint` rejects malformed or wrong-scheme input.
- POST `/api/ua/endpoint` accepts valid `opc.tcp://...` input and returns updated value.

### Runtime behavior
- With blank endpoint in host config, UA server status is `Not configured` and no usable endpoint is reported.
- After saving a valid endpoint, UA server status reports running state with the saved endpoint.

### Dashboard behavior
- Connection tab shows empty endpoint field on first run.
- Saving a valid endpoint updates status message.
- Monitor tab reflects configured vs not-configured state.

### Verification commands
- `node --check` on extracted dashboard script
- `dotnet build OpcDaToUaBridge.sln`
- Windows publish for deploy target with zero warnings and zero errors

## Non-Goals and Tradeoffs

Chosen tradeoff:
- first-run requires explicit user action

Benefits:
- matches the requested operator workflow
- no hidden binding target
- reduces confusion between listen address and client connection address

Cost:
- new install is intentionally incomplete until configured
- slightly more startup-state branching in `UaServerHost`

This is acceptable because the requirement is explicit manual endpoint entry, not convenience autoconfiguration.

## Acceptance Criteria

1. On a host with blank or missing `Ua:EndpointUrl`, the dashboard shows an empty editable endpoint field.
2. On that host, the UA server does not start and the dashboard reports not-configured state.
3. Saving a valid `opc.tcp://...` endpoint persists it to host config and starts or restarts the UA server with that endpoint.
4. Saving invalid or blank endpoint values fails with clear inline errors and does not overwrite the saved good value.
5. Config export/import continues to carry the endpoint value.
6. Help text explains that first install requires manual OPC UA endpoint setup.
7. Verification passes with `node --check`, Linux `dotnet build OpcDaToUaBridge.sln`, and Windows publish with `0 Warning(s), 0 Error(s)`.
