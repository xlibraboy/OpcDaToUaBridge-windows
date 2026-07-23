using Microsoft.AspNetCore.SignalR.Client;
using OpcBridge.Client;

namespace OpcBridge.Hmi.Services;

public sealed class HmiHubClient : IAsyncDisposable
{
    private HubConnection? connection_;

    public event Func<string?, Task>? Reconnected;

    public async Task ConnectAsync(
        string baseUrl,
        Func<HmiValueDelta[], Task> onValues,
        Func<HmiMappingsChanged, Task> onMappingsChanged,
        CancellationToken ct)
    {
        await DisposeAsync().ConfigureAwait(false);

        string hubUrl = baseUrl.TrimEnd('/') + "/hmi";
        connection_ = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        connection_.On<HmiValueDelta[]>("values", async batch => await onValues(batch).ConfigureAwait(false));
        connection_.On<HmiMappingsChanged>("mappingsChanged", async msg => await onMappingsChanged(msg).ConfigureAwait(false));

        connection_.Reconnected += async connectionId =>
        {
            Func<string?, Task>? handler = Reconnected;
            if (handler is not null)
            {
                await handler(connectionId).ConfigureAwait(false);
            }
        };

        await connection_.StartAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (connection_ is not null)
        {
            await connection_.DisposeAsync().ConfigureAwait(false);
            connection_ = null;
        }
    }
}
