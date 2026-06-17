using Opc.Ua;
using Opc.Ua.Server;
using OpcBridge.Core;

namespace OpcBridge.Ua;

internal sealed class BridgeUaServer : StandardServer
{
    private readonly IReadOnlyList<TagMapping> mappings_;
    private BridgeNodeManager? node_manager_;

    public BridgeUaServer(IReadOnlyList<TagMapping> mappings)
    {
        mappings_ = mappings;
    }

    public void UpdateValue(BridgeValue value)
    {
        node_manager_?.UpdateValue(value);
    }

    public int GetConnectedSessionCount()
    {
        return ServerInternal.SessionManager
            .GetSessions()
            .Cast<ISession>()
            .Count(session => session.Activated && !session.HasExpired);
    }

    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration)
    {
        node_manager_ = new BridgeNodeManager(server, configuration, mappings_);
        return new MasterNodeManager(server, configuration, null, new INodeManager[] { node_manager_ });
    }

    protected override ServerProperties LoadServerProperties()
    {
        return new ServerProperties
        {
            ManufacturerName = "Oh My Pi",
            ProductName = "OPC DA to OPC UA Bridge",
            ProductUri = "urn:ohmypi:opc-da-to-ua-bridge",
            SoftwareVersion = typeof(BridgeUaServer).Assembly.GetName().Version?.ToString() ?? "0.1.0",
            BuildNumber = "0",
            BuildDate = DateTime.UtcNow
        };
    }
}
