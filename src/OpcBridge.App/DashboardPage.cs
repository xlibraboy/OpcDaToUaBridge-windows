namespace OpcBridge.App;

internal static class DashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>OPC DA to OPC UA Bridge</title>
    <style>
        :root { color-scheme: dark; font-family: Arial, sans-serif; }
        body { margin: 0; background: #101418; color: #e8eef5; }
        header { padding: 20px 28px; background: #17202a; border-bottom: 1px solid #2c3e50; }
        h1 { margin: 0 0 6px; font-size: 22px; }
        main { padding: 24px 28px; }
        .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 12px; margin-bottom: 24px; }
        .card { background: #17202a; border: 1px solid #2c3e50; border-radius: 8px; padding: 14px; min-height: 68px; }
        .label { color: #9fb3c8; font-size: 12px; text-transform: uppercase; letter-spacing: .06em; }
        .value { margin-top: 7px; font-size: 18px; font-weight: 700; overflow-wrap: anywhere; }
        .subvalue { margin-top: 5px; color: #9fb3c8; font-size: 13px; overflow-wrap: anywhere; }
        .section-title { margin: 0 0 10px; color: #c8d7e6; font-size: 16px; }
        table { width: 100%; border-collapse: collapse; background: #17202a; border: 1px solid #2c3e50; }
        th, td { padding: 10px 12px; border-bottom: 1px solid #2c3e50; text-align: left; }
        th { color: #9fb3c8; font-size: 12px; text-transform: uppercase; letter-spacing: .06em; }
        tr:last-child td { border-bottom: 0; }
        .good { color: #6ee7b7; }
        .bad { color: #fca5a5; }
        .warn { color: #fcd34d; }
        code { color: #93c5fd; }
    </style>
</head>
<body>
<header>
    <h1>OPC DA to OPC UA Bridge</h1>
    <div>Runtime dashboard for simulation on Linux/Docker or real OPC DA on Windows. OPC UA endpoint: <code>opc.tcp://&lt;host-ip&gt;:4840/OpcDaToUaBridge</code></div>
</header>
<main>
    <section class="cards">
        <div class="card"><div class="label">Dashboard</div><div class="value" id="dashboardStatus">Connecting</div><div class="subvalue" id="refresh">Last refresh: -</div></div>
        <div class="card"><div class="label">Bridge runtime</div><div class="value" id="bridgeState">-</div><div class="subvalue" id="lastError">No errors</div></div>
        <div class="card"><div class="label">OPC DA client</div><div class="value" id="daState">-</div><div class="subvalue" id="daMode">Mode: -</div></div>
        <div class="card"><div class="label">Last DA read</div><div class="value" id="lastDaRead">-</div><div class="subvalue" id="lastDaReadCount">0 values</div></div>
        <div class="card"><div class="label">OPC UA server</div><div class="value" id="uaState">-</div><div class="subvalue" id="uaEndpoint">-</div></div>
        <div class="card"><div class="label">UA clients</div><div class="value" id="uaClients">0</div><div class="subvalue">Activated sessions</div></div>
        <div class="card"><div class="label">Last UA write</div><div class="value" id="lastUaWrite">-</div><div class="subvalue" id="lastUaWriteCount">0 values</div></div>
        <div class="card"><div class="label">Configuration</div><div class="value" id="mappingCount">0 tags</div><div class="subvalue" id="updateRate">Update: - ms</div></div>
    </section>

    <h2 class="section-title">OPC DA mode</h2>
    <table>
        <tbody>
            <tr>
                <td style="width: 220px;">Source mode</td>
                <td>
                    <select id="modeSelect">
                        <option value="Simulation">Simulation</option>
                        <option value="OpcDa">Real OPC DA</option>
                    </select>
                    <button id="modeApply" type="button">Apply</button>
                    <span id="modeMessage" class="subvalue">No pending change</span>
                </td>
            </tr>
        </tbody>
    </table>

    <h2 class="section-title">Live values entering bridge from OPC DA side</h2>
    <table>
        <thead>
            <tr><th>DA Item</th><th>Value</th><th>Quality</th><th>Timestamp UTC</th></tr>
        </thead>
        <tbody id="values"><tr><td colspan="4">Waiting for values...</td></tr></tbody>
    </table>
</main>
<script>
let modeChangeInFlight = false;

async function refreshDashboard() {
    const dashboardStatus = document.getElementById('dashboardStatus');
    const tbody = document.getElementById('values');
    try {
        const response = await fetch('/api/dashboard', { cache: 'no-store' });
        if (!response.ok) throw new Error('HTTP ' + response.status);
        const payload = await response.json();
        const bridge = payload.bridge || payload.Bridge || {};
        const ua = payload.ua || payload.Ua || {};
        const values = payload.values || payload.Values || [];
        const mode = get(bridge, 'daMode') || 'Simulation';

        dashboardStatus.textContent = 'Online';
        dashboardStatus.className = 'value good';
        text('refresh', 'Last refresh: ' + new Date().toLocaleTimeString());

        setValue('bridgeState', get(bridge, 'bridgeState'), stateClass(get(bridge, 'bridgeState')));
        text('lastError', get(bridge, 'lastError') || 'No errors');
        document.getElementById('lastError').className = get(bridge, 'lastError') ? 'subvalue bad' : 'subvalue';

        setValue('daState', get(bridge, 'daConnectionState'), stateClass(get(bridge, 'daConnectionState')));
        text('daMode', 'Mode: ' + mode);
        text('lastDaRead', formatTime(get(bridge, 'lastDaReadUtc')));
        text('lastDaReadCount', String(get(bridge, 'lastDaReadCount') ?? 0) + ' values');

        setValue('uaState', get(ua, 'state'), stateClass(get(ua, 'state')));
        text('uaEndpoint', get(ua, 'endpointUrl') || '-');
        text('uaClients', String(get(ua, 'connectedClientCount') ?? 0));

        text('lastUaWrite', formatTime(get(bridge, 'lastUaWriteUtc')));
        text('lastUaWriteCount', String(get(bridge, 'lastUaWriteCount') ?? 0) + ' values');
        text('mappingCount', String(get(bridge, 'mappingCount') ?? 0) + ' tags');
        text('updateRate', 'Update: ' + String(get(bridge, 'updateRateMs') ?? '-') + ' ms');

        const modeSelect = document.getElementById('modeSelect');
        if (!modeChangeInFlight) {
            modeSelect.value = mode;
        }

        tbody.innerHTML = values.map(item => {
            const daItemId = get(item, 'daItemId');
            const value = get(item, 'value');
            const isGood = get(item, 'isGood');
            const quality = get(item, 'daQuality');
            const timestamp = get(item, 'timestampUtc');
            const qualityClass = isGood ? 'good' : 'bad';
            const qualityText = isGood ? 'Good' : 'Bad';
            return `<tr><td>${escapeHtml(daItemId)}</td><td>${escapeHtml(String(value))}</td><td class="${qualityClass}">${qualityText} (${quality})</td><td>${escapeHtml(timestamp)}</td></tr>`;
        }).join('') || '<tr><td colspan="4">No values yet.</td></tr>';
    } catch (error) {
        dashboardStatus.textContent = 'Offline';
        dashboardStatus.className = 'value bad';
        tbody.innerHTML = `<tr><td colspan="4">${escapeHtml(error.message)}</td></tr>`;
    }
}

async function applyModeChange() {
    const modeSelect = document.getElementById('modeSelect');
    const modeApply = document.getElementById('modeApply');
    const modeMessage = document.getElementById('modeMessage');
    modeChangeInFlight = true;
    modeSelect.disabled = true;
    modeApply.disabled = true;
    modeMessage.textContent = 'Applying mode...';
    modeMessage.className = 'subvalue warn';

    try {
        const response = await fetch('/api/da/mode', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ mode: modeSelect.value })
        });

        if (!response.ok) {
            throw new Error('HTTP ' + response.status);
        }

        const payload = await response.json();
        modeMessage.textContent = 'Applied mode: ' + (payload.mode || modeSelect.value);
        modeMessage.className = 'subvalue good';
        await refreshDashboard();
    } catch (error) {
        modeMessage.textContent = error.message;
        modeMessage.className = 'subvalue bad';
    } finally {
        modeChangeInFlight = false;
        modeSelect.disabled = false;
        modeApply.disabled = false;
    }
}

function get(object, camelName) {
    const pascalName = camelName.charAt(0).toUpperCase() + camelName.slice(1);
    return object[camelName] ?? object[pascalName];
}

function text(id, value) {
    document.getElementById(id).textContent = value ?? '-';
}

function setValue(id, value, className) {
    const element = document.getElementById(id);
    element.textContent = value || '-';
    element.className = 'value ' + className;
}

function stateClass(value) {
    if (value === 'Running' || value === 'Connected') return 'good';
    if (value === 'Faulted' || value === 'Stopped' || value === 'Disconnected') return 'bad';
    return 'warn';
}

function formatTime(value) {
    return value ? new Date(value).toLocaleTimeString() : '-';
}

function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>'"]/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[char]));
}

document.getElementById('modeApply').addEventListener('click', applyModeChange);
refreshDashboard();
setInterval(refreshDashboard, 1000);
</script>
</body>
</html>
""";
}
