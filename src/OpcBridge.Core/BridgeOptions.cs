namespace OpcBridge.Core;

public sealed class BridgeOptions
{
    public List<TagMapping> Mappings { get; set; } = new();
    public Dictionary<int, int> RateLimits { get; set; } = new();
}