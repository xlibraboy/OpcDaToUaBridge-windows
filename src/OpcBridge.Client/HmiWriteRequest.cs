namespace OpcBridge.Client;

public sealed class HmiWriteRequest
{
    public string SourceId { get; set; } = string.Empty;
    public string DaItemId { get; set; } = string.Empty;
    public object? Value { get; set; }
}
