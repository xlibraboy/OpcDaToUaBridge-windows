using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpcBridge.Core;
using OpcBridge.Da;
using OpcBridge.Ua;

namespace OpcBridge.App;

public sealed class BridgeWorker : BackgroundService
{
    private readonly UaServerHost ua_server_;
    private readonly BridgeState bridge_state_;
    private readonly MappingStore mapping_store_;
    private readonly DaRuntimeSettings da_settings_;
    private readonly DaClientFactory da_client_factory_;
    private readonly ILogger<BridgeWorker> logger_;

    public BridgeWorker(
        UaServerHost uaServer,
        BridgeState bridgeState,
        MappingStore mappingStore,
        DaRuntimeSettings daSettings,
        DaClientFactory daClientFactory,
        ILogger<BridgeWorker> logger)
    {
        ua_server_ = uaServer;
        bridge_state_ = bridgeState;
        mapping_store_ = mappingStore;
        da_settings_ = daSettings;
        da_client_factory_ = daClientFactory;
        logger_ = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DaRuntimeSettingsSnapshot settings = da_settings_.GetSnapshot();
        (IReadOnlyList<TagMapping> mappings, long mappingVersion) = mapping_store_.GetSnapshot();
        bridge_state_.Configure(settings.Mode, settings.UpdateRateMs, mappings.Count);

        try
        {
            logger_.LogInformation("Starting bridge with {MappingCount} mappings", mappings.Count);
            await ua_server_.StartAsync(mappings, stoppingToken).ConfigureAwait(false);

            long connectedVersion = -1;
            long uaMappingVersion = mappingVersion;
            IDaClient? daClient = null;

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    settings = da_settings_.GetSnapshot();
                    (mappings, mappingVersion) = mapping_store_.GetSnapshot();

                    try
                    {
                        // Apply tag mapping changes live (add/remove UA nodes) and force DA refresh.
                        if (mappingVersion != uaMappingVersion)
                        {
                            ua_server_.SyncMappings(mappings);
                            uaMappingVersion = mappingVersion;
                            connectedVersion = -1; // force DA client to reconfigure items
                            bridge_state_.Configure(settings.Mode, settings.UpdateRateMs, mappings.Count);
                            logger_.LogInformation("Applied tag mapping change: {Count} mappings", mappings.Count);
                        }

                        if (daClient is null || connectedVersion != settings.Version)
                        {
                            if (daClient is not null)
                            {
                                bridge_state_.SetBridgeState("Switching");
                                bridge_state_.SetDaConnectionState("Reconnecting");
                                bridge_state_.ClearValues();
                                await daClient.DisposeAsync().ConfigureAwait(false);
                                daClient = null;
                            }

                            bridge_state_.SetDaMode(settings.Mode);
                            bridge_state_.Configure(settings.Mode, settings.UpdateRateMs, mappings.Count);
                            bridge_state_.SetDaConnectionState("Connecting");

                            daClient = da_client_factory_.Create(settings);
                            await daClient.ConnectAsync(stoppingToken).ConfigureAwait(false);
                            connectedVersion = settings.Version;
                            bridge_state_.SetDaConnectionState("Connected");
                        }

                        IReadOnlyList<BridgeValue> values = await daClient
                            .ReadAsync(mappings, stoppingToken)
                            .ConfigureAwait(false);

                        bridge_state_.UpdateDaRead(values);
                        for (int i = 0; i < values.Count; i++)
                        {
                            ua_server_.UpdateValue(values[i]);
                        }

                        bridge_state_.MarkUaWrite(values.Count);
                        await Task.Delay(settings.UpdateRateMs, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        bridge_state_.SetError(exception);

                        if (daClient is not null)
                        {
                            await daClient.DisposeAsync().ConfigureAwait(false);
                            daClient = null;
                        }

                        connectedVersion = -1;
                        bridge_state_.SetDaConnectionState("Disconnected");
                        await Task.Delay(settings.UpdateRateMs, stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (daClient is not null)
                {
                    await daClient.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            bridge_state_.SetBridgeState("Stopping");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        bridge_state_.SetBridgeState("Stopping");
        await ua_server_.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        bridge_state_.SetDaConnectionState("Disconnected");
        bridge_state_.SetBridgeState("Stopped");
    }
}
