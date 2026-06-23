namespace OpcBridge.Core;

public sealed class TagMapping
{
    public string SourceId { get; set; } = "default";
    public string DaItemId { get; set; } = string.Empty;
    public string UaNodeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataType { get; set; } = "Double";
}