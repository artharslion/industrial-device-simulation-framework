using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace IndustrialSim.Protocols.OpcUa;

internal sealed class IndustrialOpcUaServer : StandardServer
{
    private readonly IDeviceRuntime _runtime;

    public IndustrialOpcUaServer(IDeviceRuntime runtime) => _runtime = runtime;

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        return new MasterNodeManager(server, configuration, null, new INodeManager[]
        {
            new IndustrialNodeManager(server, configuration, _runtime)
        });
    }
}

internal sealed class IndustrialNodeManager : CustomNodeManager2
{
    private const string NamespaceUri = "urn:industrial-sim:runtime";
    private readonly IDeviceRuntime _runtime;
    private readonly Dictionary<string, BaseDataVariableState> _variables = new(StringComparer.OrdinalIgnoreCase);

    public IndustrialNodeManager(IServerInternal server, ApplicationConfiguration configuration, IDeviceRuntime runtime)
        : base(server, configuration, NamespaceUri)
    {
        _runtime = runtime;
        _runtime.State.DataPointChanged += OnDataPointChanged;
    }

    public new void Dispose()
    {
        _runtime.State.DataPointChanged -= OnDataPointChanged;
        base.Dispose();
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        var context = SystemContext;
        var device = new FolderState(null)
        {
            NodeId = new NodeId(_runtime.Definition.Id.Value, NamespaceIndex),
            BrowseName = new QualifiedName(_runtime.Definition.Id.Value, NamespaceIndex),
            DisplayName = _runtime.Definition.Id.Value,
            TypeDefinitionId = ObjectTypeIds.FolderType
        };
        device.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
        AddRootReference(externalReferences, device);
        AddPredefinedNode(context, device);

        foreach (var point in _runtime.Definition.DataPoints)
        {
            var variable = new BaseDataVariableState(device)
            {
                NodeId = new NodeId($"{_runtime.Definition.Id.Value}/{point.Name}", NamespaceIndex),
                BrowseName = new QualifiedName(point.Name, NamespaceIndex),
                DisplayName = point.Name,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = ToOpcUaDataType(point.DataType),
                ValueRank = ValueRanks.Scalar,
                AccessLevel = ToAccessLevel(point.Access),
                UserAccessLevel = ToAccessLevel(point.Access),
                Value = _runtime.Read(point.Name)?.Value,
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow,
                OnReadValue = ReadValue,
                OnWriteValue = WriteValue
            };
            _variables.Add(point.Name, variable);
            device.AddChild(variable);
            AddPredefinedNode(context, variable);
        }

        foreach (var command in _runtime.Definition.Commands)
        {
            var method = new MethodState(device)
            {
                NodeId = new NodeId($"{_runtime.Definition.Id.Value}/{command.Name}", NamespaceIndex),
                BrowseName = new QualifiedName(command.Name, NamespaceIndex),
                DisplayName = command.Name,
                Executable = true,
                UserExecutable = true,
                OnCallMethod = (_, _, _, _) =>
                {
                    _runtime.InvokeCommandAsync(command.Name).GetAwaiter().GetResult();
                    return ServiceResult.Good;
                }
            };
            device.AddChild(method);
            AddPredefinedNode(context, method);
        }
    }

    private void AddRootReference(IDictionary<NodeId, IList<IReference>> externalReferences, NodeState node)
    {
        if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
        {
            references = new List<IReference>();
            externalReferences[ObjectIds.ObjectsFolder] = references;
        }
        references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, node.NodeId));
    }

    private ServiceResult ReadValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
    {
        var point = NodeName(node);
        value = _runtime.Read(point)?.Value!;
        statusCode = StatusCodes.Good; timestamp = DateTime.UtcNow;
        return ServiceResult.Good;
    }

    private ServiceResult WriteValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
    {
        var result = _runtime.Write(NodeName(node), value);
        return result.Succeeded ? ServiceResult.Good : StatusCodes.BadNotWritable;
    }

    private void OnDataPointChanged(DataPointChanged change)
    {
        lock (Lock)
        {
            if (!_variables.TryGetValue(change.DataPointId.Value, out var variable)) return;
            variable.Value = change.NewValue.Value;
            variable.Timestamp = DateTime.UtcNow;
            variable.ClearChangeMasks(SystemContext, false);
        }
    }

    private static string NodeName(NodeState node) => ((string)node.NodeId.Identifier).Split('/').Last();
    private static byte ToAccessLevel(DataPointAccess access) => access switch
    {
        DataPointAccess.Read => AccessLevels.CurrentRead,
        DataPointAccess.Write => AccessLevels.CurrentWrite,
        DataPointAccess.ReadWrite => AccessLevels.CurrentReadOrWrite,
        _ => AccessLevels.None
    };
    private static NodeId ToOpcUaDataType(DataType type) => type switch
    {
        DataType.Boolean => DataTypeIds.Boolean,
        DataType.Int16 => DataTypeIds.Int16,
        DataType.Int32 => DataTypeIds.Int32,
        DataType.Int64 => DataTypeIds.Int64,
        DataType.UInt16 => DataTypeIds.UInt16,
        DataType.UInt32 => DataTypeIds.UInt32,
        DataType.UInt64 => DataTypeIds.UInt64,
        DataType.Float => DataTypeIds.Float,
        DataType.Double => DataTypeIds.Double,
        DataType.String => DataTypeIds.String,
        _ => DataTypeIds.BaseDataType
    };
}
