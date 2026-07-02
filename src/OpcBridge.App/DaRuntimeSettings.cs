using System.Text.Json;
using Microsoft.Extensions.Options;
using OpcBridge.Da;

namespace OpcBridge.App;

public sealed class DaRuntimeSettings
{
    public const string DefaultSourceId = "default";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object sync_ = new();
    private readonly string persist_path_;
    private DaRuntimeSettingsSnapshot snapshot_;

    public DaRuntimeSettings(IOptions<DaClientOptions> options)
    {
        persist_path_ = Path.Combine(AppContext.BaseDirectory, "sources.json");

        // Load from sources.json if it exists; otherwise seed from appsettings.json.
        DaRuntimeSettingsSnapshot? loaded = LoadFromDisk();
        if (loaded is not null)
        {
            snapshot_ = loaded;
        }
        else
        {
            snapshot_ = new DaRuntimeSettingsSnapshot(
                NormalizeUpdateRate(options.Value.UpdateRateMs),
                BuildInitialSources(options.Value),
                0);
        }
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
        DaSourceRuntimeSettings normalized = NormalizeSource(source, snapshot_.UpdateRateMs);

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

            Persist();
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

            Persist();
            snapshot = snapshot_;
            return true;
        }
    }
    public DaRuntimeSettingsSnapshot SetUpdateRate(int updateRateMs)
    {
        int normalizedUpdateRate = NormalizeUpdateRate(updateRateMs);

        lock (sync_)
        {
            snapshot_ = snapshot_ with
            {
                UpdateRateMs = normalizedUpdateRate,
                Version = snapshot_.Version + 1
            };

            Persist();
            return snapshot_;
        }
    }

    public DaRuntimeSettingsSnapshot SetSourceUpdateRate(string sourceId, int updateRateMs)
    {
        int normalizedRate = NormalizeUpdateRate(updateRateMs);

        lock (sync_)
        {
            List<DaSourceRuntimeSettings> sources = snapshot_.Sources.ToList();
            int index = sources.FindIndex(source =>
                string.Equals(source.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                return snapshot_;
            }

            sources[index] = sources[index] with { UpdateRateMs = normalizedRate };
            snapshot_ = snapshot_ with
            {
                Sources = sources,
                Version = snapshot_.Version + 1
            };

            Persist();
            return snapshot_;
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
            domain,
            0));
    }

    private static IReadOnlyList<DaSourceRuntimeSettings> BuildInitialSources(DaClientOptions options)
    {
        int defaultRate = NormalizeUpdateRate(options.UpdateRateMs);
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
                    source.RemoteDomain,
                    source.UpdateRateMs), defaultRate));
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
                options.RemoteDomain,
                options.UpdateRateMs), defaultRate));
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
            dedupedSources.Add(new DaSourceRuntimeSettings(DefaultSourceId, "Default Source", string.Empty, "localhost", null, null, null, NormalizeUpdateRate(0)));
        }

        return dedupedSources;
    }

    private static DaSourceRuntimeSettings NormalizeSource(DaSourceRuntimeSettings source, int defaultUpdateRate)
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
            string.IsNullOrWhiteSpace(source.RemoteDomain) ? null : source.RemoteDomain.Trim(),
            NormalizeUpdateRate(source.UpdateRateMs <= 0 ? defaultUpdateRate : source.UpdateRateMs));
    }

    private static string NormalizeSourceId(string? sourceId)
    {
        string value = sourceId?.Trim() ?? string.Empty;
        return value.Length == 0 ? DefaultSourceId : value;
    }

    private static int NormalizeUpdateRate(int updateRateMs)
    {
        if (updateRateMs <= 0)
        {
            return 1000;
        }

        return Math.Max(100, updateRateMs);
    }

    private void Persist()
    {
        try
        {
            lock (sync_)
            {
                var dto = new SourcesConfigDto
                {
                    UpdateRateMs = snapshot_.UpdateRateMs,
                    Sources = snapshot_.Sources
                        .Select(s => new SourceConfigDto
                        {
                            SourceId = s.SourceId,
                            DisplayName = s.DisplayName,
                            ProgId = s.ProgId,
                            Host = s.Host,
                            RemoteUsername = s.RemoteUsername,
                            RemotePassword = s.RemotePassword,
                            RemoteDomain = s.RemoteDomain,
                            UpdateRateMs = s.UpdateRateMs
                        })
                        .ToList()
                };
                string json = JsonSerializer.Serialize(dto, JsonOptions);
                File.WriteAllText(persist_path_, json);
            }
        }
        catch
        {
        }
    }

    private DaRuntimeSettingsSnapshot? LoadFromDisk()
    {
        try
        {
            if (!File.Exists(persist_path_)) return null;
            string json = File.ReadAllText(persist_path_);
            SourcesConfigDto? dto = JsonSerializer.Deserialize<SourcesConfigDto>(json);
            if (dto is null) return null;

            List<DaSourceRuntimeSettings> sources = dto.Sources?
                .Select(s => new DaSourceRuntimeSettings(
                    s.SourceId ?? DefaultSourceId,
                    s.DisplayName ?? string.Empty,
                    s.ProgId ?? string.Empty,
                    string.IsNullOrWhiteSpace(s.Host) ? "localhost" : s.Host,
                    string.IsNullOrWhiteSpace(s.RemoteUsername) ? null : s.RemoteUsername,
                    string.IsNullOrWhiteSpace(s.RemotePassword) ? null : s.RemotePassword,
                    string.IsNullOrWhiteSpace(s.RemoteDomain) ? null : s.RemoteDomain,
                    s.UpdateRateMs <= 0 ? dto.UpdateRateMs : s.UpdateRateMs))
                .ToList() ?? new List<DaSourceRuntimeSettings>();

            if (sources.Count == 0) return null;

            return new DaRuntimeSettingsSnapshot(dto.UpdateRateMs, sources, 0);
        }
        catch
        {
            return null;
        }
    }

    public void RestoreFromSnapshot(DaRuntimeSettingsSnapshot snapshot)
    {
        lock (sync_)
        {
            snapshot_ = snapshot with { Version = snapshot_.Version + 1 };
            Persist();
        }
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
    string? RemoteDomain,
    int UpdateRateMs)
{
    public DaClientOptions ToOptions()
    {
        return new DaClientOptions
        {
            SourceId = SourceId,
            DisplayName = DisplayName,
            ProgId = ProgId,
            Host = Host,
            UpdateRateMs = UpdateRateMs,
            RemoteUsername = RemoteUsername,
            RemotePassword = RemotePassword,
            RemoteDomain = RemoteDomain
        };
    }
}

internal sealed class SourcesConfigDto
{
    public int UpdateRateMs { get; set; } = 1000;
    public List<SourceConfigDto> Sources { get; set; } = new();
}

internal sealed class SourceConfigDto
{
    public string? SourceId { get; set; }
    public string? DisplayName { get; set; }
    public string? ProgId { get; set; }
    public string? Host { get; set; }
    public string? RemoteUsername { get; set; }
    public string? RemotePassword { get; set; }
    public string? RemoteDomain { get; set; }
    public int UpdateRateMs { get; set; }
}