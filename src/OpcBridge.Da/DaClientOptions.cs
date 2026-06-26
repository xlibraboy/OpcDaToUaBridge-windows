namespace OpcBridge.Da;

public sealed class DaClientOptions
{
    public string SourceId { get; set; } = "default";
    public string DisplayName { get; set; } = string.Empty;
    public string ProgId { get; set; } = string.Empty;
    public string Host { get; set; } = "localhost";
    public int UpdateRateMs { get; set; } = 1000;
    public string? RemoteUsername { get; set; }
    public string? RemotePassword { get; set; }
    public string? RemoteDomain { get; set; }
    public List<DaSourceOptions> Sources { get; set; } = new();
}

public sealed class DaSourceOptions
{
    public string SourceId { get; set; } = "default";
    public string DisplayName { get; set; } = string.Empty;
    public string ProgId { get; set; } = string.Empty;
    public string Host { get; set; } = "localhost";
    public int UpdateRateMs { get; set; } = 0;
    public string? RemoteUsername { get; set; }
    public string? RemotePassword { get; set; }
    public string? RemoteDomain { get; set; }
}