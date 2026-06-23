using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpcBridge.Core;
using OpcBridge.Da;
using OpcBridge.Ua;

namespace OpcBridge.App;

public sealed class BridgeWorker : BackgroundService
{
    private const int MaxConcurrentSourcePolls = 8;

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
        SourceMappingCache sourceMappingCache = SourceMappingCache.Build(mappings);
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
                            sourceMappingCache = SourceMappingCache.Build(mappings);
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

                        Stopwatch cycleTimer = Stopwatch.StartNew();
                        SourcePollSummary pollSummary = await PollSourcesAsync(
                            settings,
                            sessions,
                            sourceMappingCache,
                            stoppingToken).ConfigureAwait(false);
                        cycleTimer.Stop();

                        if (pollSummary.FailedSourceIds.Count > 0)
                        {
                            await DisposeFailedSessionsAsync(sessions, pollSummary.FailedSourceIds).ConfigureAwait(false);
                            connectedVersion = -1;
                        }

                        if (pollSummary.AnySuccess)
                        {
                            bridge_state_.SetBridgeState("Running");
                            bridge_state_.MarkUaWrite(pollSummary.TotalValueCount, cycleTimer.Elapsed);
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

    private async Task<SourcePollSummary> PollSourcesAsync(
        DaRuntimeSettingsSnapshot settings,
        Dictionary<string, SourceSession> sessions,
        SourceMappingCache sourceMappingCache,
        CancellationToken cancellationToken)
    {
        using SemaphoreSlim concurrencyGate = new(MaxConcurrentSourcePolls);
        List<Task<SourcePollResult>> pollTasks = new(settings.Sources.Count);

        for (int i = 0; i < settings.Sources.Count; i++)
        {
            DaSourceRuntimeSettings source = settings.Sources[i];
            if (!sessions.TryGetValue(source.SourceId, out SourceSession? session))
            {
                continue;
            }

            IReadOnlyList<TagMapping> sourceMappings = sourceMappingCache.GetMappings(source.SourceId);
            pollTasks.Add(PollSourceAsync(source, session, sourceMappings, concurrencyGate, cancellationToken));
        }

        SourcePollResult[] results = await Task.WhenAll(pollTasks).ConfigureAwait(false);
        return SourcePollSummary.FromResults(results);
    }

    private async Task<SourcePollResult> PollSourceAsync(
        DaSourceRuntimeSettings source,
        SourceSession session,
        IReadOnlyList<TagMapping> sourceMappings,
        SemaphoreSlim concurrencyGate,
        CancellationToken cancellationToken)
    {
        await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            bridge_state_.SetSourceConnectionState(source.SourceId, "Connected");
            Stopwatch readTimer = Stopwatch.StartNew();
            IReadOnlyList<BridgeValue> values = await Task
                .Run(async () => await session.Client.ReadAsync(sourceMappings, cancellationToken).ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);

            readTimer.Stop();
            bridge_state_.UpdateDaRead(source.SourceId, values, readTimer.Elapsed);
            for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
            {
                ua_server_.UpdateValue(values[valueIndex]);
            }

            return SourcePollResult.Success(source.SourceId, values.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            bridge_state_.SetSourceError(source.SourceId, exception);
            bridge_state_.ClearSourceValues(source.SourceId);
            logger_.LogWarning(exception, "Source {SourceId} read failed", source.SourceId);
            return SourcePollResult.Failure(source.SourceId);
        }
        finally
        {
            concurrencyGate.Release();
        }
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

    private static async Task DisposeFailedSessionsAsync(
        Dictionary<string, SourceSession> sessions,
        IReadOnlyList<string> failedSourceIds)
    {
        for (int i = 0; i < failedSourceIds.Count; i++)
        {
            if (!sessions.Remove(failedSourceIds[i], out SourceSession? session))
            {
                continue;
            }

            await session.Client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task DisposeSessionsAsync(Dictionary<string, SourceSession> sessions)
    {
        foreach (SourceSession session in sessions.Values)
        {
            await session.Client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class SourceMappingCache
    {
        private static readonly IReadOnlyList<TagMapping> EmptyMappings = Array.Empty<TagMapping>();
        private readonly Dictionary<string, IReadOnlyList<TagMapping>> mappings_by_source_;

        private SourceMappingCache(Dictionary<string, IReadOnlyList<TagMapping>> mappingsBySource)
        {
            mappings_by_source_ = mappingsBySource;
        }

        public static SourceMappingCache Build(IReadOnlyList<TagMapping> mappings)
        {
            Dictionary<string, List<TagMapping>> groupedMappings = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < mappings.Count; i++)
            {
                TagMapping mapping = mappings[i];
                if (!groupedMappings.TryGetValue(mapping.SourceId, out List<TagMapping>? sourceMappings))
                {
                    sourceMappings = new List<TagMapping>();
                    groupedMappings[mapping.SourceId] = sourceMappings;
                }

                sourceMappings.Add(mapping);
            }

            Dictionary<string, IReadOnlyList<TagMapping>> frozenMappings = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string sourceId, List<TagMapping> sourceMappings) in groupedMappings)
            {
                frozenMappings[sourceId] = sourceMappings.ToArray();
            }

            return new SourceMappingCache(frozenMappings);
        }

        public IReadOnlyList<TagMapping> GetMappings(string sourceId)
        {
            return mappings_by_source_.TryGetValue(sourceId, out IReadOnlyList<TagMapping>? mappings)
                ? mappings
                : EmptyMappings;
        }
    }

    private sealed record SourcePollResult(string SourceId, bool IsSuccess, int ValueCount)
    {
        public static SourcePollResult Success(string sourceId, int valueCount)
        {
            return new SourcePollResult(sourceId, true, valueCount);
        }

        public static SourcePollResult Failure(string sourceId)
        {
            return new SourcePollResult(sourceId, false, 0);
        }
    }

    private sealed record SourcePollSummary(int TotalValueCount, bool AnySuccess, IReadOnlyList<string> FailedSourceIds)
    {
        public static SourcePollSummary FromResults(IReadOnlyList<SourcePollResult> results)
        {
            int totalValueCount = 0;
            bool anySuccess = false;
            List<string> failedSourceIds = new();

            for (int i = 0; i < results.Count; i++)
            {
                SourcePollResult result = results[i];
                if (result.IsSuccess)
                {
                    totalValueCount += result.ValueCount;
                    anySuccess = true;
                }
                else
                {
                    failedSourceIds.Add(result.SourceId);
                }
            }

            return new SourcePollSummary(totalValueCount, anySuccess, failedSourceIds);
        }
    }

    private sealed record SourceSession(DaSourceRuntimeSettings Source, IDaClient Client);
}
