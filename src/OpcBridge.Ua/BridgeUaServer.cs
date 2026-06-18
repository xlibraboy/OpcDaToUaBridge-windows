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

    /// <summary>Diffs the running address space against the desired mappings, adding/removing nodes live.</summary>
    public void SyncMappings(IReadOnlyList<TagMapping> mappings)
    {
        if (node_manager_ is null)
        {
            return;
        }

        var desired = new HashSet<string>(mappings.Select(m => m.DaItemId), StringComparer.OrdinalIgnoreCase);
        var current = new HashSet<string>(node_manager_.GetMappedItemIds(), StringComparer.OrdinalIgnoreCase);

        foreach (string itemId in current)
        {
            if (!desired.Contains(itemId))
            {
                node_manager_.RemoveMapping(itemId);
            }
        }

        foreach (TagMapping mapping in mappings)
        {
            if (!current.Contains(mapping.DaItemId))
            {
                node_manager_.AddMapping(mapping);
            }
        }
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
