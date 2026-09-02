using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace IndustrialSim.Protocols.OpcUa;

internal sealed class IndustrialOpcUaServer : StandardServer
{
    private readonly IDeviceRuntime _runtime;
    private readonly OpcUaTransportFaultController _transportFault;

    public IndustrialOpcUaServer(IDeviceRuntime runtime, OpcUaTransportFaultController transportFault)
    {
        _runtime = runtime;
        _transportFault = transportFault;
    }

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        return new MasterNodeManager(server, configuration, null, new INodeManager[]
        {
            new IndustrialNodeManager(server, configuration, _runtime, _transportFault)
        });
    }
}

internal sealed class IndustrialNodeManager : CustomNodeManager2
{
    private const string NamespaceUri = "urn:industrial-sim:runtime";
    private readonly IDeviceRuntime _runtime;
    private readonly OpcUaTransportFaultController _transportFault;
    private readonly Dictionary<string, BaseDataVariableState> _variables = new(StringComparer.OrdinalIgnoreCase);
    private FolderState? _device;

    public IndustrialNodeManager(IServerInternal server, ApplicationConfiguration configuration, IDeviceRuntime runtime, OpcUaTransportFaultController transportFault)
        : base(server, configuration, NamespaceUri)
    {
        _runtime = runtime;
        _transportFault = transportFault;
        _runtime.RuntimeEventPublished += OnRuntimeEvent;
    }

    public new void Dispose()
    {
        _runtime.RuntimeEventPublished -= OnRuntimeEvent;
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
            TypeDefinitionId = ObjectTypeIds.FolderType,
            EventNotifier = EventNotifiers.SubscribeToEvents
        };
        _device = device;
        device.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
        device.AddReference(ReferenceTypeIds.HasNotifier, true, ObjectIds.ObjectsFolder);
        AddRootReference(externalReferences, device, ReferenceTypeIds.Organizes);
        AddRootReference(externalReferences, device, ReferenceTypeIds.HasNotifier);
        AddPredefinedNode(context, device);
        AddRootNotifier(device);

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
                    var transport = _transportFault.BeforeService();
                    if (StatusCode.IsBad(transport.StatusCode)) return transport;
                    _runtime.InvokeCommandAsync(command.Name).GetAwaiter().GetResult();
                    return ServiceResult.Good;
                }
            };
            device.AddChild(method);
            AddPredefinedNode(context, method);
        }
    }

    private void AddRootReference(IDictionary<NodeId, IList<IReference>> externalReferences, NodeState node, NodeId referenceType)
    {
        if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
        {
            references = new List<IReference>();
            externalReferences[ObjectIds.ObjectsFolder] = references;
        }
        references.Add(new NodeStateReference(referenceType, false, node.NodeId));
    }

    private ServiceResult ReadValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
    {
        var transport = _transportFault.BeforeService();
        if (StatusCode.IsBad(transport.StatusCode)) return transport;
        var point = NodeName(node);
        value = _runtime.Read(point)?.Value!;
        statusCode = StatusCodes.Good; timestamp = DateTime.UtcNow;
        return ServiceResult.Good;
    }

    private ServiceResult WriteValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
    {
        var transport = _transportFault.BeforeService();
        if (StatusCode.IsBad(transport.StatusCode)) return transport;
        var result = _runtime.Write(NodeName(node), value);
        return result.Succeeded ? ServiceResult.Good : StatusCodes.BadNotWritable;
    }

    private void OnRuntimeEvent(RuntimeEvent runtimeEvent)
    {
        lock (Lock)
        {
            if (runtimeEvent is DataPointChanged change && _variables.TryGetValue(change.DataPointId.Value, out var variable))
            {
                variable.Value = change.NewValue.Value;
                variable.Timestamp = DateTime.UtcNow;
                variable.ClearChangeMasks(SystemContext, false);
            }
            if (_device is null) return;
            var message = runtimeEvent switch
            {
                DataPointChanged dataPointChange => $"DataPointChanged:{dataPointChange.DataPointId.Value}",
                CommandExecuted command => $"CommandExecuted:{command.CommandName}",
                DeviceStarted => "DeviceStarted",
                DeviceStopped => "DeviceStopped",
                _ => runtimeEvent.GetType().Name
            };
            var eventState = new BaseEventState(null);
            eventState.Initialize(SystemContext, _device, EventSeverity.Medium, new LocalizedText(message));
            Server.ReportEvent(SystemContext, eventState);
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
        DataType.Int8 => DataTypeIds.SByte,
        DataType.Int16 => DataTypeIds.Int16,
        DataType.Int32 => DataTypeIds.Int32,
        DataType.Int64 => DataTypeIds.Int64,
        DataType.UInt8 => DataTypeIds.Byte,
        DataType.UInt16 => DataTypeIds.UInt16,
        DataType.UInt32 => DataTypeIds.UInt32,
        DataType.UInt64 => DataTypeIds.UInt64,
        DataType.Float => DataTypeIds.Float,
        DataType.Double => DataTypeIds.Double,
        DataType.String => DataTypeIds.String,
        _ => DataTypeIds.BaseDataType
    };
}

internal enum OpcUaTransportFaultMode { None, Disconnect, Timeout, Latency }

internal sealed class OpcUaTransportFaultController
{
    private readonly object _gate = new();
    private OpcUaTransportFaultMode _mode;
    private TimeSpan _duration;

    public OpcUaTransportFaultMode Mode { get { lock (_gate) return _mode; } }

    public void Apply(string fault, TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        var mode = fault.ToLowerInvariant() switch
        {
            "disconnect" => OpcUaTransportFaultMode.Disconnect,
            "timeout" => OpcUaTransportFaultMode.Timeout,
            "latency" => OpcUaTransportFaultMode.Latency,
            _ => throw new ArgumentException($"Unsupported OPC UA transport fault '{fault}'.", nameof(fault))
        };
        lock (_gate) { _mode = mode; _duration = duration; }
    }

    public void Recover() { lock (_gate) { _mode = OpcUaTransportFaultMode.None; _duration = TimeSpan.Zero; } }

    public ServiceResult BeforeService()
    {
        OpcUaTransportFaultMode mode;
        TimeSpan duration;
        lock (_gate) { mode = _mode; duration = _duration; }
        if ((mode is OpcUaTransportFaultMode.Timeout or OpcUaTransportFaultMode.Latency) && duration > TimeSpan.Zero) Thread.Sleep(duration);
        return mode switch
        {
            OpcUaTransportFaultMode.Disconnect => StatusCodes.BadNotConnected,
            OpcUaTransportFaultMode.Timeout => StatusCodes.BadTimeout,
            _ => ServiceResult.Good
        };
    }
}
