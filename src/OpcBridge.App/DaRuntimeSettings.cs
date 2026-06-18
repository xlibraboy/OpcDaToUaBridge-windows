using Microsoft.Extensions.Options;
using OpcBridge.Da;

namespace OpcBridge.App;

public sealed class DaRuntimeSettings
{
    private readonly object sync_ = new();
    private DaRuntimeSettingsSnapshot snapshot_;

    public DaRuntimeSettings(IOptions<DaClientOptions> options)
    {
        snapshot_ = new DaRuntimeSettingsSnapshot(
            NormalizeMode(options.Value.Mode),
            options.Value.ProgId,
            options.Value.Host,
            options.Value.UpdateRateMs,
            options.Value.RemoteUsername,
            options.Value.RemotePassword,
            options.Value.RemoteDomain,
            0);
    }

    public DaRuntimeSettingsSnapshot GetSnapshot()
    {
        lock (sync_)
        {
            return snapshot_;
        }
    }

    public DaRuntimeSettingsSnapshot SetMode(string? mode)
    {
        string normalizedMode = NormalizeMode(mode);

        lock (sync_)
        {
            if (string.Equals(snapshot_.Mode, normalizedMode, StringComparison.OrdinalIgnoreCase))
            {
                return snapshot_;
            }

            snapshot_ = snapshot_ with
            {
                Mode = normalizedMode,
                Version = snapshot_.Version + 1
            };

            return snapshot_;
        }
    }

    public DaRuntimeSettingsSnapshot SetServerConfig(string progId, string host,
        string? username = null, string? password = null, string? domain = null)
    {
        lock (sync_)
        {
            snapshot_ = snapshot_ with
            {
                ProgId = progId?.Trim() ?? string.Empty,
                Host = host?.Trim() ?? "localhost",
                RemoteUsername = string.IsNullOrWhiteSpace(username) ? null : username.Trim(),
                RemotePassword = string.IsNullOrWhiteSpace(password) ? null : password,
                RemoteDomain   = string.IsNullOrWhiteSpace(domain)   ? null : domain.Trim(),
                Version = snapshot_.Version + 1
            };
            return snapshot_;
        }
    }

    private static string NormalizeMode(string? mode)
    {
        string value = mode?.Trim() ?? string.Empty;

        if (string.Equals(value, "Simulation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Simulated", StringComparison.OrdinalIgnoreCase))
        {
            return "Simulation";
        }

        if (string.Equals(value, "OpcDa", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Real", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "RealOpcDa", StringComparison.OrdinalIgnoreCase))
        {
            return "OpcDa";
        }

        throw new ArgumentException("Mode must be Simulation or OpcDa.", nameof(mode));
    }
}

public sealed record DaRuntimeSettingsSnapshot(
    string Mode,
    string ProgId,
    string Host,
    int UpdateRateMs,
    string? RemoteUsername,
    string? RemotePassword,
    string? RemoteDomain,
    long Version)
{
    public DaClientOptions ToOptions()
    {
        return new DaClientOptions
        {
            Mode = Mode,
            ProgId = ProgId,
            Host = Host,
            UpdateRateMs = UpdateRateMs,
            RemoteUsername = RemoteUsername,
            RemotePassword = RemotePassword,
            RemoteDomain   = RemoteDomain
        };
    }
}
