namespace OpcBridge.App;

internal static class DashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>OPC Bridge</title>
    <style>
        :root {
            color-scheme: dark;
            --bg: #0a0e14;
            --panel: #11161f;
            --panel2: #161c27;
            --border: #232b38;
            --border2: #2e3848;
            --text: #d8e0ea;
            --muted: #6b7689;
            --good: #34d399;
            --bad: #f87171;
            --warn: #fbbf24;
            --accent: #38bdf8;
            font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, sans-serif;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { background: var(--bg); color: var(--text); font-size: 13px; }
        .mono { font-family: 'Consolas', 'SF Mono', monospace; }
        .topbar { display: flex; align-items: center; gap: 14px; padding: 0 18px; height: 46px; background: var(--panel); border-bottom: 1px solid var(--border2); }
        .brand { display: flex; align-items: center; gap: 9px; font-weight: 600; font-size: 14px; white-space: nowrap; }
        .dot { width: 9px; height: 9px; border-radius: 50%; background: var(--good); }
        .dot.off { background: var(--bad); }
        .pills { display: flex; gap: 7px; margin-left: 8px; flex-wrap: wrap; }
        .pill { display: flex; align-items: center; gap: 6px; background: var(--panel2); border: 1px solid var(--border); border-radius: 5px; padding: 3px 9px; font-size: 12px; white-space: nowrap; }
        .pill b { font-weight: 600; }
        .pill .k { color: var(--muted); text-transform: uppercase; font-size: 10px; letter-spacing: .05em; }
        .topbar .clock { margin-left: auto; color: var(--muted); font-size: 11px; white-space: nowrap; }
        .tabbar { display: flex; background: var(--panel); border-bottom: 1px solid var(--border2); padding: 0 12px; }
        .tabbtn { background: none; border: none; color: var(--muted); padding: 11px 18px; font-size: 13px; font-weight: 500; cursor: pointer; border-bottom: 2px solid transparent; display: flex; align-items: center; gap: 7px; }
        .tabbtn.active { color: var(--accent); border-bottom-color: var(--accent); }
        .view { display: none; padding: 16px 18px; }
        .view.active { display: block; }
        .grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
        @media (max-width: 900px) { .grid2 { grid-template-columns: 1fr; } }
        .box { background: var(--panel); border: 1px solid var(--border); border-radius: 7px; overflow: hidden; }
        .box-h { padding: 9px 14px; background: var(--panel2); border-bottom: 1px solid var(--border); font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: .07em; color: var(--muted); display: flex; align-items: center; gap: 8px; }
        .box-b { padding: 12px 14px; }
        .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 10px; margin-bottom: 14px; }
        .stat { background: var(--panel); border: 1px solid var(--border); border-radius: 7px; padding: 11px 13px; }
        .stat .k { color: var(--muted); font-size: 10px; text-transform: uppercase; letter-spacing: .06em; }
        .stat .v { margin-top: 6px; font-size: 16px; font-weight: 700; line-height: 1.1; }
        .stat .s { margin-top: 4px; color: var(--muted); font-size: 11px; }
        .mini-meter { margin-top: 7px; }
        .mini-meter-track { height: 6px; border-radius: 999px; background: var(--panel2); border: 1px solid var(--border); overflow: hidden; }
        .mini-meter-fill { height: 100%; width: 0%; background: var(--good); transition: width .2s ease, background-color .2s ease; }
        .mini-meter-fill.warn { background: var(--warn); }
        .mini-meter-fill.bad { background: var(--bad); }
        .badge { display: inline-flex; align-items: center; gap: 5px; padding: 1px 8px; border-radius: 10px; font-size: 12px; font-weight: 600; }
        .badge::before { content:''; width:6px; height:6px; border-radius:50%; background:currentColor; }
        .badge.good { color: var(--good); background: rgba(52,211,153,.12); }
        .badge.bad { color: var(--bad); background: rgba(248,113,113,.12); }
        .badge.warn { color: var(--warn); background: rgba(251,191,36,.12); }
        .badge.partial { color: var(--accent); background: rgba(56,189,248,.12); }
        table { width: 100%; border-collapse: collapse; }
        .values-wrap { overflow-x: auto; }
        .values-table { table-layout: fixed; }
        .values-table th { padding: 7px 10px; font-size: 10px; }
        .values-table td { padding: 7px 10px; font-size: 12px; line-height: 1.25; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; vertical-align: middle; }
        .values-table code, .values-table .mono { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 12px; }
        .values-table .quality { display: inline-flex; align-items: center; gap: 6px; }
        .values-table .timestamp { color: var(--muted); font-size: 11px; }
        .field { display: flex; align-items: center; gap: 8px; margin-bottom: 10px; flex-wrap: wrap; }
        .field:last-child { margin-bottom: 0; }
        label.fl { color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: .05em; width: 86px; flex-shrink: 0; }
        select, input[type=text], input[type=password] { background: var(--bg); color: var(--text); border: 1px solid var(--border2); border-radius: 5px; padding: 6px 9px; font-size: 13px; }
        input[type=text], input[type=password], select { min-width: 140px; }
        input:disabled, select:disabled { opacity: .72; cursor: not-allowed; }
        .btn { display: inline-flex; align-items: center; gap: 6px; background: var(--accent); color: #07121a; border: none; border-radius: 5px; padding: 6px 13px; font-size: 12px; font-weight: 600; cursor: pointer; white-space: nowrap; }
        .btn.ghost { background: var(--panel2); color: var(--text); border: 1px solid var(--border2); }
        .hint, .msg { font-size: 12px; color: var(--muted); }
        .list { display: flex; flex-direction: column; gap: 6px; max-height: 320px; overflow-y: auto; }
        .li { display: flex; align-items: center; gap: 10px; padding: 8px 10px; border-radius: 5px; border: 1px solid var(--border); background: var(--panel2); }
        .li .n { font-size: 13px; font-weight: 600; }
        .li .p { font-size: 11px; color: var(--muted); font-family: 'Consolas', monospace; }
        .endpoint { background: var(--bg); border: 1px solid var(--border2); border-radius: 5px; padding: 7px 11px; font-family: 'Consolas', monospace; font-size: 12px; color: var(--accent); word-break: break-all; }
        .split { display: grid; grid-template-columns: 1.2fr 1fr; gap: 12px; }
        .toolbar { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 10px; }
        .warn { color: var(--warn); }
        .good { color: var(--good); }
        .bad { color: var(--bad); }
        .source-row { display: grid; grid-template-columns: 1fr auto; gap: 10px; align-items: center; }
        .log-panel { display: flex; flex-direction: column; gap: 10px; }
        .log-view { background: var(--bg); border: 1px solid var(--border2); border-radius: 6px; padding: 10px 12px; max-height: 520px; overflow: auto; font-family: 'Consolas', 'SF Mono', monospace; font-size: 12px; line-height: 1.45; }
        .log-entry { padding: 8px 0; border-bottom: 1px solid var(--border); }
        .log-entry:last-child { border-bottom: none; }
        .log-entry .meta { color: var(--muted); font-size: 11px; margin-bottom: 4px; }
        .log-entry .message { white-space: pre-wrap; word-break: break-word; }
        .log-entry .exception { white-space: pre-wrap; word-break: break-word; margin-top: 6px; color: var(--bad); }
        .doc-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 14px; }
        .doc-card { background: var(--panel2); border: 1px solid var(--border); border-radius: 6px; padding: 12px 14px; }
        .doc-card h3 { font-size: 13px; margin-bottom: 8px; }
        .doc-card ul { padding-left: 18px; color: var(--muted); }
        .doc-card li + li { margin-top: 6px; }
        .kv { display: grid; grid-template-columns: 140px 1fr; gap: 8px 12px; align-items: start; }
        .kv .k { color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: .05em; }
        .kv .v { word-break: break-word; }
        @media (max-width: 1100px) { .split { grid-template-columns: 1fr; } }
    </style>
</head>
<body>
<div class="topbar">
    <div class="brand"><span class="dot" id="dot"></span>OPC DA&#8594;UA Bridge</div>
    <div class="pills">
        <div class="pill"><span class="k">Bridge</span><span id="pBridge">&#8212;</span></div>
        <div class="pill"><span class="k">DA</span><span id="pDa">&#8212;</span></div>
        <div class="pill"><span class="k">UA</span><span id="pUa">&#8212;</span></div>
        <div class="pill"><span class="k">Tags</span><b id="pTags">0</b></div>
        <div class="pill"><span class="k">Sources</span><b id="pSources">0</b></div>
    </div>
    <div class="clock" id="clock">&#8212;</div>
</div>
<div class="tabbar">
    <button class="tabbtn active" data-tab="monitor" onclick="showTab('monitor')">Monitor</button>
    <button class="tabbtn" data-tab="connection" onclick="showTab('connection')">Connection</button>
    <button class="tabbtn" data-tab="tags" onclick="showTab('tags')">Tags</button>
    <button class="tabbtn" data-tab="logs" onclick="showTab('logs')">Logs</button>
    <button class="tabbtn" data-tab="help" onclick="showTab('help')">Help</button>
    <button class="tabbtn" data-tab="about" onclick="showTab('about')">About</button>
</div>
<div class="view active" id="view-monitor">
    <div class="stats">
        <div class="stat"><div class="k">Bridge runtime</div><div class="v" id="bridgeState">&#8212;</div><div class="s" id="lastError">No errors</div></div>
        <div class="stat"><div class="k">Aggregate DA</div><div class="v" id="daState">&#8212;</div></div>
        <div class="stat"><div class="k">Last DA read</div><div class="v" id="lastDaRead">&#8212;</div><div class="s" id="lastDaReadCount">0 values</div></div>
        <div class="stat"><div class="k">Last UA write</div><div class="v" id="lastUaWrite">&#8212;</div><div class="s" id="lastUaWriteCount">0 values</div></div>
        <div class="stat"><div class="k">OPC UA server</div><div class="v" id="uaState">&#8212;</div><div class="s" id="uaClients">0 clients</div></div>
        <div class="stat"><div class="k">Update rate</div><div class="v" id="updateRate">&#8212;</div><div class="s" id="mappingCount">0 tags</div><div class="mini-meter" aria-hidden="true"><div class="mini-meter-track"><div class="mini-meter-fill" id="pollUtilizationFill"></div></div></div><div class="s" id="pollUtilizationText">Cycle budget —</div><div class="s" id="pollSaturation">Cycle timing normal.</div></div>
    </div>
    <div class="grid2" style="margin-bottom:14px">
        <div class="box">
            <div class="box-h">Source Status</div>
            <div class="box-b"><div class="list" id="sourceStatusList"></div></div>
        </div>
        <div class="box">
            <div class="box-h">OPC UA Endpoint</div>
            <div class="box-b"><div class="endpoint" id="uaEndpoint">&#8212;</div><div class="msg" id="uaDiagnostics" style="margin-top:8px">0 nodes · no updates yet</div></div>
        </div>
    </div>
    <div class="box">
            <div class="box-h">DA Live Values <span class="msg" id="valCount" style="margin-left:auto"></span><button class="btn ghost" id="toggleLiveValues" type="button">Disable Live Data</button></div>
        <div class="box-b" style="padding:0">
            <div class="values-wrap">
                <table class="values-table">
                    <colgroup><col style="width:14%"><col style="width:29%"><col style="width:27%"><col style="width:14%"><col style="width:16%"></colgroup>
                    <thead><tr><th>Source</th><th>DA Item ID</th><th>Value</th><th>Quality</th><th>Timestamp</th></tr></thead>
                    <tbody id="values"><tr><td colspan="5" class="msg">Waiting for values&#8230;</td></tr></tbody>
                </table>
            </div>
        </div>
    </div>
</div>
<div class="view" id="view-connection">
    <div class="box">
        <div class="box-h">Selected Source</div>
        <div class="box-b">
            <div class="field"><label class="fl">Source</label><select id="selectedSource"></select></div>
            <div class="field"><label class="fl">Source ID</label><input id="cfgSourceId" type="text" placeholder="server-a" style="flex:1"></div>
            <div class="field"><label class="fl">Name</label><input id="cfgDisplayName" type="text" placeholder="Server A" style="flex:1"></div>
            <div class="field"><label class="fl">ProgID</label><input id="cfgProgId" type="text" placeholder="ProgID" style="flex:1"></div>
            <div class="field"><label class="fl">Host</label><input id="cfgHost" type="text" placeholder="localhost" style="flex:1"></div>
            <div class="field"><label class="fl">User</label><input id="cfgUser" type="text" placeholder="username" style="width:130px"><input id="cfgPass" type="password" placeholder="password" style="width:120px"><input id="cfgDomain" type="text" placeholder="domain" style="width:110px"></div>
            <div class="field"><label class="fl">Update Rate</label><input id="cfgUpdateRate" type="text" inputmode="numeric" placeholder="1000" style="width:130px"><button class="btn ghost" id="cfgApplyRate" type="button">Apply Rate</button><span class="msg">Minimum 100 ms</span></div>
            <div class="toolbar">
                <button class="btn" id="cfgApply" type="button">Save Source</button>
                <button class="btn ghost" id="cfgNew" type="button">New Source</button>
                <button class="btn ghost" id="cfgRemove" type="button">Remove Source</button>
            </div>
            <div class="msg" id="cfgMessage">Select a source.</div>
            <div class="msg" id="rateMessage">Bridge rate applies live to all DA sources.</div>
        </div>
    </div>
    <div class="grid2" style="margin-top:14px">
        <div class="box">
            <div class="box-h">OPC DA Browser</div>
            <div class="box-b">
                <div class="toolbar">
                    <button class="btn ghost" id="btnReloadServers" type="button">Browse Servers</button>
                    <span class="msg" id="msgServers">Scan local servers.</span>
                </div>
                <div class="list" id="listServers"></div>
            </div>
        </div>
        <div class="box">
            <div class="box-h">Configured Sources</div>
            <div class="box-b"><div class="list" id="sourcesList"></div></div>
        </div>
    </div>
</div>
<div class="view" id="view-tags">
    <div class="grid2">
        <div class="box">
            <div class="box-h">Tag Browser</div>
            <div class="box-b">
                <div class="toolbar">
                    <button class="btn" id="btnBrowseAllTags" type="button">Browse All Tags</button>
                    <button class="btn ghost" id="btnBrowseTags" type="button">Browse Folders</button>
                    <span class="msg" id="tagBreadcrumb">Browse all tags, or open folders one level at a time.</span>
                </div>
                <div class="list" id="tagTree"></div>
            </div>
        </div>
        <div class="box">
            <div class="box-h">DA → OPC UA Mappings <span class="msg" id="mapCount" style="margin-left:auto"></span></div>
            <div class="box-b">
                <div class="field">
                    <label class="fl">DA Source</label>
                    <select id="mapSourceSelect"></select>
                </div>
                <div class="field" style="margin-bottom:8px">
                    <input id="manualItem" type="text" placeholder="DA Item ID" style="flex:1">
                    <input id="manualUaNodeId" type="text" placeholder="OPC UA NodeId (optional)" style="flex:1">
                    <button class="btn ghost" type="button" id="manualAdd">Add Mapping</button>
                </div>
                <div class="list" id="mappedList"></div>
            </div>
        </div>
    </div>
</div>
<div class="view" id="view-logs">
    <div class="box">
        <div class="box-h">Recent Logs</div>
        <div class="box-b log-panel">
            <div class="toolbar">
                <button class="btn ghost" id="btnRefreshLogs" type="button">Refresh Logs</button>
                <label class="fl" for="logLevel" style="width:auto">Minimum Level</label>
                <select id="logLevel">
                    <option value="Information">Information</option>
                    <option value="Warning">Warning</option>
                    <option value="Error">Error</option>
                </select>
                <span class="msg" id="logMessage">Showing recent in-app logs.</span>
            </div>
            <div class="log-view" id="logEntries"><span class="msg">Loading logs…</span></div>
        </div>
    </div>
</div>
<div class="view" id="view-help">
    <div class="box">
        <div class="box-h">Operator Help</div>
        <div class="box-b">
            <div class="doc-grid">
                <div class="doc-card">
                    <h3>Basic Workflow</h3>
                    <ul>
                        <li>Use <b>Connection</b> to configure the OPC DA source, host, and update rate.</li>
                        <li>Use <b>Tags</b> to browse DA items and create DA → OPC UA mappings.</li>
                        <li>Use <b>Monitor</b> to confirm source reads, live values, and OPC UA writes.</li>
                    </ul>
                </div>
                <div class="doc-card">
                    <h3>Update Rate Tuning</h3>
                    <ul>
                        <li>Lower update rate means faster polling; the practical minimum is 100 ms.</li>
                        <li>Watch the cycle-budget bar: green is healthy, yellow is tight, red is saturated.</li>
                        <li>If saturation appears, increase the rate or reduce mapped load per source.</li>
                    </ul>
                </div>
                <div class="doc-card">
                    <h3>Troubleshooting</h3>
                    <ul>
                        <li>Use <b>Logs</b> to review recent warnings and errors from the bridge and UA server.</li>
                        <li>If DA browse fails, check ProgID, host reachability, DCOM permissions, and credentials.</li>
                        <li>If values stop moving, verify source status, last read timing, and the last error field.</li>
                    </ul>
                </div>
            </div>
        </div>
    </div>
</div>
<div class="view" id="view-about">
    <div class="box">
        <div class="box-h">About This App</div>
        <div class="box-b">
            <div class="kv">
                <div class="k">Application</div><div class="v" id="aboutName">—</div>
                <div class="k">Version</div><div class="v" id="aboutVersion">—</div>
                <div class="k">Informational Build</div><div class="v" id="aboutInfoVersion">—</div>
                <div class="k">Framework</div><div class="v" id="aboutFramework">—</div>
                <div class="k">Architecture</div><div class="v" id="aboutArchitecture">—</div>
                <div class="k">Operating System</div><div class="v" id="aboutOs">—</div>
                <div class="k">Machine</div><div class="v" id="aboutMachine">—</div>
                <div class="k">Creator</div><div class="v" id="aboutCreator">—</div>
            </div>
        </div>
    </div>
</div>
""";

    public const string Script = """
<script>
const ESC = {'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'};
const esc = s => String(s ?? '').replace(/[&<>'"]/g, c => ESC[c]);
const attr = esc;
const el = id => document.getElementById(id);
const state = {
    tagPath: '',
    sources: [],
    selectedSourceId: 'default',
    editingNewSource: false,
    liveValuesEnabled: true,
    lastValueCount: 0,
    updateRateMs: 1000,
    logsLoaded: false,
    appInfoLoaded: false
};

function showTab(name) {
    document.querySelectorAll('.tabbtn').forEach(b => b.classList.toggle('active', b.dataset.tab === name));
    document.querySelectorAll('.view').forEach(v => v.classList.toggle('active', v.id === 'view-' + name));
    if (location.hash !== '#' + name) history.replaceState(null, '', '#' + name);
    if (name === 'logs') loadLogs().catch(e => el('logMessage').textContent = '✗ ' + e.message);
    if (name === 'about') loadAppInfo().catch(e => el('aboutName').textContent = '✗ ' + e.message);
}
function badge(t, c) { return `<span class="badge ${c}">${esc(t)}</span>`; }
function stateClass(v) {
    if (!v) return 'warn';
    const s = String(v).toLowerCase();
    if (s === 'running' || s === 'connected') return 'good';
    if (s === 'partial') return 'partial';
    if (s === 'faulted' || s === 'stopped' || s === 'disconnected') return 'bad';
    return 'warn';
}
function relTime(u) {
    if (!u) return '—';
    const d = Math.floor((Date.now() - new Date(u)) / 1000);
    if (d < 5) return 'just now';
    if (d < 60) return d + 's ago';
    if (d < 3600) return Math.floor(d / 60) + 'm ago';
    return new Date(u).toLocaleTimeString();
}
function shortTime(u) {
    if (!u) return '—';
    return new Date(u).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}
function locTime(u) { return u ? new Date(u).toLocaleString() : '—'; }
function get(o, k) { return o?.[k] ?? o?.[k[0].toUpperCase() + k.slice(1)]; }
function currentSource() { return state.editingNewSource ? null : state.sources.find(s => s.sourceId === state.selectedSourceId) || null; }
function defaultUaNodeId(sourceId, itemId) { return `ns=2;s=${sourceId}/${itemId}`; }
function renderSources() {
    const select = el('selectedSource');
    const mapSelect = el('mapSourceSelect');
    const options = state.sources.map(source => `<option value="${esc(source.sourceId)}">${esc(source.displayName || source.sourceId)}</option>`).join('');
    select.innerHTML = options;
    mapSelect.innerHTML = options;
    if (!state.editingNewSource && !state.sources.some(source => source.sourceId === state.selectedSourceId) && state.sources.length) {
        state.selectedSourceId = state.sources[0].sourceId;
    }
    select.value = state.selectedSourceId;
    mapSelect.value = state.selectedSourceId;
    el('pSources').textContent = state.sources.length;
    el('sourcesList').innerHTML = state.sources.length ? state.sources.map(source =>
        `<div class="li source-row"><div><div class="n">${esc(source.displayName || source.sourceId)}</div><div class="p">${esc(source.sourceId)} · ${esc(source.host || 'localhost')} · ${esc(source.progId || '')}</div></div><button class="btn ghost" data-action="select-source" data-source-id="${attr(source.sourceId)}">Select</button></div>`
    ).join('') : '<span class="msg">No sources configured.</span>';
    loadSelectedSourceForm();
}
function loadSelectedSourceForm() {
    const source = currentSource();
    if (!source) return;
    state.editingNewSource = false;
    el('cfgSourceId').value = source.sourceId || '';
    el('cfgSourceId').disabled = true;
    el('cfgDisplayName').value = source.displayName || '';
    el('cfgProgId').value = source.progId || '';
    el('cfgHost').value = source.host || 'localhost';
    el('cfgUser').value = source.remoteUsername || '';
    el('cfgPass').value = '';
    el('cfgDomain').value = source.remoteDomain || '';
    el('cfgMessage').textContent = 'Editing source ' + (source.displayName || source.sourceId) + '. Source ID is fixed; create a new source for another ID.';
}
async function loadSources() {
    const payload = await (await fetch('/api/da/sources', { cache: 'no-store' })).json();
    state.sources = payload.sources || [];
    state.updateRateMs = Number(payload.updateRateMs || state.updateRateMs || 1000);
    if (document.activeElement !== el('cfgUpdateRate')) el('cfgUpdateRate').value = state.updateRateMs;
    renderSources();
}
function updateLiveValuesUi() {
    el('toggleLiveValues').textContent = state.liveValuesEnabled ? 'Disable Live Data' : 'Enable Live Data';
    el('valCount').textContent = state.lastValueCount + ' values' + (state.liveValuesEnabled ? '' : ' · paused');
}

function formatMs(value) {
    const n = Number(value ?? 0);
    return n > 0 ? n.toLocaleString(undefined, { maximumFractionDigits: 1 }) + ' ms' : '—';
}

function formatRate(value) {
    const n = Number(value ?? 0);
    return n > 0 ? n.toLocaleString(undefined, { maximumFractionDigits: 1 }) + ' values/s' : '0 values/s';
}

function formatUaDiagnostics(ua) {
    const nodeCount = get(ua, 'mappedNodeCount') ?? 0;
    const lastUpdateUtc = get(ua, 'lastValueUpdateUtc');
    return nodeCount + ' nodes · last node update ' + (lastUpdateUtc ? relTime(lastUpdateUtc) : 'never');
}
function formatPollSaturation(lastPollDurationMs, updateRateMs) {
    const duration = Number(lastPollDurationMs ?? 0);
    const rate = Number(updateRateMs ?? 0);
    if (duration <= 0 || rate <= 0) return { text: 'Waiting for cycle timing…', className: 's' };
    if (duration >= rate) return { text: 'Poll saturated · cycle time is at or above the configured rate.', className: 's bad' };
    if (duration >= rate * 0.8) return { text: 'Cycle budget is getting tight.', className: 's warn' };
    return { text: 'Cycle timing normal.', className: 's' };
}

function formatPollUtilization(lastPollDurationMs, updateRateMs) {
    const duration = Number(lastPollDurationMs ?? 0);
    const rate = Number(updateRateMs ?? 0);
    if (duration <= 0 || rate <= 0) {
        return { width: '0%', className: 'mini-meter-fill', text: 'Cycle budget —' };
    }

    const percent = Math.max(0, Math.round((duration / rate) * 100));
    const clampedPercent = Math.max(0, Math.min(percent, 100));
    const className = percent >= 100
        ? 'mini-meter-fill bad'
        : percent >= 80
            ? 'mini-meter-fill warn'
            : 'mini-meter-fill';

    return {
        width: clampedPercent + '%',
        className,
        text: 'Cycle budget ' + percent + '%'
    };
}
async function loadLogs(force = false) {
    if (state.logsLoaded && !force) return;
    const level = el('logLevel')?.value || 'Information';
    el('logMessage').textContent = 'Loading logs…';
    const payload = await (await fetch('/api/logs?limit=200&level=' + encodeURIComponent(level), { cache: 'no-store' })).json();
    const entries = payload.entries || [];
    el('logEntries').innerHTML = entries.length ? entries.map(entry => {
        const timestamp = locTime(entry.timestampUtc || entry.TimestampUtc);
        const levelText = entry.level || entry.Level || 'Information';
        const category = entry.category || entry.Category || 'App';
        const message = entry.message || entry.Message || '';
        const exceptionText = entry.exceptionText || entry.ExceptionText || '';
        return `<div class="log-entry"><div class="meta">${esc(timestamp)} · ${esc(levelText)} · ${esc(category)}</div><div class="message">${esc(message)}</div>${exceptionText ? `<div class="exception">${esc(exceptionText)}</div>` : ''}</div>`;
    }).join('') : '<span class="msg">No log entries for this filter yet.</span>';
    el('logMessage').textContent = entries.length + ' recent entries';
    state.logsLoaded = true;
}

async function loadAppInfo(force = false) {
    if (state.appInfoLoaded && !force) return;
    const payload = await (await fetch('/api/app-info', { cache: 'no-store' })).json();
    el('aboutName').textContent = payload.name || 'OpcBridge.App';
    el('aboutVersion').textContent = payload.version || '—';
    el('aboutInfoVersion').textContent = payload.informationalVersion || '—';
    el('aboutFramework').textContent = payload.framework || '—';
    el('aboutArchitecture').textContent = payload.processArchitecture || '—';
    el('aboutOs').textContent = payload.osDescription || '—';
    el('aboutMachine').textContent = payload.machineName || '—';
    el('aboutCreator').textContent = payload.creator || '—';
    state.appInfoLoaded = true;
}



async function refresh() {
    try {
        const p = await (await fetch('/api/dashboard', { cache: 'no-store' })).json();
        const b = p.bridge || p.Bridge || {};
        const ua = p.ua || p.Ua || {};
        const vs = p.values || p.Values || [];
        const sources = get(b, 'sources') || [];
        el('dot').className = 'dot';
        el('clock').textContent = new Date().toLocaleTimeString();
        el('pBridge').innerHTML = badge(get(b, 'bridgeState') || '—', stateClass(get(b, 'bridgeState')));
        el('pDa').innerHTML = badge(get(b, 'daConnectionState') || '—', stateClass(get(b, 'daConnectionState')));
        el('pUa').innerHTML = badge(get(ua, 'state') || '—', stateClass(get(ua, 'state')));
        el('pTags').textContent = get(b, 'mappingCount') ?? 0;
        el('bridgeState').innerHTML = badge(get(b, 'bridgeState') || '—', stateClass(get(b, 'bridgeState')));
        const err = get(b, 'lastError');
        el('lastError').textContent = err || 'No errors';
        el('lastError').className = 's' + (err ? ' bad' : '');
        el('daState').innerHTML = badge(get(b, 'daConnectionState') || '—', stateClass(get(b, 'daConnectionState')));
        el('lastDaRead').textContent = relTime(get(b, 'lastDaReadUtc'));
        el('lastDaReadCount').textContent = (get(b, 'lastDaReadCount') ?? 0) + ' values';
        el('lastUaWrite').textContent = relTime(get(b, 'lastUaWriteUtc'));
        el('lastUaWriteCount').textContent = (get(b, 'lastUaWriteCount') ?? 0) + ' values · ' + formatMs(get(b, 'lastPollDurationMs')) + ' · ' + formatRate(get(b, 'lastPollValueRate'));
        el('uaState').innerHTML = badge(get(ua, 'state') || '—', stateClass(get(ua, 'state')));
        el('uaClients').textContent = (get(ua, 'connectedClientCount') ?? 0) + ' clients';
        const updateRateMs = Number(get(b, 'updateRateMs') || state.updateRateMs || 1000);
        const pollSaturation = formatPollSaturation(get(b, 'lastPollDurationMs'), updateRateMs);
        const pollUtilization = formatPollUtilization(get(b, 'lastPollDurationMs'), updateRateMs);
        state.updateRateMs = updateRateMs;
        el('updateRate').textContent = updateRateMs + ' ms';
        el('pollUtilizationFill').style.width = pollUtilization.width;
        el('pollUtilizationFill').className = pollUtilization.className;
        el('pollUtilizationText').textContent = pollUtilization.text;
        el('pollSaturation').textContent = pollSaturation.text;
        el('pollSaturation').className = pollSaturation.className;
        if (document.activeElement !== el('cfgUpdateRate')) el('cfgUpdateRate').value = updateRateMs;
        el('mappingCount').textContent = (get(b, 'mappingCount') ?? 0) + ' tags';
        el('uaEndpoint').textContent = get(ua, 'endpointUrl') || '—';
        el('uaDiagnostics').textContent = formatUaDiagnostics(ua);
        el('sourceStatusList').innerHTML = sources.length ? sources.map(source =>
            `<div class="li"><div style="flex:1"><div class="n">${esc(get(source,'displayName') || get(source,'sourceId'))}</div><div class="p">${esc(get(source,'sourceId'))} · ${esc(get(source,'host') || '')} · ${esc(get(source,'progId') || '')} · ${(get(source,'lastDaReadCount') ?? 0)} values in ${formatMs(get(source,'lastDaReadDurationMs'))}</div></div><div>${badge(get(source,'connectionState') || '—', stateClass(get(source,'connectionState')))}</div></div>`
        ).join('') : '<span class="msg">No source status yet.</span>';
        state.lastValueCount = vs.length;
        updateLiveValuesUi();
        if (state.liveValuesEnabled) {
            el('values').innerHTML = vs.length ? vs.map(it => {
                const g = get(it, 'isGood');
                const q = get(it, 'daQuality');
                const sourceId = get(it, 'sourceId');
                const itemId = get(it, 'daItemId');
                const value = String(get(it, 'value') ?? '');
                const timestamp = locTime(get(it, 'timestampUtc'));
                const timestampShort = shortTime(get(it, 'timestampUtc'));
                return `<tr><td><code title="${attr(sourceId)}">${esc(sourceId)}</code></td><td><code title="${attr(itemId)}">${esc(itemId)}</code></td><td class="mono" title="${attr(value)}">${esc(value)}</td><td title="${attr(String(q ?? ''))}"><span class="quality">${badge(g ? 'Good' : 'Bad', g ? 'good' : 'bad')} <span class="${g ? 'good' : 'bad'}">(${q})</span></span></td><td class="msg timestamp" title="${attr(timestamp)}">${esc(timestampShort)}</td></tr>`;
            }).join('') : '<tr><td colspan="5" class="msg">No values yet.</td></tr>';
        }
    } catch (e) {
        el('dot').className = 'dot off';
        el('clock').textContent = 'offline';
        if (state.liveValuesEnabled) {
            el('values').innerHTML = `<tr><td colspan="5" class="bad">${esc(e.message)}</td></tr>`;
        }
    }
}
async function loadMappings() {
    const p = await (await fetch('/api/mappings', { cache: 'no-store' })).json();
    const mappings = p.mappings || [];
    el('mapCount').textContent = mappings.length + ' mappings';
    el('mappedList').innerHTML = mappings.length ? mappings.map(t => {
        const sourceId = t.sourceId || t.SourceId || 'default';
        const item = t.daItemId || t.DaItemId;
        const name = t.displayName || t.DisplayName || item;
        const node = t.uaNodeId || t.UaNodeId || defaultUaNodeId(sourceId, item);
        return `<div class="li"><div style="flex:1"><div class="n">${esc(name)}</div><div class="p">${esc(sourceId)} · ${esc(item)} · UA: ${esc(node)}</div></div><button class="btn ghost" data-action="remove-mapping" data-source-id="${attr(sourceId)}" data-item-id="${attr(item)}">Remove</button></div>`;
    }).join('') : '<span class="msg">No DA → OPC UA mappings.</span>';
}

function pickSource(sourceId) {
    state.selectedSourceId = sourceId;
    state.editingNewSource = false;
    state.tagPath = '';
    el('tagTree').innerHTML = '<span class="msg">Browse the active source to load tags.</span>';
    renderCrumb();
    renderSources();
}
async function saveSource() {
    const sourceId = el('cfgSourceId').value.trim();
    if (!sourceId) {
        el('cfgMessage').textContent = '✗ Source ID is required.';
        return;
    }
    const body = {
        sourceId,
        displayName: el('cfgDisplayName').value.trim() || null,
        progId: el('cfgProgId').value.trim(),
        host: el('cfgHost').value.trim() || 'localhost',
        remoteUsername: el('cfgUser').value.trim() || null,
        remotePassword: el('cfgPass').value || null,
        remoteDomain: el('cfgDomain').value.trim() || null
    };
    const r = await fetch('/api/da/sources', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const p = await r.json();
    if (!r.ok) throw new Error(p.error || ('HTTP ' + r.status));
    state.selectedSourceId = p.source?.sourceId || body.sourceId;
    state.editingNewSource = false;
    await loadSources();
    await refresh();
    el('cfgMessage').textContent = 'Source saved.';
}
async function saveUpdateRate() {
    const updateRateMs = Number.parseInt(el('cfgUpdateRate').value.trim(), 10);
    if (!Number.isFinite(updateRateMs) || updateRateMs <= 0) {
        el('rateMessage').textContent = '✗ Enter an update rate in milliseconds.';
        return;
    }

    const r = await fetch('/api/da/update-rate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ updateRateMs })
    });
    const p = await r.json();
    if (!r.ok) throw new Error(p.error || ('HTTP ' + r.status));
    state.updateRateMs = Number(p.updateRateMs || updateRateMs);
    el('cfgUpdateRate').value = state.updateRateMs;
    await refresh();
    el('rateMessage').textContent = 'Update rate applied: ' + state.updateRateMs + ' ms.';
}
async function removeSelectedSource() {
    const source = currentSource();
    if (!source || state.editingNewSource) return;
    if (!confirm('Remove source "' + source.sourceId + '" and its DA → OPC UA mappings?')) return;
    const r = await fetch('/api/da/sources/remove', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ sourceId: source.sourceId }) });
    const p = await r.json();
    if (!r.ok) throw new Error(p.error || ('HTTP ' + r.status));
    state.selectedSourceId = 'default';
    await loadSources();
    await loadMappings();
    await refresh();
    el('cfgMessage').textContent = 'Source removed.';
}
function newSource() {
    state.selectedSourceId = '';
    state.editingNewSource = true;
    el('selectedSource').value = '';
    el('mapSourceSelect').value = '';
    el('cfgSourceId').disabled = false;
    el('cfgSourceId').value = '';
    el('cfgDisplayName').value = '';
    el('cfgProgId').value = '';
    el('cfgHost').value = 'localhost';
    el('cfgUser').value = '';
    el('cfgPass').value = '';
    el('cfgDomain').value = '';
    el('tagTree').innerHTML = '<span class="msg">Save the new source before browsing tags.</span>';
    el('cfgMessage').textContent = 'Enter a unique Source ID, then save.';
}
async function browseServers() {
    const host = (el('cfgHost').value.trim() || 'localhost');
    el('msgServers').textContent = 'Scanning…';
    const url = '/api/da/servers' + (host && host !== 'localhost' ? ('?host=' + encodeURIComponent(host)) : '');
    const p = await (await fetch(url, { cache: 'no-store' })).json();
    if (p.error) throw new Error(p.error);
    const servers = p.servers || [];
    el('listServers').innerHTML = servers.length ? servers.map((s, i) => {
        const prog = s.progId || s.ProgId;
        const desc = s.description || s.Description || prog;
        return `<div class="li"><div style="flex:1"><div class="n">${esc(desc)}</div><div class="p">${esc(prog)}</div></div><button class="btn ghost" data-action="pick-server" data-prog-id="${attr(prog)}" data-host="${attr(host)}">Use</button></div>`;
    }).join('') : '<span class="msg">No servers found.</span>';
    el('msgServers').textContent = servers.length + ' servers';
}
function pickServer(progId, host) {
    el('cfgProgId').value = progId;
    el('cfgHost').value = host;
    el('cfgMessage').textContent = 'Selected server; save source to apply.';
}
function renderCrumb() {
    el('tagBreadcrumb').textContent = state.tagPath || 'root';
}
async function browseTags(path, recursive = false) {
    const source = currentSource();
    if (!source || state.editingNewSource) {
        el('tagTree').innerHTML = '<span class="bad">Save or select a source before browsing tags.</span>';
        return;
    }
    state.tagPath = path || '';
    renderCrumb();
    el('tagTree').innerHTML = '<span class="msg">Browsing…</span>';
    const body = {
        sourceId: source.sourceId,
        progId: source.progId,
        host: source.host || 'localhost',
        path: state.tagPath,
        recursive,
        remoteUsername: source.remoteUsername || null,
        remotePassword: null,
        remoteDomain: source.remoteDomain || null
    };
    const p = await (await fetch('/api/da/tags', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })).json();
    if (p.error) throw new Error(p.error);
    const branches = p.branches || [];
    const tags = p.tags || [];
    const rows = [];
    for (const branch of branches) {
        const child = state.tagPath ? state.tagPath + '.' + branch : branch;
        rows.push(`<div class="li"><div style="flex:1"><div class="n">${esc(branch)}</div><div class="p">branch</div></div><button class="btn ghost" data-action="open-branch" data-path="${attr(child)}">Open</button></div>`);
    }
    for (const tag of tags) {
        const itemId = tag.itemId || tag.ItemId;
        const name = tag.name || tag.Name || itemId;
        rows.push(`<div class="li"><div style="flex:1"><div class="n">${esc(name)}</div><div class="p">${esc(itemId)}</div></div><button class="btn ghost" data-action="add-tag" data-source-id="${attr(source.sourceId)}" data-item-id="${attr(itemId)}" data-name="${attr(name)}">Add</button></div>`);
    }
    el('tagTree').innerHTML = rows.length ? rows.join('') : '<span class="msg">Empty.</span>';
}
async function addTag(sourceId, itemId, name) {
    await fetch('/api/mappings/add', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tags: [{ sourceId, daItemId: itemId, displayName: name || itemId, dataType: 'Auto', uaNodeId: defaultUaNodeId(sourceId, itemId) }] })
    });
    await loadMappings();
    await refresh();
}
async function addManual() {
    const itemId = el('manualItem').value.trim();
    const source = currentSource();
    if (!itemId || !source || state.editingNewSource) return;
    const sourceId = source.sourceId;
    const uaNodeId = el('manualUaNodeId').value.trim() || defaultUaNodeId(sourceId, itemId);
    await fetch('/api/mappings/add', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tags: [{ sourceId, daItemId: itemId, displayName: itemId, dataType: 'Auto', uaNodeId }] })
    });
    el('manualItem').value = '';
    el('manualUaNodeId').value = '';
    await loadMappings();
    await refresh();
}
async function removeMapping(sourceId, itemId) {
    await fetch('/api/mappings/remove', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sourceId, daItemId: itemId })
    });
    await loadMappings();
    await refresh();
}
function toggleLiveValues() {
    state.liveValuesEnabled = !state.liveValuesEnabled;
    updateLiveValuesUi();
    if (state.liveValuesEnabled) {
        refresh().catch(e => {
            el('dot').className = 'dot off';
            el('clock').textContent = 'offline';
            el('values').innerHTML = `<tr><td colspan="5" class="bad">${esc(e.message)}</td></tr>`;
        });
    }
}

function bindDynamicButtons() {
    el('sourcesList').addEventListener('click', event => {
        const button = event.target.closest('button[data-action="select-source"]');
        if (!button) return;
        pickSource(button.dataset.sourceId || '');
    });
    el('listServers').addEventListener('click', event => {
        const button = event.target.closest('button[data-action="pick-server"]');
        if (!button) return;
        pickServer(button.dataset.progId || '', button.dataset.host || 'localhost');
    });
    el('tagTree').addEventListener('click', event => {
        const button = event.target.closest('button[data-action]');
        if (!button) return;
        if (button.dataset.action === 'open-branch') {
            browseTags(button.dataset.path || '').catch(e => el('tagTree').innerHTML = `<span class="bad">${esc(e.message)}</span>`);
            return;
        }
        if (button.dataset.action === 'add-tag') {
            addTag(button.dataset.sourceId || '', button.dataset.itemId || '', button.dataset.name || '').catch(e => alert('Add failed: ' + e.message));
        }
    });
    el('mappedList').addEventListener('click', event => {
        const button = event.target.closest('button[data-action="remove-mapping"]');
        if (!button) return;
        removeMapping(button.dataset.sourceId || '', button.dataset.itemId || '').catch(e => alert('Remove failed: ' + e.message));
    });
}

document.addEventListener('DOMContentLoaded', async () => {
    el('selectedSource').addEventListener('change', e => pickSource(e.target.value));
    el('mapSourceSelect').addEventListener('change', e => pickSource(e.target.value));
    el('cfgApply').addEventListener('click', () => saveSource().catch(e => el('cfgMessage').textContent = '✗ ' + e.message));
    el('cfgNew').addEventListener('click', newSource);
    el('cfgRemove').addEventListener('click', () => removeSelectedSource().catch(e => el('cfgMessage').textContent = '✗ ' + e.message));
    el('cfgApplyRate').addEventListener('click', () => saveUpdateRate().catch(e => el('rateMessage').textContent = '✗ ' + e.message));
    el('btnReloadServers').addEventListener('click', () => browseServers().catch(e => el('msgServers').textContent = e.message));
    el('btnBrowseTags').addEventListener('click', () => browseTags('').catch(e => el('tagTree').innerHTML = `<span class="bad">${esc(e.message)}</span>`));
    el('btnBrowseAllTags').addEventListener('click', () => browseTags('', true).catch(e => el('tagTree').innerHTML = `<span class="bad">${esc(e.message)}</span>`));
    el('manualAdd').addEventListener('click', () => addManual().catch(e => alert('Add failed: ' + e.message)));
    el('toggleLiveValues').addEventListener('click', toggleLiveValues);
    el('btnRefreshLogs').addEventListener('click', () => loadLogs(true).catch(e => el('logMessage').textContent = '✗ ' + e.message));
    el('logLevel').addEventListener('change', () => {
        state.logsLoaded = false;
        loadLogs(true).catch(e => el('logMessage').textContent = '✗ ' + e.message);
    });
    bindDynamicButtons();
    const initTab = location.hash.slice(1);
    if (['monitor','connection','tags','logs','help','about'].includes(initTab)) showTab(initTab);
    await loadSources();
    await loadMappings();
    updateLiveValuesUi();
    await refresh();
    if (initTab === 'logs') await loadLogs();
    if (initTab === 'about') await loadAppInfo();
    setInterval(refresh, 1000);
});
</script>
</body>
</html>
""";

    public static string FullHtml => Html + Script;
}
