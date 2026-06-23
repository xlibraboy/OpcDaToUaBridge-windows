using Microsoft.Extensions.Options;
using OpcBridge.Da;

namespace OpcBridge.App;

public sealed class DaRuntimeSettings
{
    public const string DefaultSourceId = "default";

    private readonly object sync_ = new();
    private DaRuntimeSettingsSnapshot snapshot_;

    public DaRuntimeSettings(IOptions<DaClientOptions> options)
    {
        snapshot_ = new DaRuntimeSettingsSnapshot(
            NormalizeUpdateRate(options.Value.UpdateRateMs),
            BuildInitialSources(options.Value),
            0);
    }

    public DaRuntimeSettingsSnapshot GetSnapshot()
    {
        lock (sync_)
        {
            return snapshot_;
        }
    }

    public DaRuntimeSettingsSnapshot UpsertSource(DaSourceRuntimeSettings source)
    {
        DaSourceRuntimeSettings normalized = NormalizeSource(source);

        lock (sync_)
        {
            List<DaSourceRuntimeSettings> sources = snapshot_.Sources.ToList();
            int index = sources.FindIndex(existing =>
                string.Equals(existing.SourceId, normalized.SourceId, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                sources[index] = normalized;
            }
            else
            {
                sources.Add(normalized);
            }

            snapshot_ = snapshot_ with
            {
                Sources = sources,
                Version = snapshot_.Version + 1
            };

            return snapshot_;
        }
    }

    public bool TryRemoveSource(string sourceId, out DaRuntimeSettingsSnapshot snapshot)
    {
        string normalizedSourceId = NormalizeSourceId(sourceId);

        lock (sync_)
        {
            if (snapshot_.Sources.Count <= 1)
            {
                snapshot = snapshot_;
                return false;
            }

            List<DaSourceRuntimeSettings> sources = snapshot_.Sources
                .Where(source => !string.Equals(source.SourceId, normalizedSourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (sources.Count == snapshot_.Sources.Count)
            {
                snapshot = snapshot_;
                return false;
            }

            snapshot_ = snapshot_ with
            {
                Sources = sources,
                Version = snapshot_.Version + 1
            };

            snapshot = snapshot_;
            return true;
        }
    }

    public DaRuntimeSettingsSnapshot SetServerConfig(
        string progId,
        string host,
        string? username = null,
        string? password = null,
        string? domain = null)
    {
        return UpsertSource(new DaSourceRuntimeSettings(
            DefaultSourceId,
            "Default Source",
            progId,
            host,
            username,
            password,
            domain));
    }

    private static IReadOnlyList<DaSourceRuntimeSettings> BuildInitialSources(DaClientOptions options)
    {
        List<DaSourceRuntimeSettings> configuredSources = new();

        if (options.Sources is { Count: > 0 })
        {
            foreach (DaSourceOptions source in options.Sources)
            {
                configuredSources.Add(NormalizeSource(new DaSourceRuntimeSettings(
                    source.SourceId,
                    source.DisplayName,
                    source.ProgId,
                    source.Host,
                    source.RemoteUsername,
                    source.RemotePassword,
                    source.RemoteDomain)));
            }
        }
        else
        {
            configuredSources.Add(NormalizeSource(new DaSourceRuntimeSettings(
                string.IsNullOrWhiteSpace(options.SourceId) ? DefaultSourceId : options.SourceId,
                string.IsNullOrWhiteSpace(options.DisplayName) ? "Default Source" : options.DisplayName,
                options.ProgId,
                options.Host,
                options.RemoteUsername,
                options.RemotePassword,
                options.RemoteDomain)));
        }

        List<DaSourceRuntimeSettings> dedupedSources = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < configuredSources.Count; i++)
        {
            DaSourceRuntimeSettings source = configuredSources[i];
            if (seen.Add(source.SourceId))
            {
                dedupedSources.Add(source);
            }
        }

        if (dedupedSources.Count == 0)
        {
            dedupedSources.Add(new DaSourceRuntimeSettings(DefaultSourceId, "Default Source", string.Empty, "localhost", null, null, null));
        }

        return dedupedSources;
    }

    private static DaSourceRuntimeSettings NormalizeSource(DaSourceRuntimeSettings source)
    {
        string sourceId = NormalizeSourceId(source.SourceId);
        string displayName = string.IsNullOrWhiteSpace(source.DisplayName) ? sourceId : source.DisplayName.Trim();

        return new DaSourceRuntimeSettings(
            sourceId,
            displayName,
            source.ProgId?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(source.Host) ? "localhost" : source.Host.Trim(),
            string.IsNullOrWhiteSpace(source.RemoteUsername) ? null : source.RemoteUsername.Trim(),
            string.IsNullOrWhiteSpace(source.RemotePassword) ? null : source.RemotePassword,
            string.IsNullOrWhiteSpace(source.RemoteDomain) ? null : source.RemoteDomain.Trim());
    }

    private static string NormalizeSourceId(string? sourceId)
    {
        string value = sourceId?.Trim() ?? string.Empty;
        return value.Length == 0 ? DefaultSourceId : value;
    }

    private static int NormalizeUpdateRate(int updateRateMs)
    {
        return updateRateMs <= 0 ? 1000 : updateRateMs;
    }


}

public sealed record DaRuntimeSettingsSnapshot(
    int UpdateRateMs,
    IReadOnlyList<DaSourceRuntimeSettings> Sources,
    long Version)
{
    public DaSourceRuntimeSettings? GetSource(string? sourceId)
    {
        string normalizedSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? DaRuntimeSettings.DefaultSourceId
            : sourceId.Trim();

        return Sources.FirstOrDefault(source =>
            string.Equals(source.SourceId, normalizedSourceId, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record DaSourceRuntimeSettings(
    string SourceId,
    string DisplayName,
    string ProgId,
    string Host,
    string? RemoteUsername,
    string? RemotePassword,
    string? RemoteDomain)
{
    public DaClientOptions ToOptions(int updateRateMs)
    {
        return new DaClientOptions
        {
            SourceId = SourceId,
            DisplayName = DisplayName,
            ProgId = ProgId,
            Host = Host,
            UpdateRateMs = updateRateMs,
            RemoteUsername = RemoteUsername,
            RemotePassword = RemotePassword,
            RemoteDomain = RemoteDomain
        };
    }
}