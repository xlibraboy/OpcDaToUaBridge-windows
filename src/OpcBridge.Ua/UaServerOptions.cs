namespace OpcBridge.Ua;

public sealed class UaServerOptions
{
    public string ApplicationName { get; set; } = "OpcDaToUaBridge";
    public string EndpointUrl { get; set; } = "opc.tcp://0.0.0.0:4840/OpcDaToUaBridge";
    public bool AutoAcceptUntrustedCertificates { get; set; } = true;
}
