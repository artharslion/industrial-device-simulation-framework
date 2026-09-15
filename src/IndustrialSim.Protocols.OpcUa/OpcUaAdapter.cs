using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;

namespace IndustrialSim.Protocols.OpcUa;

public sealed class OpcUaAdapter : IProtocolAdapter
{
    private readonly OpcUaEndpointHostManager _manager;
    private IDeviceRuntime? _runtime;
    private OpcUaDeviceRegistration? _registration;
    private readonly OpcUaTransportFaultController _transportFault = new();
    private IReadOnlyDictionary<string, string> _dataPointNodeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public OpcUaAdapter() : this(new OpcUaEndpointHostManager()) { }

    public OpcUaAdapter(OpcUaEndpointHostManager manager) =>
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    public string Name => "opcua";
    public bool IsRunning { get; private set; }
    public bool IsDisconnected { get; private set; }
    public TimeSpan Latency { get; private set; }
    public string Endpoint { get; private set; } = "opc.tcp://0.0.0.0:4840";
    public int Port { get; private set; }
    public bool IsStandardOpcUaServer => _registration?.IsServerRunning == true && IsRunning;
    public event Action<DataPointChanged>? DataPointChanged;
    public IReadOnlyCollection<string> Nodes => _runtime is null
        ? []
        : _runtime.Definition.DataPoints.Select(point => NodeIdFor(point.Name))
            .Concat(_runtime.Definition.Commands.Select(command => $"{_runtime.Definition.Id.Value}/{command.Name}"))
            .ToArray();

    public void Configure(IReadOnlyDictionary<string, string>? dataPointNodeIds)
    {
        _dataPointNodeIds = dataPointNodeIds is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(dataPointNodeIds, StringComparer.OrdinalIgnoreCase);
    }

    public void ApplyTransportFault(string fault, TimeSpan duration)
    {
        var notificationsWereSuppressed = _transportFault.SuppressNotifications;
        _transportFault.Apply(fault, duration);
        IsDisconnected = _transportFault.Mode is OpcUaTransportFaultMode.Disconnect or OpcUaTransportFaultMode.Timeout;
        Latency = _transportFault.Mode == OpcUaTransportFaultMode.Latency ? duration : TimeSpan.Zero;
        if (notificationsWereSuppressed && !_transportFault.SuppressNotifications) _registration?.Refresh();
    }

    public void RecoverTransportFault()
    {
        _transportFault.Recover();
        IsDisconnected = false;
        Latency = TimeSpan.Zero;
        _registration?.Refresh();
    }

    public async Task StartAsync(
        IDeviceRuntime runtime,
        ProtocolOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(runtime);
        if (IsRunning) return;

        _runtime = runtime;
        Endpoint = options.Endpoint ?? (options.Port > 0 ? $"opc.tcp://0.0.0.0:{options.Port}" : Endpoint);
        _runtime.State.DataPointChanged += OnDataPointChanged;
        try
        {
            if (options.Port > 0)
            {
                _registration = await _manager.RegisterAsync(
                    Endpoint,
                    runtime.Definition.Id.Value,
                    runtime,
                    _dataPointNodeIds,
                    _transportFault,
                    cancellationToken);
                Endpoint = _registration.Descriptor.Endpoint;
                Port = _registration.Descriptor.Port;
            }
            IsRunning = true;
        }
        catch
        {
            _runtime.State.DataPointChanged -= OnDataPointChanged;
            _runtime = null;
            Port = 0;
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRunning && _runtime is null) return;
        if (_registration is not null)
        {
            await _registration.DisposeAsync();
            _registration = null;
        }
        if (_runtime is not null) _runtime.State.DataPointChanged -= OnDataPointChanged;
        _runtime = null;
        IsRunning = false;
        Port = 0;
    }

    public async Task StartServerAsync(int port = 4840, CancellationToken cancellationToken = default)
    {
        Ensure();
        if (_registration is not null) throw new InvalidOperationException("OPC UA server is already running.");
        Endpoint = $"opc.tcp://0.0.0.0:{port}";
        _registration = await _manager.RegisterAsync(
            Endpoint,
            _runtime!.Definition.Id.Value,
            _runtime,
            _dataPointNodeIds,
            _transportFault,
            cancellationToken);
        Port = port;
    }

    public object? Read(string node)
    {
        EnsureTransport();
        Ensure();
        if (Latency > TimeSpan.Zero) Thread.Sleep(Latency);
        return _runtime!.Read(DataPointName(node))?.Value;
    }

    public void Write(string node, object? value)
    {
        EnsureTransport();
        Ensure();
        var result = _runtime!.Write(DataPointName(node), value);
        if (!result.Succeeded) throw new InvalidOperationException(result.Error);
    }

    public Task InvokeMethodAsync(string method, CancellationToken cancellationToken = default)
    {
        EnsureTransport();
        Ensure();
        return _runtime!.InvokeCommandAsync(NodeName(method), cancellationToken);
    }

    private static string NodeName(string node) => node[(node.LastIndexOf('/') + 1)..];

    private string DataPointName(string node) =>
        _dataPointNodeIds.FirstOrDefault(pair => pair.Value.Equals(node, StringComparison.Ordinal)).Key ?? NodeName(node);

    private string NodeIdFor(string dataPoint) =>
        _dataPointNodeIds.TryGetValue(dataPoint, out var nodeId) ? nodeId : $"{_runtime!.Definition.Id.Value}/{dataPoint}";

    private void Ensure()
    {
        if (!IsRunning || _runtime is null) throw new InvalidOperationException("OPC UA adapter is not running.");
    }

    private void EnsureTransport()
    {
        if (IsDisconnected) throw new IOException("OPC UA transport disconnected.");
    }

    private void OnDataPointChanged(DataPointChanged change) => DataPointChanged?.Invoke(change);
}
