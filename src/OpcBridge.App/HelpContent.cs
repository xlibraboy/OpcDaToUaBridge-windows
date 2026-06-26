namespace OpcBridge.App;

internal static class HelpContent
{
    public const string Markdown = """
# Basic Workflow

- Use **Connection** to configure the OPC DA source, host, credentials, and polling rates.
- Use **Tags** to browse DA items, create DA → OPC UA mappings, and set per-tag poll rates.
- Use **Monitor** to confirm source reads, live values, rate-group alarms, and OPC UA writes.
- Use **Logs** to review warnings and errors from the bridge and UA server.

---

# Poll Rate & Tag Limits

- Each tag can be assigned its own poll rate via the faceplate (Tags tab → click a tag). Tags with the same rate share one OPC DA group.
- The default rate (Connection tab → Polling section) applies only to new sources and tags set to "Source Default".
- Watch the alarm bar on the Monitor tab: <span class="good">green</span> = within limits, <span class="warn">yellow</span> = cycle budget warning, <span class="bad">red</span> = limit exceeded or saturated.

## Default tag limits per rate

*(appsettings.json → `Bridge:RateLimits`)*

| Rate | Max Tags | Basis |
|------|----------|-------|
| 100 ms | 200 | COM device read ~0.4ms/item; 80ms budget at 80% cycle |
| 250 ms | 500 | Cached reads ~0.4ms; 200ms budget |
| 500 ms | 1,000 | Mixed cache/device ~0.4ms; 400ms budget |
| 1 s | 5,000 | Cached reads; 800ms budget; UA lock ~50ms |
| 2 s | 10,000 | ~1.6s budget; UA updates ~10K/sec |
| 5 s | 20,000 | ~4s budget; network ~2MB/s per UA client |
| 10 s | 50,000 | ~8s budget; network ~5MB/s; lock contention monitor |

## How limits are derived

- **DA COM read time** — `IOPCSyncIO.Read` with N items takes ~0.4–1ms/item (cache) or 2–5ms/item (device). Limit = 80% of rate interval ÷ per-item read time.
- **UA server lock** — each `UpdateValue` holds a lock for ~5–10μs. At 5000 tags × 500ms = 10K updates/sec, lock contention becomes measurable.
- **Network bandwidth** — each UA client notification is ~50–100 bytes. At 5000 tags × 500ms ≈ 500KB/s per client; 50K tags × 100ms ≈ 5MB/s saturates 100Mbps LAN.
- Limits are **conservative estimates**, not hard ceilings. Adjust in `appsettings.json` for your hardware and network. The alarm bar warns before degradation.

---

# Manual Override & Tag Modes

- **Source mode** — the tag publishes the live value read from the DA server (default).
- **Manual mode** — the tag publishes a fixed value you set, overriding the DA read. Switching to Manual with an empty field auto-copies the current live value.
- **Disabled** — the tag is not published to OPC UA and not read from DA.
- Open a tag's faceplate (Tags tab → click a tag) to change mode, set manual value, or adjust poll rate.

---

# OPC UA Server

- The bridge runs a built-in OPC UA server. UA clients connect to the endpoint shown on the Monitor tab.
- Each DA tag mapping creates one UA variable node under the "OPC DA Tags" folder (namespace index 2).
- Node IDs follow the pattern `ns=2;s={sourceId}/{daItemId}` unless a custom UA Node ID is specified.
- The UA server supports read and subscription (monitored items). Writes from UA clients are not supported (read-only bridge).

---

# Troubleshooting

- **DA browse fails** — check ProgID, host reachability, DCOM permissions, and credentials (Connection tab → Credentials section).
- **Values stop moving** — check Monitor → Source Status for connection state and last read timing. Check the alarm bar for rate-group saturation.
- **Tags not appearing in UA** — verify the tag is Enabled and in Source mode (Tags tab → faceplate). Check Monitor → OPC UA Endpoint for node count.
- **Rate group saturated** — the read time exceeds 80% of the poll rate. Increase the rate or reduce the number of tags in that rate group.
- **Tag limit exceeded** — the number of tags in a rate group exceeds the configured limit. Move some tags to a slower rate or increase the limit in `appsettings.json`.

---

# Configuration Reference

## appsettings.json

- **Da:ProgId** — OPC DA server ProgID (e.g. `Matrikon.OPC.Simulation.1`)
- **Da:Host** — DA server host (localhost or remote IP)
- **Da:UpdateRateMs** — default poll rate for new sources (min 100ms)
- **Ua:EndpointUrl** — OPC UA server endpoint (default `opc.tcp://0.0.0.0:4840/OpcDaToUaBridge`)
- **Ua:AutoAcceptUntrustedCertificates** — accept untrusted UA client certs (dev/test)
- **Bridge:RateLimits** — max tags per rate group (rate ms → max tags)
- **Bridge:Mappings** — initial tag mappings loaded at startup
""";
}
