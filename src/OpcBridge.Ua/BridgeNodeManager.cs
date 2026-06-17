using Opc.Ua;
using Opc.Ua.Server;
using OpcBridge.Core;

namespace OpcBridge.Ua;

internal sealed class BridgeNodeManager : CustomNodeManager2
{
    private const string NamespaceUri = "urn:ohmypi:opc-da-to-ua-bridge:tags";
    private readonly IReadOnlyList<TagMapping> mappings_;
    private readonly Dictionary<string, BaseDataVariableState> variables_by_da_item_ = new(StringComparer.OrdinalIgnoreCase);
    private ushort namespace_index_;

    public BridgeNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration,
        IReadOnlyList<TagMapping> mappings)
        : base(server, configuration, NamespaceUri)
    {
        mappings_ = mappings;
        SystemContext.NodeIdFactory = this;
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            namespace_index_ = Server.NamespaceUris.GetIndexOrAppend(NamespaceUri);

            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                references = new List<IReference>();
                externalReferences[ObjectIds.ObjectsFolder] = references;
            }

            FolderState root = CreateFolder(null, "OpcDaTags", "OPC DA Tags");
            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));
            AddPredefinedNode(SystemContext, root);

            for (int i = 0; i < mappings_.Count; i++)
            {
                TagMapping mapping = mappings_[i];
                BaseDataVariableState variable = CreateVariable(root, mapping);
                variables_by_da_item_[mapping.DaItemId] = variable;
                AddPredefinedNode(SystemContext, variable);
            }
        }
    }

    public void UpdateValue(BridgeValue value)
    {
        lock (Lock)
        {
            if (!variables_by_da_item_.TryGetValue(value.DaItemId, out BaseDataVariableState? variable))
            {
                return;
            }

            variable.Value = value.Value;
            variable.Timestamp = value.TimestampUtc;
            variable.StatusCode = value.IsGood ? StatusCodes.Good : StatusCodes.Bad;
            variable.ClearChangeMasks(SystemContext, false);
        }
    }

    private FolderState CreateFolder(NodeState? parent, string path, string name)
    {
        FolderState folder = new(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            TypeDefinitionId = ObjectTypeIds.FolderType,
            NodeId = new NodeId(path, namespace_index_),
            BrowseName = new QualifiedName(name, namespace_index_),
            DisplayName = new LocalizedText(name),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            EventNotifier = EventNotifiers.None
        };

        parent?.AddChild(folder);
        return folder;
    }

    private BaseDataVariableState CreateVariable(FolderState parent, TagMapping mapping)
    {
        NodeId dataType = ToDataTypeId(mapping.DataType);
        BaseDataVariableState variable = new(parent)
        {
            SymbolicName = mapping.DisplayName,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            NodeId = new NodeId(ToNodeIdentifier(mapping), namespace_index_),
            BrowseName = new QualifiedName(mapping.DisplayName, namespace_index_),
            DisplayName = new LocalizedText(mapping.DisplayName),
            Description = new LocalizedText(mapping.DaItemId),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            DataType = dataType,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            Historizing = false,
            Value = CreateInitialValue(mapping.DataType),
            StatusCode = StatusCodes.BadWaitingForInitialData,
            Timestamp = DateTime.UtcNow
        };

        parent.AddChild(variable);
        return variable;
    }

    private static string ToNodeIdentifier(TagMapping mapping)
    {
        string nodeId = mapping.UaNodeId.Trim();
        if (nodeId.Length == 0)
        {
            return mapping.DaItemId;
        }

        int stringMarker = nodeId.IndexOf(";s=", StringComparison.OrdinalIgnoreCase);
        if (stringMarker >= 0)
        {
            return nodeId[(stringMarker + 3)..];
        }

        return nodeId;
    }

    private static NodeId ToDataTypeId(string dataType)
    {
        return dataType.Trim().ToUpperInvariant() switch
        {
            "BOOL" or "BOOLEAN" => DataTypeIds.Boolean,
            "BYTE" => DataTypeIds.Byte,
            "INT16" or "SHORT" => DataTypeIds.Int16,
            "INT32" or "INT" => DataTypeIds.Int32,
            "INT64" or "LONG" => DataTypeIds.Int64,
            "FLOAT" or "SINGLE" => DataTypeIds.Float,
            "DOUBLE" or "REAL8" => DataTypeIds.Double,
            "STRING" => DataTypeIds.String,
            _ => DataTypeIds.BaseDataType
        };
    }

    private static object CreateInitialValue(string dataType)
    {
        return dataType.Trim().ToUpperInvariant() switch
        {
            "BOOL" or "BOOLEAN" => false,
            "BYTE" => (byte)0,
            "INT16" or "SHORT" => (short)0,
            "INT32" or "INT" => 0,
            "INT64" or "LONG" => 0L,
            "FLOAT" or "SINGLE" => 0.0f,
            "DOUBLE" or "REAL8" => 0.0,
            "STRING" => string.Empty,
            _ => 0.0
        };
    }
}
