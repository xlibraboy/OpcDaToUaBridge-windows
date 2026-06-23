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
        bridge_state_.Configure(settings.UpdateRateMs, mappings.Count, settings.Sources);

        try
        {
            logger_.LogInformation("Starting bridge with {MappingCount} mappings across {SourceCount} sources", mappings.Count, settings.Sources.Count);
            await ua_server_.StartAsync(mappings, stoppingToken).ConfigureAwait(false);

            long uaMappingVersion = mappingVersion;
            long connectedVersion = -1;
            Dictionary<string, SourceSession> sessions = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    settings = da_settings_.GetSnapshot();
                    (mappings, mappingVersion) = mapping_store_.GetSnapshot();

                    try
                    {
                        if (mappingVersion != uaMappingVersion)
                        {
                            ua_server_.SyncMappings(mappings);
                            bridge_state_.RetainMappedValues(mappings);
                            uaMappingVersion = mappingVersion;
                            connectedVersion = -1;
                            bridge_state_.UpdateSources(settings.UpdateRateMs, mappings.Count, settings.Sources);
                            logger_.LogInformation("Applied tag mapping change: {Count} mappings", mappings.Count);
                        }

                        if (connectedVersion != settings.Version)
                        {
                            await ReconfigureSessionsAsync(settings, sessions, stoppingToken).ConfigureAwait(false);
                            connectedVersion = settings.Version;
                            bridge_state_.UpdateSources(settings.UpdateRateMs, mappings.Count, settings.Sources);
                        }

                        int totalValueCount = 0;
                        bool anySuccess = false;

                        for (int i = 0; i < settings.Sources.Count; i++)
                        {
                            DaSourceRuntimeSettings source = settings.Sources[i];
                            IReadOnlyList<TagMapping> sourceMappings = mappings
                                .Where(mapping => string.Equals(mapping.SourceId, source.SourceId, StringComparison.OrdinalIgnoreCase))
                                .ToArray();

                            if (!sessions.TryGetValue(source.SourceId, out SourceSession? session))
                            {
                                continue;
                            }

                            try
                            {
                                bridge_state_.SetSourceConnectionState(source.SourceId, "Connected");
                                IReadOnlyList<BridgeValue> values = await session.Client
                                    .ReadAsync(sourceMappings, stoppingToken)
                                    .ConfigureAwait(false);

                                bridge_state_.UpdateDaRead(source.SourceId, values);
                                for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
                                {
                                    ua_server_.UpdateValue(values[valueIndex]);
                                }

                                totalValueCount += values.Count;
                                anySuccess = true;
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception exception)
                            {
                                bridge_state_.SetSourceError(source.SourceId, exception);
                                bridge_state_.ClearSourceValues(source.SourceId);
                                logger_.LogWarning(exception, "Source {SourceId} read failed", source.SourceId);

                                await session.Client.DisposeAsync().ConfigureAwait(false);
                                sessions.Remove(source.SourceId);
                                connectedVersion = -1;
                            }
                        }

                        if (anySuccess)
                        {
                            bridge_state_.SetBridgeState("Running");
                            bridge_state_.MarkUaWrite(totalValueCount);
                        }

                        await Task.Delay(settings.UpdateRateMs, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        bridge_state_.SetError(exception);
                        logger_.LogError(exception, "Bridge loop failed");
                        await DisposeSessionsAsync(sessions).ConfigureAwait(false);
                        sessions.Clear();
                        connectedVersion = -1;
                        await Task.Delay(settings.UpdateRateMs, stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                await DisposeSessionsAsync(sessions).ConfigureAwait(false);
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

    private async Task ReconfigureSessionsAsync(
        DaRuntimeSettingsSnapshot settings,
        Dictionary<string, SourceSession> sessions,
        CancellationToken cancellationToken)
    {
        HashSet<string> desiredSources = settings.Sources
            .Select(source => source.SourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach ((string sourceId, SourceSession session) in sessions.ToArray())
        {
            if (desiredSources.Contains(sourceId))
            {
                continue;
            }

            await session.Client.DisposeAsync().ConfigureAwait(false);
            sessions.Remove(sourceId);
            bridge_state_.ClearSourceValues(sourceId);
        }

        for (int i = 0; i < settings.Sources.Count; i++)
        {
            DaSourceRuntimeSettings source = settings.Sources[i];

            if (sessions.Remove(source.SourceId, out SourceSession? existing))
            {
                await existing.Client.DisposeAsync().ConfigureAwait(false);
            }

            bridge_state_.SetSourceConnectionState(source.SourceId, "Connecting");
            IDaClient client = da_client_factory_.Create(settings, source);
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            sessions[source.SourceId] = new SourceSession(source, client);
            bridge_state_.SetSourceConnectionState(source.SourceId, "Connected");
        }
    }

    private static async Task DisposeSessionsAsync(Dictionary<string, SourceSession> sessions)
    {
        foreach (SourceSession session in sessions.Values)
        {
            await session.Client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed record SourceSession(DaSourceRuntimeSettings Source, IDaClient Client);
}
