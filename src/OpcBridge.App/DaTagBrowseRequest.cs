namespace OpcBridge.App;

public sealed record DaTagBrowseRequest(
    string SourceId,
    string ProgId,
    string Host,
    string? Path = null,
    bool Recursive = false,
    string? RemoteUsername = null,
    string? RemotePassword = null,
    string? RemoteDomain = null);