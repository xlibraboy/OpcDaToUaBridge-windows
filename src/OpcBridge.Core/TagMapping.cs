namespace OpcBridge.Core;

public sealed class TagMapping
{
    public string SourceId { get; set; } = "default";
    public string DaItemId { get; set; } = string.Empty;
    public string UaNodeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DataType { get; set; } = "Double";
    public bool Enabled { get; set; } = true;
    public string Mode { get; set; } = TagMode.Source;
    public string? ManualValue { get; set; }
    public int PollRateMs { get; set; }
    public float DeadbandPct { get; set; }
    public bool Writeable { get; set; }
}

public static class TagMode
{
    public const string Source = "Source";
    public const string Manual = "Manual";
}