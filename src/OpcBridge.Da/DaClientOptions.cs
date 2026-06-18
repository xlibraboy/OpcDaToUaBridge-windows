namespace OpcBridge.Da;

public sealed class DaClientOptions
{
    public string Mode { get; set; } = "Simulation";
    public string ProgId { get; set; } = string.Empty;
    public string Host { get; set; } = "localhost";
    public int UpdateRateMs { get; set; } = 1000;

    // Optional credentials for remote DCOM access (workgroup / cross-domain)
    public string? RemoteUsername { get; set; }
    public string? RemotePassword { get; set; }
    public string? RemoteDomain   { get; set; }
}
