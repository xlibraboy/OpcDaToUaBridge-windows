using OpcBridge.Da;

namespace OpcBridge.App;

public sealed class DaClientFactory
{
    public IDaClient Create(DaRuntimeSettingsSnapshot settings)
    {
        return string.Equals(settings.Mode, "OpcDa", StringComparison.OrdinalIgnoreCase)
            ? new OpcDaClient(settings.ToOptions())
            : new SimulatedDaClient();
    }
}
