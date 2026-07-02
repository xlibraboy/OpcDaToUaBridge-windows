# Dashboard Sticky Header / Tab Menu Design

## Goal
Keep the dashboard's top title bar and left tab menu visible at all times; only the main content area should scroll.

## Affected Elements
- `.topbar` — header containing brand title, status pills, clock.
- `.tabbar` — left sidebar containing tab buttons (Monitor, Connection, Diagnostics, Tags, Logs, Help, About).
- `.app-shell` — flex container holding `.tabbar` + `.content`.
- `.content` — main scrollable area containing active view panels.

## Selected Approach: Flex Layout with Internal Scrolling (Approach A)

### Rationale
Avoids `position: fixed` and `position: sticky`, which can cause z-index issues, mobile fragility, and browser inconsistencies. Uses the existing flex shell and simply constrains the scrollable region to `.content`.

### CSS Changes
1. `body`
   - `display: flex;`
   - `flex-direction: column;`
   - `height: 100vh;`
   - `overflow: hidden;`
2. `.topbar`
   - No structural change; remains at the top of the flex column.
3. `.app-shell`
   - `flex: 1;`
   - `min-height: 0;` (required so flex child can shrink below content size)
   - `overflow: hidden;`
   - Remove `min-height: calc(100vh - 46px);`
4. `.tabbar`
   - `overflow-y: auto;` (allow tab menu to scroll if viewport is too short)
5. `.content`
   - `overflow-y: auto;` (only this area scrolls)
   - Keep `min-width: 0;`

### Mobile Behavior
- Existing `@media (max-width: 600px)` rule already converts `.tabbar` to a horizontal row.
- With `overflow-y: auto` on `.tabbar`, a very short viewport can scroll the tab bar vertically; on mobile it already scrolls horizontally via `overflow-x: auto`. No conflict.

### Accessibility / Usability
- Header and navigation remain accessible without scrolling back to top.
- Scrollbar appears only in the content area, preserving screen real estate.

## Rejected Alternatives

### B. Sticky Positioning
- Required `position: sticky` on `.topbar` and `.tabbar`.
- Sticky inside a flex parent is unreliable without careful `overflow` management.
- Could break in some browsers if `.app-shell` ever gains `overflow-x: auto`.

### C. Fixed Positioning
- Required `position: fixed` for both elements and hard-coded margins on `.content`.
- Introduces fragility with modal overlays, tooltips, and responsive breakpoints.
- Width/height constants would be duplicated in CSS.

## Verification
1. Linux Docker build: `dotnet build OpcDaToUaBridge.sln` → 0 warnings, 0 errors.
2. JS validation: extract dashboard JS and run `node --check`.
3. Windows host: deploy, hard-refresh browser, scroll long content, confirm header and tab menu remain visible.
