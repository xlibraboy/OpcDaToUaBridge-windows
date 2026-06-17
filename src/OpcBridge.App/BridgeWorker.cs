using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpcBridge.Core;
using OpcBridge.Da;
using OpcBridge.Ua;

namespace OpcBridge.App;

public sealed class BridgeWorker : BackgroundService
{
    private readonly UaServerHost ua_server_;
    private readonly BridgeState bridge_state_;
    private readonly BridgeOptions bridge_options_;
    private readonly DaRuntimeSettings da_settings_;
    private readonly DaClientFactory da_client_factory_;
    private readonly ILogger<BridgeWorker> logger_;

    public BridgeWorker(
        UaServerHost uaServer,
        BridgeState bridgeState,
        IOptions<BridgeOptions> bridgeOptions,
        DaRuntimeSettings daSettings,
        DaClientFactory daClientFactory,
        ILogger<BridgeWorker> logger)
    {
        ua_server_ = uaServer;
        bridge_state_ = bridgeState;
        bridge_options_ = bridgeOptions.Value;
        da_settings_ = daSettings;
        da_client_factory_ = daClientFactory;
        logger_ = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DaRuntimeSettingsSnapshot settings = da_settings_.GetSnapshot();
        bridge_state_.Configure(settings.Mode, settings.UpdateRateMs, bridge_options_.Mappings.Count);

        try
        {
            logger_.LogInformation("Starting bridge with {MappingCount} mappings", bridge_options_.Mappings.Count);
            await ua_server_.StartAsync(bridge_options_.Mappings, stoppingToken).ConfigureAwait(false);

            long connectedVersion = -1;
            IDaClient? daClient = null;

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    settings = da_settings_.GetSnapshot();

                    try
                    {
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
                            bridge_state_.Configure(settings.Mode, settings.UpdateRateMs, bridge_options_.Mappings.Count);
                            bridge_state_.SetDaConnectionState("Connecting");

                            daClient = da_client_factory_.Create(settings);
                            await daClient.ConnectAsync(stoppingToken).ConfigureAwait(false);
                            connectedVersion = settings.Version;
                            bridge_state_.SetDaConnectionState("Connected");
                        }

                        IReadOnlyList<BridgeValue> values = await daClient
                            .ReadAsync(bridge_options_.Mappings, stoppingToken)
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
