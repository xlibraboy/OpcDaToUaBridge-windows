using OpcBridge.Da;

namespace OpcBridge.App;

public sealed class DaClientFactory
{
    public IDaClient Create(DaRuntimeSettingsSnapshot settings, DaSourceRuntimeSettings source)
    {
        return new OpcDaClient(source.ToOptions(settings.UpdateRateMs));
    }
}