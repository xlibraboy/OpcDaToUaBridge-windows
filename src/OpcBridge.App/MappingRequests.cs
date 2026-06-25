namespace OpcBridge.App;

public sealed record MappingTagDto(
    string SourceId,
    string DaItemId,
    string? DisplayName = null,
    string? DataType = null,
    string? UaNodeId = null,
    bool? Enabled = null,
    string? Mode = null,
    string? ManualValue = null);

public sealed record MappingAddRequest(List<MappingTagDto>? Tags);

public sealed record MappingRemoveRequest(string SourceId, string DaItemId);

public sealed record MappingUpdateRequest(MappingTagDto Tag);