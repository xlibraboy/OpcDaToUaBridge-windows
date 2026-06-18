namespace OpcBridge.App;

public sealed record DaServerConfigRequest(
    string ProgId,
    string Host,
    string? RemoteUsername = null,
    string? RemotePassword = null,
    string? RemoteDomain   = null);
