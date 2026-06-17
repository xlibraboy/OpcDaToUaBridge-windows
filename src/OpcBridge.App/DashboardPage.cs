namespace OpcBridge.App;

internal static class DashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>OPC Bridge Dashboard</title>
    <style>
        :root {
            color-scheme: dark;
            --bg: #0d1117;
            --surface: #161b22;
            --surface2: #1c2330;
            --border: #30363d;
            --text: #e6edf3;
            --muted: #8b949e;
            --good: #3fb950;
            --bad: #f85149;
            --warn: #d29922;
            --accent: #58a6ff;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
        }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--bg); color: var(--text); font-size: 14px; }

        /* Header */
        header {
            padding: 16px 28px;
            background: var(--surface);
            border-bottom: 1px solid var(--border);
            display: flex;
            align-items: center;
            gap: 16px;
        }
        header h1 { margin: 0; font-size: 18px; font-weight: 600; }
        .header-meta { color: var(--muted); font-size: 13px; }
        .header-meta code { color: var(--accent); background: rgba(88,166,255,.1); padding: 1px 5px; border-radius: 4px; font-size: 12px; }
        .pulse-dot {
            width: 9px; height: 9px; border-radius: 50%;
            background: var(--good); flex-shrink: 0;
            box-shadow: 0 0 0 0 rgba(63,185,80,.6);
            animation: pulse 2s infinite;
        }
        .pulse-dot.offline { background: var(--bad); animation: none; box-shadow: none; }
        @keyframes pulse {
            0%   { box-shadow: 0 0 0 0 rgba(63,185,80,.6); }
            70%  { box-shadow: 0 0 0 7px rgba(63,185,80,0); }
            100% { box-shadow: 0 0 0 0 rgba(63,185,80,0); }
        }
        .last-refresh { margin-left: auto; color: var(--muted); font-size: 12px; }

        main { padding: 24px 28px; }

        /* Cards */
        .cards {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(190px, 1fr));
            gap: 10px;
            margin-bottom: 28px;
        }
        .card {
            background: var(--surface);
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 14px 16px;
        }
        .card-label {
            color: var(--muted);
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: .06em;
            font-weight: 500;
        }
        .card-value {
            margin-top: 8px;
            font-size: 17px;
            font-weight: 700;
            overflow-wrap: anywhere;
            line-height: 1.2;
        }
        .card-sub {
            margin-top: 5px;
            color: var(--muted);
            font-size: 12px;
            overflow-wrap: anywhere;
        }

        /* Badge */
        .badge {
            display: inline-flex;
            align-items: center;
            gap: 5px;
            padding: 2px 8px;
            border-radius: 12px;
            font-size: 13px;
            font-weight: 600;
        }
        .badge::before { content: ''; width: 6px; height: 6px; border-radius: 50%; background: currentColor; opacity: .7; }
        .badge.good  { color: var(--good);  background: rgba(63,185,80,.12); }
        .badge.bad   { color: var(--bad);   background: rgba(248,81,73,.12); }
        .badge.warn  { color: var(--warn);  background: rgba(210,153,34,.12); }

        /* Section */
        .section-header { font-size: 14px; font-weight: 600; color: var(--muted); text-transform: uppercase; letter-spacing: .06em; margin: 0 0 10px; }

        /* Tables */
        .table-wrap { border: 1px solid var(--border); border-radius: 8px; overflow: hidden; margin-bottom: 28px; }
        table { width: 100%; border-collapse: collapse; background: var(--surface); }
        thead { position: sticky; top: 0; z-index: 1; }
        th {
            background: var(--surface2);
            color: var(--muted);
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: .06em;
            font-weight: 500;
            padding: 9px 14px;
            text-align: left;
            border-bottom: 1px solid var(--border);
        }
        td { padding: 10px 14px; border-bottom: 1px solid var(--border); vertical-align: middle; }
        tr:last-child td { border-bottom: none; }
        tbody tr:hover td { background: var(--surface2); }
        td.label-cell { color: var(--muted); width: 180px; font-size: 13px; }

        .good  { color: var(--good); }
        .bad   { color: var(--bad); }
        .warn  { color: var(--warn); }
        code   { color: var(--accent); font-size: 12px; }

        /* Mode control */
        .mode-row { display: flex; align-items: center; gap: 10px; }
        select {
            background: var(--surface2);
            color: var(--text);
            border: 1px solid var(--border);
            border-radius: 6px;
            padding: 6px 10px;
            font-size: 13px;
            cursor: pointer;
        }
        select:focus { outline: 2px solid var(--accent); outline-offset: 1px; border-color: transparent; }
        input[type="text"] {
            background: var(--surface2);
            color: var(--text);
            border: 1px solid var(--border);
            border-radius: 6px;
            padding: 6px 10px;
            font-size: 13px;
            width: 280px;
        }
        input[type="text"]:focus { outline: 2px solid var(--accent); outline-offset: 1px; border-color: transparent; }
        button {
            background: var(--accent);
            color: #0d1117;
            border: none;
            border-radius: 6px;
            padding: 6px 14px;
            font-size: 13px;
            font-weight: 600;
            cursor: pointer;
            transition: opacity .15s;
        }
        button:hover:not(:disabled) { opacity: .85; }
        button:disabled { opacity: .4; cursor: not-allowed; }
        .mode-msg { font-size: 13px; color: var(--muted); }

        /* UA endpoint display */
        .endpoint-box {
            background: var(--surface2);
            border: 1px solid var(--border);
            border-radius: 6px;
            padding: 8px 12px;
            font-family: monospace;
            font-size: 13px;
            color: var(--accent);
            word-break: break-all;
        }
        /* Tab buttons */
        .tab-btn {
            background: none;
            color: var(--muted);
            border: none;
            border-bottom: 2px solid transparent;
            border-radius: 0;
            padding: 10px 20px;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
        }
        .tab-btn.active { color: var(--text); border-bottom-color: var(--accent); }
        .tab-btn:hover:not(.active) { color: var(--text); }
        /* Server list items */
        .server-item {
            display: flex;
            flex-direction: column;
            padding: 10px 12px;
            border-radius: 6px;
            cursor: pointer;
            margin-bottom: 4px;
            border: 1px solid transparent;
        }
        .server-item:hover { background: var(--surface2); border-color: var(--border); }
        .server-item.selected { background: rgba(88,166,255,.08); border-color: var(--accent); }
        .server-item .s-name { font-size: 13px; font-weight: 600; color: var(--text); }
        .server-item .s-prog { font-size: 12px; color: var(--muted); font-family: monospace; margin-top:2px; }
    </style>
</head>
<body>
<header>
    <div class="pulse-dot" id="pulseDot"></div>
    <div>
        <h1>OPC DA → UA Bridge</h1>
        <div class="header-meta">Runtime dashboard &nbsp;·&nbsp; UA endpoint: <code id="headerEndpoint">opc.tcp://…</code></div>
    </div>
    <div class="last-refresh" id="refresh">—</div>
</header>

<main>

    <div class="cards">
        <div class="card">
            <div class="card-label">Bridge runtime</div>
            <div class="card-value" id="bridgeState">—</div>
            <div class="card-sub" id="lastError">No errors</div>
        </div>
        <div class="card">
            <div class="card-label">OPC DA client</div>
            <div class="card-value" id="daState">—</div>
            <div class="card-sub" id="daMode">Mode: —</div>
        </div>
        <div class="card">
            <div class="card-label">Last DA read</div>
            <div class="card-value" id="lastDaRead">—</div>
            <div class="card-sub" id="lastDaReadCount">0 values</div>
        </div>
        <div class="card">
            <div class="card-label">Last UA write</div>
            <div class="card-value" id="lastUaWrite">—</div>
            <div class="card-sub" id="lastUaWriteCount">0 values</div>
        </div>
        <div class="card">
            <div class="card-label">OPC UA server</div>
            <div class="card-value" id="uaState">—</div>
            <div class="card-sub" id="uaClients">0 clients</div>
        </div>
        <div class="card">
            <div class="card-label">Configuration</div>
            <div class="card-value" id="mappingCount">0 tags</div>
            <div class="card-sub" id="updateRate">Update: — ms</div>
        </div>
    </div>

    <p class="section-header">OPC UA Endpoint</p>
    <div class="endpoint-box" id="uaEndpoint">—</div>
    <br>

    <p class="section-header">OPC DA mode</p>
    <div class="table-wrap">
        <table>
            <tbody>
                <tr>
                    <td class="label-cell">Source mode</td>
                    <td>
                        <div class="mode-row">
                            <select id="modeSelect">
                                <option value="Simulation">Simulation</option>
                                <option value="OpcDa">Real OPC DA</option>
                            </select>
                            <button id="modeApply" type="button">Apply</button>
                            <span class="mode-msg" id="modeMessage">No pending change</span>
                        </div>
                    </td>
                </tr>
            </tbody>
        </table>
    </div>

    <p class="section-header">OPC DA server browser</p>
    <div class="table-wrap" style="margin-bottom:28px">
        <!-- Tabs -->
        <div style="display:flex; border-bottom:1px solid var(--border); background:var(--surface2);">
            <button class="tab-btn active" id="tabLocal" onclick="switchTab('local')" type="button">Local</button>
            <button class="tab-btn" id="tabRemote" onclick="switchTab('remote')" type="button">Remote</button>
        </div>

        <!-- Local tab -->
        <div id="paneLocal" style="padding:14px 16px;">
            <div class="mode-row" style="margin-bottom:10px;">
                <button id="btnReloadLocal" type="button" onclick="browseServers(null)">⟳ Reload</button>
                <span class="mode-msg" id="msgLocal">—</span>
            </div>
            <div id="listLocal"><span style="color:var(--muted);font-size:13px">Click Reload to scan local OPC DA servers.</span></div>
        </div>

        <!-- Remote tab -->
        <div id="paneRemote" style="padding:14px 16px; display:none;">
            <div class="mode-row" style="margin-bottom:10px;">
                <input id="remoteHost" type="text" placeholder="192.168.x.x or hostname" style="width:220px">
                <button id="btnReloadRemote" type="button" onclick="browseServers(document.getElementById('remoteHost').value)">⟳ Browse</button>
                <span class="mode-msg" id="msgRemote">—</span>
            </div>
            <div id="listRemote"><span style="color:var(--muted);font-size:13px">Enter a host and click Browse.</span></div>
        </div>

        <!-- Selected server row -->
        <div style="border-top:1px solid var(--border); padding:12px 16px; background:var(--surface2); display:flex; align-items:center; gap:10px; flex-wrap:wrap;">
            <span style="color:var(--muted);font-size:12px;text-transform:uppercase;letter-spacing:.06em">Selected</span>
            <input id="cfgProgId" type="text" placeholder="ProgID" style="flex:1;min-width:200px">
            <input id="cfgHost" type="text" placeholder="Host (localhost or IP)" style="width:180px">
            <button id="cfgApply" type="button" onclick="applyServerConfig()">Apply</button>
            <span class="mode-msg" id="cfgMessage">—</span>
        </div>
    </div>

    <p class="section-header">Live values — OPC DA → Bridge</p>
    <div class="table-wrap">
        <table>
            <thead>
                <tr><th>DA Item ID</th><th>Value</th><th>Quality</th><th>Timestamp (local)</th></tr>
            </thead>
            <tbody id="values"><tr><td colspan="4">Waiting for values…</td></tr></tbody>
        </table>
    </div>

</main>

<script>
const ESC = {'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'};
const esc = s => String(s??'').replace(/[&<>'"]/g, c => ESC[c]);
const el = id => document.getElementById(id);

let modeChangeInFlight = false;

function badge(text, cls) {
    return `<span class="badge ${cls}">${esc(text)}</span>`;
}

function stateClass(v) {
    if (!v) return 'warn';
    const s = v.toLowerCase();
    if (s === 'running' || s === 'connected') return 'good';
    if (s === 'faulted' || s === 'stopped' || s === 'disconnected') return 'bad';
    return 'warn'; // Starting, Reconnecting, Unknown, etc.
}

function relativeTime(utcStr) {
    if (!utcStr) return '—';
    const diff = Math.floor((Date.now() - new Date(utcStr).getTime()) / 1000);
    if (diff < 5)  return 'just now';
    if (diff < 60) return diff + 's ago';
    if (diff < 3600) return Math.floor(diff/60) + 'm ago';
    return new Date(utcStr).toLocaleTimeString();
}

function localTime(utcStr) {
    if (!utcStr) return '—';
    return new Date(utcStr).toLocaleString();
}

function get(o, key) {
    return o[key] ?? o[key[0].toUpperCase() + key.slice(1)];
}

async function refreshDashboard() {
    try {
        const r = await fetch('/api/dashboard', { cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const p = await r.json();
        const b = p.bridge || p.Bridge || {};
        const ua = p.ua || p.Ua || {};
        const values = p.values || p.Values || [];

        // Header
        el('pulseDot').className = 'pulse-dot';
        el('refresh').textContent = 'Updated ' + new Date().toLocaleTimeString();

        const endpoint = get(ua, 'endpointUrl') || 'opc.tcp://…';
        el('headerEndpoint').textContent = endpoint;
        el('uaEndpoint').textContent = endpoint;

        // Cards
        const bsClass = stateClass(get(b, 'bridgeState'));
        el('bridgeState').innerHTML = badge(get(b, 'bridgeState') || '—', bsClass);

        const err = get(b, 'lastError');
        el('lastError').textContent = err || 'No errors';
        el('lastError').className = 'card-sub' + (err ? ' bad' : '');

        const dsClass = stateClass(get(b, 'daConnectionState'));
        el('daState').innerHTML = badge(get(b, 'daConnectionState') || '—', dsClass);
        el('daMode').textContent = 'Mode: ' + (get(b, 'daMode') || '—');

        el('lastDaRead').textContent = relativeTime(get(b, 'lastDaReadUtc'));
        el('lastDaReadCount').textContent = (get(b, 'lastDaReadCount') ?? 0) + ' values';

        el('lastUaWrite').textContent = relativeTime(get(b, 'lastUaWriteUtc'));
        el('lastUaWriteCount').textContent = (get(b, 'lastUaWriteCount') ?? 0) + ' values';

        const usClass = stateClass(get(ua, 'state'));
        el('uaState').innerHTML = badge(get(ua, 'state') || '—', usClass);
        el('uaClients').textContent = (get(ua, 'connectedClientCount') ?? 0) + ' clients';

        el('mappingCount').textContent = (get(b, 'mappingCount') ?? 0) + ' tags';
        el('updateRate').textContent = 'Update: ' + (get(b, 'updateRateMs') ?? '—') + ' ms';

        // Mode select
        if (!modeChangeInFlight) {
            el('modeSelect').value = get(b, 'daMode') || 'Simulation';
        }

        // Values table
        el('values').innerHTML = values.length
            ? values.map(item => {
                const isGood = get(item, 'isGood');
                const quality = get(item, 'daQuality');
                const qBadge = badge(isGood ? 'Good' : 'Bad', isGood ? 'good' : 'bad');
                return `<tr>
                    <td><code>${esc(get(item,'daItemId'))}</code></td>
                    <td>${esc(String(get(item,'value') ?? ''))}</td>
                    <td>${qBadge} <span class="${isGood?'good':'bad'}" style="font-size:12px">(${quality})</span></td>
                    <td>${esc(localTime(get(item,'timestampUtc')))}</td>
                </tr>`;
            }).join('')
            : '<tr><td colspan="4">No values yet.</td></tr>';

    } catch (e) {
        el('pulseDot').className = 'pulse-dot offline';
        el('refresh').textContent = 'Offline — ' + e.message;
        el('values').innerHTML = `<tr><td colspan="4" class="bad">${esc(e.message)}</td></tr>`;
    }
}

async function applyModeChange() {
    const sel = el('modeSelect'), btn = el('modeApply'), msg = el('modeMessage');
    modeChangeInFlight = true;
    sel.disabled = btn.disabled = true;
    msg.textContent = 'Applying…';
    msg.className = 'mode-msg warn';
    try {
        const r = await fetch('/api/da/mode', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ mode: sel.value })
        });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const p = await r.json();
        msg.textContent = '✓ Applied: ' + (p.mode || sel.value);
        msg.className = 'mode-msg good';
        await refreshDashboard();
    } catch (e) {
        msg.textContent = '✗ ' + e.message;
        msg.className = 'mode-msg bad';
    } finally {
        modeChangeInFlight = false;
        sel.disabled = btn.disabled = false;
    }
}

el('modeApply').addEventListener('click', applyModeChange);
refreshDashboard();
setInterval(refreshDashboard, 1000);

// ── Server browser ────────────────────────────────────────────────
function switchTab(tab) {
    const isLocal = tab === 'local';
    el('tabLocal').classList.toggle('active', isLocal);
    el('tabRemote').classList.toggle('active', !isLocal);
    el('paneLocal').style.display  = isLocal ? '' : 'none';
    el('paneRemote').style.display = isLocal ? 'none' : '';
}

async function browseServers(host) {
    const isLocal = !host || host.trim() === '';
    const listEl = el(isLocal ? 'listLocal' : 'listRemote');
    const msgEl  = el(isLocal ? 'msgLocal'  : 'msgRemote');
    const btnEl  = el(isLocal ? 'btnReloadLocal' : 'btnReloadRemote');
    const displayHost = isLocal ? 'localhost' : host.trim();

    btnEl.disabled = true;
    msgEl.textContent = 'Scanning…';
    msgEl.className = 'mode-msg warn';
    listEl.innerHTML = '<span style="color:var(--muted);font-size:13px">Scanning…</span>';

    try {
        const url = '/api/da/servers' + (isLocal ? '' : '?host=' + encodeURIComponent(host.trim()));
        const r = await fetch(url, { cache: 'no-store' });
        const p = await r.json();
        if (p.error) throw new Error(p.error);

        const servers = p.servers || [];
        if (servers.length === 0) {
            listEl.innerHTML = '<span style="color:var(--muted);font-size:13px">No OPC DA servers found.</span>';
            msgEl.textContent = '0 servers';
            msgEl.className = 'mode-msg';
            return;
        }

        msgEl.textContent = servers.length + ' server' + (servers.length > 1 ? 's' : '') + ' found';
        msgEl.className = 'mode-msg good';

        listEl.innerHTML = servers.map((s, i) => {
            const id = `si-${isLocal?'l':'r'}-${i}`;
            return `<div class="server-item" id="${id}"
                onclick="selectServer(${JSON.stringify(s.progId||s.ProgId)},${JSON.stringify(displayHost)},'${id}')">
                <span class="s-name">${esc(s.description||s.Description||s.progId||s.ProgId)}</span>
                <span class="s-prog">${esc(s.progId||s.ProgId)}</span>
            </div>`;
        }).join('');
    } catch (e) {
        listEl.innerHTML = `<span style="color:var(--bad);font-size:13px">${esc(e.message)}</span>`;
        msgEl.textContent = '✗ ' + e.message;
        msgEl.className = 'mode-msg bad';
    } finally {
        btnEl.disabled = false;
    }
}

function selectServer(progId, host, itemId) {
    document.querySelectorAll('.server-item.selected').forEach(n => n.classList.remove('selected'));
    const item = document.getElementById(itemId);
    if (item) item.classList.add('selected');
    el('cfgProgId').value = progId;
    el('cfgHost').value   = host;
    el('cfgMessage').textContent = 'Selected — click Apply to connect';
    el('cfgMessage').className   = 'mode-msg warn';
}

// ── Server config apply ───────────────────────────────────────────
async function loadServerConfig() {
    try {
        const r = await fetch('/api/da/config', { cache: 'no-store' });
        if (!r.ok) return;
        const p = await r.json();
        el('cfgProgId').value = p.progId || p.ProgId || '';
        el('cfgHost').value   = p.host   || p.Host   || 'localhost';
    } catch {}
}

async function applyServerConfig() {
    const btn = el('cfgApply'), msg = el('cfgMessage');
    btn.disabled = true;
    msg.textContent = 'Applying…';
    msg.className = 'mode-msg warn';
    try {
        const r = await fetch('/api/da/config', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ progId: el('cfgProgId').value.trim(), host: el('cfgHost').value.trim() || 'localhost' })
        });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        msg.textContent = '✓ Saved — switch to OpcDa mode to connect';
        msg.className = 'mode-msg good';
    } catch (e) {
        msg.textContent = '✗ ' + e.message;
        msg.className = 'mode-msg bad';
    } finally {
        btn.disabled = false;
    }
}

loadServerConfig();
</script>
</body>
</html>
""";
}
