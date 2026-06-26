namespace OpcBridge.App;

public sealed record DaServerConfigRequest(
    string SourceId,
    string? DisplayName,
    string ProgId,
    string Host,
    string? RemoteUsername = null,
    string? RemotePassword = null,
    string? RemoteDomain = null,
    int UpdateRateMs = 0);