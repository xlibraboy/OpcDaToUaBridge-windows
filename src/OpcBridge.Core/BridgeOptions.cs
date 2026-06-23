namespace OpcBridge.Core;

public sealed class BridgeOptions
{
    public List<TagMapping> Mappings { get; set; } = new();
}