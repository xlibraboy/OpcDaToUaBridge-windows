using OpcBridge.Core;

namespace OpcBridge.Da;

public interface IDaClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BridgeValue>> ReadAsync(
        IReadOnlyList<TagMapping> mappings,
        CancellationToken cancellationToken);

    Task<bool> WriteAsync(string daItemId, object? value, CancellationToken cancellationToken);

    bool TryGetTagMetadata(string daItemId, out short? canonicalDataType, out int? accessRights);
}
