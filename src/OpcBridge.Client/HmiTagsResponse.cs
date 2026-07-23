namespace OpcBridge.Client;

public sealed class HmiTagsResponse
{
    public long Version { get; set; }
    public IReadOnlyList<HmiTagDto> Tags { get; set; } = Array.Empty<HmiTagDto>();
}
