# Dashboard Sticky Header Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the dashboard header and left tab menu visible while only the main content area scrolls.

**Architecture:** Use a flex-column body with `height: 100vh`, let `.app-shell` fill remaining space, and move vertical scrolling to `.content`. No `position: fixed` or `position: sticky` is needed.

**Tech Stack:** C# raw-string HTML/CSS in `src/OpcBridge.App/DashboardPage.cs`; .NET 8; Docker build verification.

## Global Constraints

- Target framework: `net8.0-windows` for DA, `net8.0` for cross-platform projects.
- Dual-platform build must pass with 0 warnings, 0 errors (Linux Docker + Windows publish).
- CA1416 warnings fixed with `[SupportedOSPlatform("windows")]` / inverted guards; not applicable for this UI-only change.
- Dashboard JS must validate with `node --check` after any JS edits.
- No SQLite; config stays JSON-only.
- Commit and push to both remotes (`origin` and `win`) after verification.

---

### Task 1: Update CSS in DashboardPage.cs

**Files:**
- Modify: `src/OpcBridge.App/DashboardPage.cs` (the CSS block near the top of the raw-string HTML)

**Interfaces:**
- Consumes: existing `.topbar`, `.app-shell`, `.tabbar`, `.content`, `.view` selectors.
- Produces: updated layout rules so `.content` becomes the sole scrollable region.

- [ ] **Step 1: Read current CSS block**

Locate these rules in `DashboardPage.cs`:
```css
body { background: var(--bg); color: var(--text); font-size: 13px; }
.app-shell { display: flex; min-height: calc(100vh - 46px); }
.tabbar { display: flex; flex-direction: column; background: var(--panel); border-right: 1px solid var(--border2); padding: 8px 0; width: 152px; flex-shrink: 0; }
.content { flex: 1; min-width: 0; overflow-x: auto; }
```

- [ ] **Step 2: Apply CSS changes**

Replace/augment the rules above so the layout becomes:
```css
body { background: var(--bg); color: var(--text); font-size: 13px; display: flex; flex-direction: column; height: 100vh; overflow: hidden; }
.app-shell { display: flex; flex: 1; min-height: 0; overflow: hidden; }
.tabbar { display: flex; flex-direction: column; background: var(--panel); border-right: 1px solid var(--border2); padding: 8px 0; width: 152px; flex-shrink: 0; overflow-y: auto; }
.content { flex: 1; min-width: 0; overflow: auto; }
```

- [ ] **Step 3: Verify no `</div>` tags were disturbed**

The HTML structure after the CSS block must remain:
```html
<div class="topbar">...</div>
<div class="app-shell">
  <div class="tabbar">...</div>
  <div class="content">...</div>
</div>
```

- [ ] **Step 4: Run Linux Docker build**

Run:
```bash
docker run --rm -v "$PWD":/workspace -w /workspace mcr.microsoft.com/dotnet/sdk:8.0 dotnet build OpcDaToUaBridge.sln
```

Expected output ends with:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Task 2: Validate Dashboard JavaScript

**Files:**
- Read from live app or extract from `DashboardPage.cs`.

**Interfaces:**
- Consumes: dashboard HTML served by the running app.
- Produces: confirmation that the JavaScript still parses after any dashboard changes.

- [ ] **Step 1: Extract JavaScript for validation**

If the app is running locally or on the Windows host, fetch the dashboard HTML and extract the `<script>` content into a file. If not running, the JS is unchanged by this CSS-only task; still run `node --check` on the extracted script as a sanity check.

- [ ] **Step 2: Run node --check**

```bash
node --check /tmp/dashboard.js
```

Expected: no output (success) or only pre-existing issues unrelated to this change.

### Task 3: Deploy and Verify on Windows Host

**Files:**
- Build output in `publish/` directory.

**Interfaces:**
- Consumes: built `win-x86` publish artifacts.
- Produces: running bridge with pinned header and tab menu.

- [ ] **Step 1: Publish win-x86**

```bash
dotnet publish src/OpcBridge.App -c Release -o publish -r win-x86 --self-contained false
```

- [ ] **Step 2: Package and copy to xlibr-win**

```bash
tar -czf /tmp/publish.tar.gz -C publish .
scp /tmp/publish.tar.gz xlibr-win:C:/OpcDaToUaBridge/publish.tar.gz
```

- [ ] **Step 3: Remote deploy and health check**

Run via SSH:
```powershell
powershell -NoProfile -Command "
    schtasks /end /tn OpcDaToUaBridge 2>$null;
    Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue;
    Start-Sleep -Seconds 2;
    tar -xzf C:\OpcDaToUaBridge\publish.tar.gz -C C:\OpcDaToUaBridge\publish;
    Remove-Item C:\OpcDaToUaBridge\publish.tar.gz;
    Remove-Item C:\OpcDaToUaBridge\publish\bridge-task-*.log -ErrorAction SilentlyContinue;
    schtasks /run /tn OpcDaToUaBridge;
    Start-Sleep -Seconds 8;
    (Invoke-RestMethod http://127.0.0.1:8080/health -TimeoutSec 5).status
"
```

Expected output: `ok`

- [ ] **Step 4: Visual verification**

Open the dashboard in a browser. Scroll within any long tab (e.g., Tags, Logs, Help). Confirm:
- The top bar (brand, pills, clock) stays visible.
- The left tab menu stays visible.
- Only the main content area scrolls.

### Task 4: Commit and Push

**Files:**
- `src/OpcBridge.App/DashboardPage.cs`
- `docs/superpowers/specs/2026-07-02-sticky-header-design.md`
- `docs/superpowers/plans/2026-07-02-sticky-header.md`

- [ ] **Step 1: Stage and commit**

```bash
git add -A
git commit -m "feat(dashboard): keep header and tab menu static while scrolling"
```

- [ ] **Step 2: Push to both remotes**

```bash
git push origin main
git push win main
```

Expected: both pushes succeed without conflicts.
