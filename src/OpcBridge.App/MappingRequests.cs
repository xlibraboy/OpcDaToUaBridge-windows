namespace OpcBridge.App;

public sealed record MappingTagDto(
    string DaItemId,
    string? DisplayName = null,
    string? DataType = null,
    string? UaNodeId = null);

public sealed record MappingAddRequest(List<MappingTagDto>? Tags);

public sealed record MappingRemoveRequest(string DaItemId);
