using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace IndustrialSim.Protocols.OpcUa;

internal sealed record OpcUaDeviceProjection(
    string SimulationKey,
    IDeviceRuntime Runtime,
    IReadOnlyDictionary<string, string> DataPointNodeIds,
    OpcUaTransportFaultController TransportFault);

internal sealed class IndustrialOpcUaServer(IReadOnlyList<OpcUaDeviceProjection> initialProjections) : StandardServer
{
    private IndustrialNodeManager? _nodeManager;

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        _nodeManager = new IndustrialNodeManager(server, configuration, initialProjections);
        return new MasterNodeManager(server, configuration, null, new INodeManager[] { _nodeManager });
    }

    public void AddDevice(OpcUaDeviceProjection projection) =>
        (_nodeManager ?? throw new InvalidOperationException("OPC UA node manager has not started.")).AddDevice(projection);

    public void RemoveDevice(string simulationKey) => _nodeManager?.RemoveDevice(simulationKey);

    public void RefreshDevice(string simulationKey) => _nodeManager?.RefreshDevice(simulationKey);
}

internal sealed class IndustrialNodeManager : CustomNodeManager2
{
    private const string NamespaceUri = "urn:industrial-sim:runtime";
    private readonly IReadOnlyList<OpcUaDeviceProjection> _initialProjections;
    private readonly Dictionary<string, DeviceNodeSet> _devices = new(StringComparer.OrdinalIgnoreCase);
    private FolderState? _industrialSim;
    private FolderState? _devicesFolder;

    public IndustrialNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration,
        IReadOnlyList<OpcUaDeviceProjection> initialProjections)
        : base(server, configuration, NamespaceUri)
    {
        _initialProjections = initialProjections;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var device in _devices.Values) device.Projection.Runtime.RuntimeEventPublished -= device.EventHandler;
            _devices.Clear();
        }
        base.Dispose(disposing);
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            _industrialSim = Folder(null, "industrial-sim", "IndustrialSim");
            _industrialSim.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            AddRootReference(externalReferences, _industrialSim, ReferenceTypeIds.Organizes);
            AddPredefinedNode(SystemContext, _industrialSim);

            _devicesFolder = Folder(_industrialSim, "industrial-sim/devices", "Devices");
            _industrialSim.AddChild(_devicesFolder);
            AddPredefinedNode(SystemContext, _devicesFolder);

            foreach (var projection in _initialProjections) AddDeviceCore(projection);
        }
    }

    public void AddDevice(OpcUaDeviceProjection projection)
    {
        lock (Lock) AddDeviceCore(projection);
    }

    public void RemoveDevice(string simulationKey)
    {
        lock (Lock)
        {
            if (!_devices.Remove(simulationKey, out var set)) return;
            set.Projection.Runtime.RuntimeEventPublished -= set.EventHandler;
            RemoveRootNotifier(set.Device);
            _devicesFolder?.RemoveChild(set.Device);
            RemovePredefinedNode(SystemContext, set.Device, []);
        }
    }

    public void RefreshDevice(string simulationKey)
    {
        lock (Lock)
        {
            if (!_devices.TryGetValue(simulationKey, out var set)) return;
            foreach (var (name, variable) in set.Variables)
            {
                variable.Value = set.Projection.Runtime.Read(name)?.Value;
                variable.StatusCode = StatusCodes.Good;
                variable.Timestamp = DateTime.UtcNow;
                variable.ClearChangeMasks(SystemContext, false);
            }
        }
    }

    private void AddDeviceCore(OpcUaDeviceProjection projection)
    {
        if (_devicesFolder is null) throw new InvalidOperationException("OPC UA address space is not initialized.");
        if (_devices.ContainsKey(projection.SimulationKey))
            throw new InvalidOperationException($"OPC UA device '{projection.SimulationKey}' is already registered.");

        var nodeIds = CandidateNodeIds(projection).ToArray();
        var duplicate = nodeIds.GroupBy(id => id.ToString(), StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"OPC UA NodeId '{duplicate.Key}' is used more than once by device '{projection.SimulationKey}'.");
        foreach (var nodeId in nodeIds)
            if (PredefinedNodes.ContainsKey(nodeId))
                throw new InvalidOperationException($"OPC UA NodeId '{nodeId}' is already registered on the shared endpoint.");

        var device = Folder(_devicesFolder, projection.SimulationKey, projection.SimulationKey);
        device.EventNotifier = EventNotifiers.SubscribeToEvents;
        var metadata = Folder(device, $"{projection.SimulationKey}/$metadata", "Metadata");
        var state = Folder(device, $"{projection.SimulationKey}/$state", "State");
        var dataPoints = Folder(device, $"{projection.SimulationKey}/$datapoints", "Datapoints");
        var commands = Folder(device, $"{projection.SimulationKey}/$commands", "Commands");
        var faults = Folder(device, $"{projection.SimulationKey}/$faults", "Faults");
        device.AddChild(metadata);
        device.AddChild(state);
        device.AddChild(dataPoints);
        device.AddChild(commands);
        device.AddChild(faults);

        var variables = new Dictionary<string, BaseDataVariableState>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in projection.Runtime.Definition.DataPoints)
        {
            var variable = new BaseDataVariableState(device)
            {
                NodeId = DataPointNodeId(projection, point.Name),
                BrowseName = new QualifiedName(point.Name, NamespaceIndex),
                DisplayName = point.Name,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = ToOpcUaDataType(point.DataType),
                ValueRank = ValueRanks.Scalar,
                AccessLevel = ToAccessLevel(point.Access),
                UserAccessLevel = ToAccessLevel(point.Access),
                Value = projection.Runtime.Read(point.Name)?.Value,
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow
            };
            variable.OnReadValue = (ISystemContext context, NodeState node, NumericRange range, QualifiedName encoding, ref object value, ref StatusCode status, ref DateTime timestamp) =>
                ReadValue(projection, point.Name, ref value, ref status, ref timestamp);
            variable.OnWriteValue = (ISystemContext context, NodeState node, NumericRange range, QualifiedName encoding, ref object value, ref StatusCode status, ref DateTime timestamp) =>
                WriteValue(projection, point.Name, value);
            variables.Add(point.Name, variable);
            device.AddChild(variable);
            dataPoints.AddReference(ReferenceTypeIds.Organizes, false, variable.NodeId);
            variable.AddReference(ReferenceTypeIds.Organizes, true, dataPoints.NodeId);
        }

        foreach (var command in projection.Runtime.Definition.Commands)
        {
            var method = new MethodState(device)
            {
                NodeId = new NodeId($"{projection.SimulationKey}/{command.Name}", NamespaceIndex),
                BrowseName = new QualifiedName(command.Name, NamespaceIndex),
                DisplayName = command.Name,
                Executable = true,
                UserExecutable = true,
                OnCallMethod = (_, _, _, _) =>
                {
                    var transport = projection.TransportFault.BeforeService();
                    if (StatusCode.IsBad(transport.StatusCode)) return transport;
                    projection.Runtime.InvokeCommandAsync(command.Name).GetAwaiter().GetResult();
                    return ServiceResult.Good;
                }
            };
            device.AddChild(method);
            commands.AddReference(ReferenceTypeIds.Organizes, false, method.NodeId);
            method.AddReference(ReferenceTypeIds.Organizes, true, commands.NodeId);
        }

        _devicesFolder.AddChild(device);
        AddPredefinedNode(SystemContext, device);
        AddRootNotifier(device);

        Action<RuntimeEvent> handler = runtimeEvent => OnRuntimeEvent(projection.SimulationKey, runtimeEvent);
        projection.Runtime.RuntimeEventPublished += handler;
        _devices.Add(projection.SimulationKey, new DeviceNodeSet(projection, device, variables, handler));
    }

    private void OnRuntimeEvent(string simulationKey, RuntimeEvent runtimeEvent)
    {
        lock (Lock)
        {
            if (!_devices.TryGetValue(simulationKey, out var set)) return;
            if (runtimeEvent is DataPointChanged change &&
                set.Variables.TryGetValue(change.DataPointId.Value, out var variable) &&
                !set.Projection.TransportFault.SuppressNotifications)
            {
                variable.Value = change.NewValue.Value;
                variable.StatusCode = StatusCodes.Good;
                variable.Timestamp = DateTime.UtcNow;
                variable.ClearChangeMasks(SystemContext, false);
            }

            var message = runtimeEvent switch
            {
                DataPointChanged dataPointChange => $"DataPointChanged:{dataPointChange.DataPointId.Value}",
                CommandExecuted command => $"CommandExecuted:{command.CommandName}",
                DeviceStarted => "DeviceStarted",
                DeviceStopped => "DeviceStopped",
                _ => runtimeEvent.GetType().Name
            };
            var eventState = new BaseEventState(null);
            eventState.Initialize(SystemContext, set.Device, EventSeverity.Medium, new LocalizedText(message));
            Server.ReportEvent(SystemContext, eventState);
        }
    }

    private static ServiceResult ReadValue(
        OpcUaDeviceProjection projection,
        string point,
        ref object value,
        ref StatusCode statusCode,
        ref DateTime timestamp)
    {
        var transport = projection.TransportFault.BeforeService();
        if (StatusCode.IsBad(transport.StatusCode)) return transport;
        value = projection.Runtime.Read(point)?.Value!;
        statusCode = StatusCodes.Good;
        timestamp = DateTime.UtcNow;
        return ServiceResult.Good;
    }

    private static ServiceResult WriteValue(OpcUaDeviceProjection projection, string point, object value)
    {
        var transport = projection.TransportFault.BeforeService();
        if (StatusCode.IsBad(transport.StatusCode)) return transport;
        var result = projection.Runtime.Write(point, value);
        return result.Succeeded ? ServiceResult.Good : StatusCodes.BadNotWritable;
    }

    private IEnumerable<NodeId> CandidateNodeIds(OpcUaDeviceProjection projection)
    {
        yield return new NodeId(projection.SimulationKey, NamespaceIndex);
        yield return new NodeId($"{projection.SimulationKey}/$metadata", NamespaceIndex);
        yield return new NodeId($"{projection.SimulationKey}/$state", NamespaceIndex);
        yield return new NodeId($"{projection.SimulationKey}/$datapoints", NamespaceIndex);
        yield return new NodeId($"{projection.SimulationKey}/$commands", NamespaceIndex);
        yield return new NodeId($"{projection.SimulationKey}/$faults", NamespaceIndex);
        foreach (var point in projection.Runtime.Definition.DataPoints) yield return DataPointNodeId(projection, point.Name);
        foreach (var command in projection.Runtime.Definition.Commands) yield return new NodeId($"{projection.SimulationKey}/{command.Name}", NamespaceIndex);
    }

    private NodeId DataPointNodeId(OpcUaDeviceProjection projection, string dataPoint) => new(
        projection.DataPointNodeIds.TryGetValue(dataPoint, out var configured) ? configured : $"{projection.SimulationKey}/{dataPoint}",
        NamespaceIndex);

    private FolderState Folder(NodeState? parent, string nodeId, string browseName) => new(parent)
    {
        NodeId = new NodeId(nodeId, NamespaceIndex),
        BrowseName = new QualifiedName(browseName, NamespaceIndex),
        DisplayName = browseName,
        TypeDefinitionId = ObjectTypeIds.FolderType
    };

    private static void AddRootReference(
        IDictionary<NodeId, IList<IReference>> externalReferences,
        NodeState node,
        NodeId referenceType)
    {
        if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
        {
            references = new List<IReference>();
            externalReferences[ObjectIds.ObjectsFolder] = references;
        }
        references.Add(new NodeStateReference(referenceType, false, node.NodeId));
    }

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

    private sealed record DeviceNodeSet(
        OpcUaDeviceProjection Projection,
        FolderState Device,
        IReadOnlyDictionary<string, BaseDataVariableState> Variables,
        Action<RuntimeEvent> EventHandler);
}

internal enum OpcUaTransportFaultMode { None, Disconnect, Timeout, Latency }

internal sealed class OpcUaTransportFaultController
{
    private readonly object _gate = new();
    private OpcUaTransportFaultMode _mode;
    private TimeSpan _duration;

    public OpcUaTransportFaultMode Mode { get { lock (_gate) return _mode; } }
    public bool SuppressNotifications { get { lock (_gate) return _mode is OpcUaTransportFaultMode.Disconnect or OpcUaTransportFaultMode.Timeout; } }

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
