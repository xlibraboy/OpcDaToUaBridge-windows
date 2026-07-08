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
    public string AccessRights { get; set; } = TagAccessRights.Read;
    public bool MqttEnabled { get; set; }
    public string? MqttTopic { get; set; }
}

public static class TagMode
{
    public const string Source = "Source";
    public const string Manual = "Manual";
}

public static class TagAccessRights
{
    public const string Read = "Read";
    public const string ReadWrite = "Read-Write";
    public const string Write = "Write";
}
