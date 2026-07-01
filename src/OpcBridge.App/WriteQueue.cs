using System.Threading.Channels;
using OpcBridge.Core;

namespace OpcBridge.App;

/// <summary>
/// A bounded channel that serializes UA→DA writes. UA node write handlers enqueue
/// a <see cref="WriteRequest"/> (non-blocking); per-source consumers drain the queue
/// and call <c>IDaClient.WriteAsync</c>, keeping COM work on each source's STA thread.
/// </summary>
internal sealed class WriteQueue
{
    private readonly Channel<WriteRequest> channel_ = Channel.CreateBounded<WriteRequest>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(WriteRequest req, CancellationToken ct)
    {
        return channel_.Writer.WriteAsync(req, ct);
    }

    public IAsyncEnumerable<WriteRequest> ReaderAsync(CancellationToken ct)
    {
        return channel_.Reader.ReadAllAsync(ct);
    }
}

internal sealed record WriteRequest(
    string SourceId,
    string DaItemId,
    object? Value,
    TaskCompletionSource<bool> Tcs);
