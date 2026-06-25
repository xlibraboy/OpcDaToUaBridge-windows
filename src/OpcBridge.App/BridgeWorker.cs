using System.Diagnostics;
using System.Globalization;
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
        IReadOnlyList<TagMapping> activeMappings = sourceMappingCache.GetActiveMappings();
        bridge_state_.Configure(settings.UpdateRateMs, activeMappings.Count, settings.Sources);

        try
        {
            logger_.LogInformation("Starting bridge with {MappingCount} mappings across {SourceCount} sources", activeMappings.Count, settings.Sources.Count);
            await ua_server_.StartAsync(activeMappings, stoppingToken).ConfigureAwait(false);

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
                            activeMappings = sourceMappingCache.GetActiveMappings();
                            ua_server_.SyncMappings(activeMappings);
                            bridge_state_.RetainMappedValues(activeMappings);
                            uaMappingVersion = mappingVersion;
                            connectedVersion = -1;
                            bridge_state_.UpdateSources(settings.UpdateRateMs, activeMappings.Count, settings.Sources);
                            logger_.LogInformation("Applied tag mapping change: {Count} mappings", activeMappings.Count);
                        }

                        if (connectedVersion != settings.Version)
                        {
                            await ReconfigureSessionsAsync(settings, sessions, stoppingToken).ConfigureAwait(false);
                            connectedVersion = settings.Version;
                            bridge_state_.UpdateSources(settings.UpdateRateMs, activeMappings.Count, settings.Sources);
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

            IReadOnlyList<TagMapping> sourceReadMappings = sourceMappingCache.GetSourceReadMappings(source.SourceId);
            IReadOnlyList<TagMapping> manualMappings = sourceMappingCache.GetManualMappings(source.SourceId);
            pollTasks.Add(PollSourceAsync(source, session, sourceReadMappings, manualMappings, concurrencyGate, cancellationToken));
        }

        SourcePollResult[] results = await Task.WhenAll(pollTasks).ConfigureAwait(false);
        return SourcePollSummary.FromResults(results);
    }

    private async Task<SourcePollResult> PollSourceAsync(
        DaSourceRuntimeSettings source,
        SourceSession session,
        IReadOnlyList<TagMapping> sourceReadMappings,
        IReadOnlyList<TagMapping> manualMappings,
        SemaphoreSlim concurrencyGate,
        CancellationToken cancellationToken)
    {
        await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        int outputValueCount = 0;
        bool sourceReadSucceeded = false;

        try
        {
            bridge_state_.SetSourceConnectionState(source.SourceId, "Connected");
            Stopwatch readTimer = Stopwatch.StartNew();
            IReadOnlyList<BridgeValue> values = await Task
                .Run(async () => await session.Client.ReadAsync(sourceReadMappings, cancellationToken).ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);

            readTimer.Stop();
            bridge_state_.UpdateDaRead(source.SourceId, values, readTimer.Elapsed);
            for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
            {
                BridgeValue value = values[valueIndex];
                bridge_state_.SetValue(value);
                ua_server_.UpdateValue(value);
                outputValueCount++;
            }

            sourceReadSucceeded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            bridge_state_.SetSourceError(source.SourceId, exception);
            ClearReadValues(sourceReadMappings);
            logger_.LogWarning(exception, "Source {SourceId} read failed", source.SourceId);
        }

        outputValueCount += ApplyManualMappings(manualMappings);

        return sourceReadSucceeded
            ? SourcePollResult.Success(source.SourceId, outputValueCount)
            : SourcePollResult.Failure(source.SourceId, outputValueCount);
    }

    private int ApplyManualMappings(IReadOnlyList<TagMapping> manualMappings)
    {
        int updatedCount = 0;

        for (int i = 0; i < manualMappings.Count; i++)
        {
            TagMapping mapping = manualMappings[i];
            if (!TryCreateManualValue(mapping, out BridgeValue manualValue))
            {
                bridge_state_.ClearValue(mapping.SourceId, mapping.DaItemId);
                continue;
            }

            bridge_state_.SetValue(manualValue);
            ua_server_.UpdateValue(manualValue);
            updatedCount++;
        }

        return updatedCount;
    }

    private void ClearReadValues(IReadOnlyList<TagMapping> readMappings)
    {
        for (int i = 0; i < readMappings.Count; i++)
        {
            TagMapping mapping = readMappings[i];
            bridge_state_.ClearValue(mapping.SourceId, mapping.DaItemId);
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

    private static bool TryCreateManualValue(TagMapping mapping, out BridgeValue value)
    {
        if (TryConvertManualValue(mapping.DataType, mapping.ManualValue, out object? convertedValue))
        {
            value = new BridgeValue(
                mapping.SourceId,
                mapping.DaItemId,
                convertedValue,
                DateTime.UtcNow,
                192,
                true);
            return true;
        }

        value = new BridgeValue(mapping.SourceId, mapping.DaItemId, null, DateTime.UtcNow, 0, false);
        return false;
    }

    private static bool TryConvertManualValue(string dataType, string? manualValue, out object? convertedValue)
    {
        string text = manualValue?.Trim() ?? string.Empty;
        string normalizedDataType = dataType.Trim().ToUpperInvariant();

        if (normalizedDataType is "STRING")
        {
            convertedValue = text;
            return true;
        }

        if (normalizedDataType is "BOOL" or "BOOLEAN")
        {
            if (bool.TryParse(text, out bool boolValue))
            {
                convertedValue = boolValue;
                return true;
            }

            if (text == "1")
            {
                convertedValue = true;
                return true;
            }

            if (text == "0")
            {
                convertedValue = false;
                return true;
            }

            convertedValue = null;
            return false;
        }

        if (normalizedDataType is "BYTE")
        {
            if (byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte byteValue))
            {
                convertedValue = byteValue;
                return true;
            }
        }
        else if (normalizedDataType is "SBYTE")
        {
            if (sbyte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte sbyteValue))
            {
                convertedValue = sbyteValue;
                return true;
            }
        }
        else if (normalizedDataType is "INT16" or "SHORT")
        {
            if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out short shortValue))
            {
                convertedValue = shortValue;
                return true;
            }
        }
        else if (normalizedDataType is "UINT16")
        {
            if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort ushortValue))
            {
                convertedValue = ushortValue;
                return true;
            }
        }
        else if (normalizedDataType is "INT32" or "INT")
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
            {
                convertedValue = intValue;
                return true;
            }
        }
        else if (normalizedDataType is "UINT32")
        {
            if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uintValue))
            {
                convertedValue = uintValue;
                return true;
            }
        }
        else if (normalizedDataType is "INT64" or "LONG")
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
            {
                convertedValue = longValue;
                return true;
            }
        }
        else if (normalizedDataType is "UINT64")
        {
            if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong ulongValue))
            {
                convertedValue = ulongValue;
                return true;
            }
        }
        else if (normalizedDataType is "FLOAT" or "SINGLE")
        {
            if (float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float floatValue))
            {
                convertedValue = floatValue;
                return true;
            }
        }
        else if (normalizedDataType is "DOUBLE" or "REAL8")
        {
            if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double doubleValue))
            {
                convertedValue = doubleValue;
                return true;
            }
        }
        else if (normalizedDataType is "DECIMAL")
        {
            if (decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out decimal decimalValue))
            {
                convertedValue = decimalValue;
                return true;
            }
        }
        else if (TryInferManualValue(text, out object? inferredValue))
        {
            convertedValue = inferredValue;
            return true;
        }

        convertedValue = null;
        return false;
    }

    private static bool TryInferManualValue(string text, out object? convertedValue)
    {
        if (bool.TryParse(text, out bool boolValue))
        {
            convertedValue = boolValue;
            return true;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
        {
            convertedValue = longValue;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double doubleValue))
        {
            convertedValue = doubleValue;
            return true;
        }

        convertedValue = text;
        return true;
    }

    private sealed class SourceMappingCache
    {
        private static readonly IReadOnlyList<TagMapping> EmptyMappings = Array.Empty<TagMapping>();
        private readonly Dictionary<string, SourceMappingSet> mappings_by_source_;
        private readonly IReadOnlyList<TagMapping> active_mappings_;

        private SourceMappingCache(Dictionary<string, SourceMappingSet> mappingsBySource, IReadOnlyList<TagMapping> activeMappings)
        {
            mappings_by_source_ = mappingsBySource;
            active_mappings_ = activeMappings;
        }

        public static SourceMappingCache Build(IReadOnlyList<TagMapping> mappings)
        {
            Dictionary<string, List<TagMapping>> groupedMappings = new(StringComparer.OrdinalIgnoreCase);
            List<TagMapping> activeMappings = new(mappings.Count);

            for (int i = 0; i < mappings.Count; i++)
            {
                TagMapping mapping = mappings[i];
                if (!groupedMappings.TryGetValue(mapping.SourceId, out List<TagMapping>? sourceMappings))
                {
                    sourceMappings = new List<TagMapping>();
                    groupedMappings[mapping.SourceId] = sourceMappings;
                }

                sourceMappings.Add(mapping);
                if (mapping.Enabled)
                {
                    activeMappings.Add(mapping);
                }
            }

            Dictionary<string, SourceMappingSet> frozenMappings = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string sourceId, List<TagMapping> sourceMappings) in groupedMappings)
            {
                TagMapping[] all = sourceMappings.ToArray();
                TagMapping[] active = sourceMappings.Where(mapping => mapping.Enabled).ToArray();
                TagMapping[] sourceRead = active.Where(mapping => string.Equals(mapping.Mode, TagMode.Source, StringComparison.OrdinalIgnoreCase)).ToArray();
                TagMapping[] manual = active.Where(mapping => string.Equals(mapping.Mode, TagMode.Manual, StringComparison.OrdinalIgnoreCase)).ToArray();
                frozenMappings[sourceId] = new SourceMappingSet(all, active, sourceRead, manual);
            }

            return new SourceMappingCache(frozenMappings, activeMappings.ToArray());
        }

        public IReadOnlyList<TagMapping> GetActiveMappings()
        {
            return active_mappings_;
        }

        public IReadOnlyList<TagMapping> GetMappings(string sourceId)
        {
            return mappings_by_source_.TryGetValue(sourceId, out SourceMappingSet? mappings)
                ? mappings.All
                : EmptyMappings;
        }

        public IReadOnlyList<TagMapping> GetSourceReadMappings(string sourceId)
        {
            return mappings_by_source_.TryGetValue(sourceId, out SourceMappingSet? mappings)
                ? mappings.SourceRead
                : EmptyMappings;
        }

        public IReadOnlyList<TagMapping> GetManualMappings(string sourceId)
        {
            return mappings_by_source_.TryGetValue(sourceId, out SourceMappingSet? mappings)
                ? mappings.Manual
                : EmptyMappings;
        }
    }

    private sealed record SourceMappingSet(
        IReadOnlyList<TagMapping> All,
        IReadOnlyList<TagMapping> Active,
        IReadOnlyList<TagMapping> SourceRead,
        IReadOnlyList<TagMapping> Manual);

    private sealed record SourcePollResult(string SourceId, bool ReadSucceeded, int OutputValueCount)
    {
        public static SourcePollResult Success(string sourceId, int outputValueCount)
        {
            return new SourcePollResult(sourceId, true, outputValueCount);
        }

        public static SourcePollResult Failure(string sourceId, int outputValueCount)
        {
            return new SourcePollResult(sourceId, false, outputValueCount);
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
                totalValueCount += result.OutputValueCount;
                anySuccess |= result.ReadSucceeded || result.OutputValueCount > 0;

                if (!result.ReadSucceeded)
                {
                    failedSourceIds.Add(result.SourceId);
                }
            }

            return new SourcePollSummary(totalValueCount, anySuccess, failedSourceIds);
        }
    }



    private sealed record SourceSession(DaSourceRuntimeSettings Source, IDaClient Client);
}
