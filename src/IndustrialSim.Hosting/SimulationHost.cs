using System.Collections.Concurrent;
using IndustrialSim.Configuration;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Devices;
using IndustrialSim.Devices.Motor;
using IndustrialSim.Devices.Pump;
using IndustrialSim.Devices.Sensor;
using IndustrialSim.Faults;
using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Protocols.Modbus;
using IndustrialSim.Protocols.OpcUa;
using IndustrialSim.Runtime.Engine;
using IndustrialSim.Runtime.State;
using IndustrialSim.Runtime.Time;
using IndustrialSim.Scenarios;

namespace IndustrialSim.Hosting;

public sealed record HostConfigurationOverrides(string? OpcUaEndpoint = null, int? ModbusPort = null, int? WebPort = null, string? LogLevel = null)
{
    public static HostConfigurationOverrides Resolve(
        string? cliOpcUaEndpoint = null,
        string? cliModbusPort = null,
        string? cliWebPort = null,
        string? cliLogLevel = null,
        Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var endpoint = First(cliOpcUaEndpoint, environment("INDUSTRIALSIM_OPCUA_ENDPOINT"));
        if (endpoint is not null && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("opc.tcp", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(uri.Host) || uri.Port is < 1 or > 65535))
            throw new ArgumentException($"OPC UA endpoint override '{endpoint}' must be an absolute opc.tcp URI with a valid host and port.");
        return new HostConfigurationOverrides(
            endpoint,
            ParsePort("Modbus", First(cliModbusPort, environment("INDUSTRIALSIM_MODBUS_PORT"))),
            ParsePort("Web", First(cliWebPort, environment("INDUSTRIALSIM_WEB_PORT"))),
            First(cliLogLevel, environment("INDUSTRIALSIM_LOG_LEVEL")));
    }

    private static string? First(string? preferred, string? fallback) => !string.IsNullOrWhiteSpace(preferred) ? preferred.Trim() : !string.IsNullOrWhiteSpace(fallback) ? fallback.Trim() : null;
    private static int? ParsePort(string name, string? value)
    {
        if (value is null) return null;
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535) throw new ArgumentException($"{name} port override must be between 1 and 65535.");
        return port;
    }
}

public sealed record SimulationHostOptions(bool Deterministic = false, int Seed = 0, HostConfigurationOverrides? Overrides = null);

public sealed class SimulationHost : IAsyncDisposable
{
    public const int EventRetentionCapacity = 1000;

    private readonly DeviceLaunchDefinition _launch;
    private readonly Dictionary<string, IProtocolAdapter> _protocols = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<object> _events = new();
    private int _eventCount;
    private readonly Dictionary<string, DataFaultProcessor> _dataFaultProcessors = new(StringComparer.OrdinalIgnoreCase);
    private readonly DeviceFaultController _deviceFaultController;
    private readonly DeviceBehaviorDefinition? _behavior;
    private readonly SimulationHostOptions _options;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private Motor? _motor;
    private Pump? _pump;
    private Sensor? _sensor;
    private TimeSpan _lastBehaviorTime;
    private bool _disposed;
    private IProtocolOperationObserver? _protocolOperationObserver;
    private OpcUaEndpointHostManager _opcUaServers;
    private bool _ownsOpcUaServers;

    private SimulationHost(DeviceLaunchDefinition launch, OpcUaEndpointHostManager? opcUaServers = null)
    {
        ValidateLaunch(launch);
        _launch = launch;
        _opcUaServers = opcUaServers ?? new OpcUaEndpointHostManager();
        _ownsOpcUaServers = opcUaServers is null;
        _options = launch.Options;
        var configuration = launch.Definition;
        var options = launch.Options;
        Engine = new SimulationEngine(options.Deterministic ? new DeterministicClock() : new RealTimeClock());
        var state = new StateStore(configuration);
        var commandHandlers = new Dictionary<string, Func<CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase);
        _behavior = ResolveBehavior(configuration);
        if (configuration.Behavior is not null)
        {
            try { BuiltInDeviceProfiles.Validate(configuration, configuration.Behavior); }
            catch (ArgumentException exception) { throw new DeviceLaunchException(exception.Message, "invalidBehaviorProfile"); }
        }
        if (_behavior is not null && !_behavior.Profile.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            switch (_behavior.Profile.ToLowerInvariant())
            {
                case "pump":
                    _pump = CreatePump(state, _behavior);
                    commandHandlers["start"] = _ => { _pump.Start(Engine.CurrentTime); return Task.CompletedTask; };
                    commandHandlers["stop"] = _ => { _pump.Stop(Engine.CurrentTime); return Task.CompletedTask; };
                    break;
                case "motor":
                    _motor = CreateMotor(state, _behavior);
                    commandHandlers["start"] = _ => { _motor.Start(Engine.CurrentTime); return Task.CompletedTask; };
                    commandHandlers["stop"] = _ => { _motor.Stop(Engine.CurrentTime); return Task.CompletedTask; };
                    break;
                case "sensor":
                    _sensor = CreateSensor(state, _behavior);
                    commandHandlers["reset"] = _ => { _sensor.Reset(Engine.CurrentTime); return Task.CompletedTask; };
                    break;
            }
        }
        Runtime = new InMemoryDeviceRuntime(configuration, state, commandHandlers, () => Engine.CurrentTime);
        FaultManager = new FaultManager(Engine);
        _deviceFaultController = new DeviceFaultController(Runtime.State);
        Runtime.RuntimeEventPublished += RetainEvent;
        FaultManager.LifecycleChanged += OnFaultLifecycleChanged;

        if (launch.OpcUa is not null)
        {
            var opcUa = new OpcUaAdapter(_opcUaServers);
            opcUa.Configure(launch.OpcUa.DataPointNodeIds);
            _protocols.Add("opcua", opcUa);
        }
        if (launch.Modbus is not null)
        {
            var modbus = new ModbusAdapter();
            modbus.Configure(launch.Modbus.Mappings);
            _protocols.Add("modbus", modbus);
        }
    }

    public IDeviceRuntime Runtime { get; }
    public StateStore State => Runtime.State;
    public SimulationEngine Engine { get; }
    public FaultManager FaultManager { get; }
    public ScenarioRunner? ScenarioRunner { get; private set; }
    public string? ActiveScenarioName { get; private set; }
    public IReadOnlyDictionary<string, IProtocolAdapter> Protocols => _protocols;
    public IReadOnlyCollection<object> Events => _events.ToArray();
    public event Action<ScenarioActionObservation>? ScenarioActionObserved;
    public event Action<ProtocolLifecycleObservation>? ProtocolLifecycleObserved;
    public bool IsRunning { get; private set; }
    public bool IsDeterministic => _options.Deterministic;
    public int Seed => _options.Seed;
    public DeviceBehaviorDefinition? Behavior => _behavior;
    public DeviceLaunchDefinition LaunchDefinition => _launch;
    public IReadOnlyList<ProtocolPortBinding> PortBindings => _launch.PortBindings;
    public int WebPort => _launch.WebPort;
    public long TotalTicks => Interlocked.Read(ref _totalTicks);

    private long _totalTicks;

    public void AttachProtocolOperationObserver(IProtocolOperationObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var existing = Interlocked.CompareExchange(ref _protocolOperationObserver, observer, null);
        if (existing is not null && !ReferenceEquals(existing, observer))
            throw new InvalidOperationException("A simulation host can use only one protocol operation observer.");
    }

    public static Task<SimulationHost> LoadAsync(string path, CancellationToken cancellationToken = default) => LoadAsync(path, new SimulationHostOptions(), cancellationToken);

    public static Task<SimulationHost> LoadAsync(string path, SimulationHostOptions options, CancellationToken cancellationToken = default) =>
        LoadAsync(path, options, null, cancellationToken);

    public static async Task<SimulationHost> LoadAsync(
        string path,
        SimulationHostOptions options,
        OpcUaEndpointHostManager? opcUaServers,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Configuration path cannot be blank.", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException($"Device configuration file '{path}' was not found.", path);
        var yaml = await File.ReadAllTextAsync(path, cancellationToken);
        var loaded = new YamlConfigurationLoader().Load(yaml);
        var opcUa = loaded.Configuration.Protocols?.Opcua is { Enabled: true } opc
            ? new OpcUaLaunchDefinition(options.Overrides?.OpcUaEndpoint ?? opc.Endpoint ?? "opc.tcp://0.0.0.0:4840")
            : null;
        var modbus = loaded.Configuration.Protocols?.Modbus is { Enabled: true } modbusConfiguration
            ? new ModbusLaunchDefinition(options.Overrides?.ModbusPort ?? modbusConfiguration.Port, loaded.ModbusMappings)
            : null;
        var webPort = options.Overrides?.WebPort ?? loaded.Configuration.Web?.Port ?? 8080;
        return new SimulationHost(new DeviceLaunchDefinition(loaded.Device, options, opcUa, modbus, new DeviceLaunchSource("yaml"), webPort), opcUaServers);
    }

    public static SimulationHost Create(DeviceLaunchDefinition launch) => new(launch);

    public static SimulationHost Create(DeviceLaunchDefinition launch, OpcUaEndpointHostManager opcUaServers) =>
        new(launch, opcUaServers ?? throw new ArgumentNullException(nameof(opcUaServers)));

    public static SimulationHost Create(DeviceDefinition definition, SimulationHostOptions? options = null) =>
        Create(new DeviceLaunchDefinition(definition, options ?? new SimulationHostOptions()));

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsRunning)
        {
            await Engine.StartAsync(cancellationToken);
            if (!IsDeterministic && _loopTask is null) StartRealTimeLoop();
            return;
        }
        var started = new List<IProtocolAdapter>();
        try
        {
            await Engine.StartAsync(cancellationToken);
            foreach (var (name, protocol) in _protocols)
            {
                started.Add(protocol);
                using var operation = _protocolOperationObserver?.Start(Runtime.Definition.Id.Value, name, "start");
                try
                {
                    switch (name)
                    {
                        case "opcua":
                            var endpoint = _launch.OpcUa!.Endpoint;
                            var opcPort = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Port > 0 ? uri.Port : 4840;
                            await protocol.StartAsync(Runtime, new ProtocolOptions(endpoint, opcPort), cancellationToken);
                            break;
                        case "modbus":
                            var modbusPort = _launch.Modbus!.Port;
                            await protocol.StartAsync(Runtime, new ProtocolOptions(Port: modbusPort), cancellationToken);
                            if (modbusPort == 0) await ((ModbusAdapter)protocol).StartServerAsync(0, cancellationToken);
                            break;
                    }
                    operation?.SetResult(true, null);
                    ObserveProtocol(name, "start", true, null);
                }
                catch (Exception exception)
                {
                    operation?.SetResult(false, exception.GetType().Name);
                    ObserveProtocol(name, "start", false, exception.GetType().Name);
                    throw;
                }
            }
            IsRunning = true;
            Runtime.Publish(new DeviceStarted(Engine.CurrentTime, Runtime.Definition.Id));
            if (!IsDeterministic) StartRealTimeLoop();
        }
        catch
        {
            foreach (var protocol in started.AsEnumerable().Reverse()) await protocol.StopAsync(CancellationToken.None);
            await Engine.StopAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ValidateLaunch(DeviceLaunchDefinition launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(launch.Definition);
        if (launch.OpcUa is { } opcUa)
        {
            if (!Uri.TryCreate(opcUa.Endpoint, UriKind.Absolute, out var uri) ||
                !uri.Scheme.Equals("opc.tcp", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host) || uri.Port is < 1 or > 65535)
                throw new DeviceLaunchException($"OPC UA endpoint '{opcUa.Endpoint}' must be an absolute opc.tcp URI with a valid host and port.", "invalidOpcUaConfiguration");
            if (opcUa.DataPointNodeIds is { } nodeIds)
            {
                var points = launch.Definition.DataPoints.Select(point => point.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var (dataPoint, nodeId) in nodeIds)
                {
                    if (!points.Contains(dataPoint)) throw new DeviceLaunchException($"OPC UA mapping targets unknown data point '{dataPoint}'.", "invalidOpcUaConfiguration");
                    if (string.IsNullOrWhiteSpace(nodeId)) throw new DeviceLaunchException($"OPC UA mapping for '{dataPoint}' requires a node id.", "invalidOpcUaConfiguration");
                }
                var duplicate = nodeIds.Values.GroupBy(value => value, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
                if (duplicate is not null) throw new DeviceLaunchException($"OPC UA node id '{duplicate.Key}' is mapped more than once.", "invalidOpcUaConfiguration");
            }
        }
        if (launch.Modbus is { } modbus)
        {
            if (modbus.Port is < 1 or > 65535) throw new DeviceLaunchException("Modbus port must be between 1 and 65535.", "invalidModbusMapping");
            if (modbus.Mappings.Count == 0) throw new DeviceLaunchException("Enabled Modbus requires at least one mapping.", "modbusMappingRequired");
            try
            {
                ModbusMappingValidator.ValidateResolved(modbus.Mappings);
                YamlConfigurationLoader.ValidateMappings(launch.Definition, modbus.Mappings);
            }
            catch (ArgumentException exception)
            {
                throw new DeviceLaunchException(exception.Message, "invalidModbusMapping");
            }
        }
        if (launch.WebPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(launch), "Web port must be between 1 and 65535.");
    }

    public ScenarioRunner RunScenario(string yaml)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var scenario = new ScenarioParser().Parse(yaml);
        var runner = new ScenarioRunner(
            scenario,
            Engine,
            State,
            command: (_, command) => Runtime.InvokeCommandAsync(command).GetAwaiter().GetResult(),
            faultAction: ScheduleScenarioFault);
        runner.ActionExecuted += (action, timestamp) => ScenarioActionObserved?.Invoke(new ScenarioActionObservation(
            Runtime.Definition.Id.Value,
            scenario.Name,
            ActionName(action),
            timestamp));
        runner.Start();
        ScenarioRunner?.Stop();
        ActiveScenarioName = scenario.Name;
        ScenarioRunner = runner;
        return runner;
    }

    public void Tick(TimeSpan amount)
    {
        if (Engine.State != EngineState.Running) return;
        Engine.Tick(amount);
        UpdateDeviceBehavior(amount);
        Interlocked.Increment(ref _totalTicks);
    }

    public void Update() => Engine.Update();

    public bool StopScenario()
    {
        if (ScenarioRunner is null || !ScenarioRunner.IsRunning) return false;
        ScenarioRunner.Stop();
        return true;
    }

    public void Reset()
    {
        StopScenario();
        Engine.Reset();
        foreach (var point in Runtime.Definition.DataPoints)
            if (point.InitialValue is not null) State.SetInternal(new DataPointId(point.Name), point.InitialValue.Value, Engine.CurrentTime);
        if (_pump is not null) _pump = CreatePump(State, _behavior!);
        if (_motor is not null) _motor = CreateMotor(State, _behavior!);
        if (_sensor is not null) _sensor = CreateSensor(State, _behavior!);
    }

    public void ScheduleFault(FaultSpec fault)
    {
        ValidateFault(fault);
        FaultManager.Schedule(fault);
    }

    public void ActivateFault(FaultSpec fault)
    {
        ValidateFault(fault);
        FaultManager.Activate(fault);
    }

    public bool RecoverFault(string id) => FaultManager.Recover(id);

    public void ApplyNetworkFault(string protocol, string type, TimeSpan duration)
    {
        if (!_protocols.TryGetValue(protocol, out var adapter)) throw new ArgumentException($"Protocol '{protocol}' is not configured.", nameof(protocol));
        adapter.ApplyTransportFault(type, duration);
    }

    public void RecoverNetworkFault(string protocol)
    {
        if (!_protocols.TryGetValue(protocol, out var adapter)) throw new ArgumentException($"Protocol '{protocol}' is not configured.", nameof(protocol));
        adapter.RecoverTransportFault();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRunning && Engine.State == EngineState.Stopped) return;
        if (_loopCts is not null)
        {
            await _loopCts.CancelAsync();
            if (_loopTask is not null) await _loopTask;
            _loopCts.Dispose();
            _loopCts = null;
            _loopTask = null;
        }
        var publishStopped = IsRunning;
        foreach (var (name, protocol) in _protocols.Reverse())
        {
            if (!protocol.IsRunning) continue;
            using var operation = _protocolOperationObserver?.Start(Runtime.Definition.Id.Value, name, "stop");
            try
            {
                await protocol.StopAsync(cancellationToken);
                operation?.SetResult(true, null);
                ObserveProtocol(name, "stop", true, null);
            }
            catch (Exception exception)
            {
                operation?.SetResult(false, exception.GetType().Name);
                ObserveProtocol(name, "stop", false, exception.GetType().Name);
                throw;
            }
        }
        await Engine.StopAsync(cancellationToken);
        IsRunning = false;
        if (publishStopped) Runtime.Publish(new DeviceStopped(Engine.CurrentTime, Runtime.Definition.Id));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync(CancellationToken.None);
        if (_ownsOpcUaServers) await _opcUaServers.DisposeAsync();
        _disposed = true;
    }

    internal void UseOpcUaServers(OpcUaEndpointHostManager opcUaServers)
    {
        ArgumentNullException.ThrowIfNull(opcUaServers);
        if (IsRunning) throw new InvalidOperationException("A running simulation cannot change its OPC UA server manager.");
        if (_launch.OpcUa is null || ReferenceEquals(_opcUaServers, opcUaServers)) return;
        var adapter = new OpcUaAdapter(opcUaServers);
        adapter.Configure(_launch.OpcUa.DataPointNodeIds);
        _protocols["opcua"] = adapter;
        _opcUaServers = opcUaServers;
        _ownsOpcUaServers = false;
    }

    private void ScheduleScenarioFault(FaultAction action)
    {
        var category = !string.IsNullOrWhiteSpace(action.Protocol) || action.Type.StartsWith("network.", StringComparison.OrdinalIgnoreCase)
            ? FaultCategory.Network
            : !string.IsNullOrWhiteSpace(action.DataPoint) || action.Type.StartsWith("data.", StringComparison.OrdinalIgnoreCase)
                ? FaultCategory.Data
                : FaultCategory.Device;
        var type = action.Type.Contains('.') ? action.Type[(action.Type.LastIndexOf('.') + 1)..] : action.Type;
        var target = category switch
        {
            FaultCategory.Network => action.Protocol,
            FaultCategory.Data => action.DataPoint,
            _ => null
        };
        ScheduleFault(new FaultSpec(
            $"scenario-{Guid.NewGuid():N}",
            category,
            string.IsNullOrWhiteSpace(action.Device) ? Runtime.Definition.Id.Value : action.Device,
            target,
            Engine.CurrentTime.Elapsed,
            action.Duration,
            type,
            action.Metadata));
    }

    private void OnFaultLifecycleChanged(FaultEvent change)
    {
        RetainEvent(change);
        if (change.Lifecycle == FaultLifecycle.Active) ApplyFault(change.Fault);
        else if (change.Lifecycle == FaultLifecycle.Recovered) RecoverFaultEffect(change.Fault);
    }

    private void ApplyFault(FaultSpec fault)
    {
        switch (fault.Category)
        {
            case FaultCategory.Data:
                var dataPointId = new DataPointId(fault.Target!);
                var current = State.GetExposedInternal(dataPointId)!;
                var point = Runtime.Definition.DataPoints.Single(item => item.Name.Equals(fault.Target, StringComparison.OrdinalIgnoreCase));
                Enum.TryParse<DataFaultType>(fault.Type, true, out var dataType);
                var seed = fault.Metadata is not null && fault.Metadata.ContainsKey("seed") ? MetadataInt(fault, "seed") : Seed;
                var parameter = MetadataDouble(fault, "parameter");
                var processor = new DataFaultProcessor(seed);
                _dataFaultProcessors[fault.Id] = processor;
                var frozen = current.Value;
                State.AddValueProjection(fault.Id, dataPointId, candidate =>
                {
                    var input = dataType is DataFaultType.Stale or DataFaultType.Freeze ? frozen : candidate.Value;
                    return ScalarValue.Create(point.DataType, processor.Apply(dataType, input, parameter));
                }, Engine.CurrentTime);
                break;
            case FaultCategory.Device:
                if (!Enum.TryParse<DeviceFaultType>(fault.Type, true, out var deviceType)) throw new ArgumentException($"Device fault '{fault.Id}' has unsupported type '{fault.Type}'.");
                _deviceFaultController.Activate(deviceType, Engine.CurrentTime);
                break;
            case FaultCategory.Network:
                if (string.IsNullOrWhiteSpace(fault.Target)) throw new ArgumentException($"Network fault '{fault.Id}' requires a protocol target.");
                ApplyNetworkFault(fault.Target, fault.Type ?? "disconnect", fault.Duration ?? TimeSpan.Zero);
                break;
        }
    }

    private void RecoverFaultEffect(FaultSpec fault)
    {
        switch (fault.Category)
        {
            case FaultCategory.Data when !string.IsNullOrWhiteSpace(fault.Target):
                State.RemoveValueProjection(fault.Id, Engine.CurrentTime);
                if (_dataFaultProcessors.Remove(fault.Id, out var processor)) processor.Recover();
                break;
            case FaultCategory.Device:
                if (Enum.TryParse<DeviceFaultType>(fault.Type, true, out var deviceType)) _deviceFaultController.Recover(deviceType, Engine.CurrentTime);
                break;
            case FaultCategory.Network when !string.IsNullOrWhiteSpace(fault.Target):
                RecoverNetworkFault(fault.Target);
                break;
        }
    }

    private static int MetadataInt(FaultSpec fault, string name) => fault.Metadata is not null && fault.Metadata.TryGetValue(name, out var value) && int.TryParse(value, out var parsed) ? parsed : 0;
    private static double MetadataDouble(FaultSpec fault, string name) => fault.Metadata is not null && fault.Metadata.TryGetValue(name, out var value) && double.TryParse(value, out var parsed) ? parsed : 0;

    private void ValidateFault(FaultSpec fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        if (!string.Equals(fault.Device, Runtime.Definition.Id.Value, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Fault device '{fault.Device}' does not match runtime device '{Runtime.Definition.Id.Value}'.");
        switch (fault.Category)
        {
            case FaultCategory.Data:
                if (string.IsNullOrWhiteSpace(fault.Target)) throw new ArgumentException($"Data fault '{fault.Id}' requires a datapoint target.");
                if (!Runtime.Definition.DataPoints.Any(point => point.Name.Equals(fault.Target, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"Data fault '{fault.Id}' targets unknown data point '{fault.Target}'.");
                if (State.GetExposedInternal(new DataPointId(fault.Target)) is null) throw new ArgumentException($"Data fault '{fault.Id}' requires an initialized data point target.");
                if (!Enum.TryParse<DataFaultType>(fault.Type, true, out _)) throw new ArgumentException($"Data fault '{fault.Id}' has unsupported type '{fault.Type}'.");
                ValidateNumericMetadata<double>(fault, "parameter", double.TryParse);
                ValidateNumericMetadata<int>(fault, "seed", int.TryParse);
                break;
            case FaultCategory.Device:
                if (!Enum.TryParse<DeviceFaultType>(fault.Type, true, out var deviceType)) throw new ArgumentException($"Device fault '{fault.Id}' has unsupported type '{fault.Type}'.");
                var requiredPoint = deviceType is DeviceFaultType.SensorFailure or DeviceFaultType.Overheat ? "alarm" : "running";
                if (!Runtime.Definition.DataPoints.Any(point => point.Name.Equals(requiredPoint, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"Device fault '{fault.Id}' requires data point '{requiredPoint}'.");
                break;
            case FaultCategory.Network:
                if (string.IsNullOrWhiteSpace(fault.Target) || !_protocols.ContainsKey(fault.Target)) throw new ArgumentException($"Network fault '{fault.Id}' targets an unconfigured protocol '{fault.Target}'.");
                if (!Enum.TryParse<NetworkFaultType>(fault.Type, true, out _)) throw new ArgumentException($"Network fault '{fault.Id}' has unsupported type '{fault.Type}'.");
                break;
        }
    }

    private static void ValidateNumericMetadata<T>(FaultSpec fault, string name, TryParse<T> tryParse)
    {
        if (fault.Metadata is not null && fault.Metadata.TryGetValue(name, out var value) && !tryParse(value, out _)) throw new ArgumentException($"Fault '{fault.Id}' metadata '{name}' must be numeric.");
    }

    private delegate bool TryParse<T>(string value, out T result);

    private void StartRealTimeLoop()
    {
        _lastBehaviorTime = Engine.CurrentTime.Elapsed;
        _loopCts = new CancellationTokenSource();
        _loopTask = RunRealTimeLoopAsync(_loopCts.Token);
    }

    private async Task RunRealTimeLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var now = Engine.CurrentTime.Elapsed;
                if (Engine.State != EngineState.Running) { _lastBehaviorTime = now; continue; }
                Engine.Update();
                UpdateDeviceBehavior(now - _lastBehaviorTime);
                _lastBehaviorTime = now;
                Interlocked.Increment(ref _totalTicks);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void UpdateDeviceBehavior(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) return;
        _pump?.Update(elapsed, Engine.CurrentTime);
        _motor?.Update(elapsed, Engine.CurrentTime);
        _sensor?.Update(elapsed, Engine.CurrentTime);
    }

    private void RetainEvent(object @event)
    {
        _events.Enqueue(@event);
        var count = Interlocked.Increment(ref _eventCount);
        while (count > EventRetentionCapacity && _events.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _eventCount);
        }
    }

    private void ObserveProtocol(string protocol, string operation, bool succeeded, string? errorCode) =>
        ProtocolLifecycleObserved?.Invoke(new ProtocolLifecycleObservation(
            Runtime.Definition.Id.Value,
            protocol,
            operation,
            succeeded,
            errorCode,
            Engine.CurrentTime));

    private static string ActionName(ScenarioAction action) => action switch
    {
        SetAction => "set",
        RampAction => "ramp",
        CommandAction => "command",
        WaitAction => "wait",
        FaultAction => "fault",
        _ => "unknown"
    };

    private static bool CanAttachPump(DeviceDefinition definition)
    {
        if (!definition.Type.Equals("pump", StringComparison.OrdinalIgnoreCase)) return false;
        var points = definition.DataPoints.ToDictionary(point => point.Name, StringComparer.OrdinalIgnoreCase);
        return points.TryGetValue("speed", out var speed) && speed.DataType == DataType.Int32
            && points.TryGetValue("temperature", out var temperature) && temperature.DataType == DataType.Double
            && points.TryGetValue("pressure", out var pressure) && pressure.DataType == DataType.Double
            && points.TryGetValue("running", out var running) && running.DataType == DataType.Boolean
            && points.TryGetValue("alarm", out var alarm) && alarm.DataType == DataType.Boolean;
    }

    private static bool CanAttachMotor(DeviceDefinition definition)
    {
        if (!definition.Type.Equals("motor", StringComparison.OrdinalIgnoreCase)) return false;
        var points = definition.DataPoints.ToDictionary(point => point.Name, StringComparer.OrdinalIgnoreCase);
        return points.TryGetValue("speed", out var speed) && speed.DataType == DataType.Int32
            && points.TryGetValue("temperature", out var temperature) && temperature.DataType == DataType.Double
            && points.TryGetValue("current", out var current) && current.DataType == DataType.Double
            && points.TryGetValue("running", out var running) && running.DataType == DataType.Boolean
            && points.TryGetValue("alarm", out var alarm) && alarm.DataType == DataType.Boolean;
    }

    private static bool CanAttachSensor(DeviceDefinition definition)
    {
        if (!definition.Type.Equals("sensor", StringComparison.OrdinalIgnoreCase)) return false;
        var points = definition.DataPoints.ToDictionary(point => point.Name, StringComparer.OrdinalIgnoreCase);
        return points.TryGetValue("value", out var value) && value.DataType == DataType.Double
            && points.TryGetValue("quality", out var quality) && quality.DataType == DataType.String;
    }

    private static DeviceBehaviorDefinition? ResolveBehavior(DeviceDefinition definition)
    {
        if (definition.Behavior is not null) return definition.Behavior;
        if (CanAttachPump(definition)) return new DeviceBehaviorDefinition("pump");
        if (CanAttachMotor(definition)) return new DeviceBehaviorDefinition("motor");
        if (CanAttachSensor(definition)) return new DeviceBehaviorDefinition("sensor");
        return null;
    }

    private static Pump CreatePump(StateStore state, DeviceBehaviorDefinition behavior) => new(state, new PumpParameters(
        (int)Math.Round(BuiltInDeviceProfiles.Parameter(behavior, "ratedSpeed")),
        TimeSpan.FromSeconds(BuiltInDeviceProfiles.Parameter(behavior, "accelerationSeconds")),
        BuiltInDeviceProfiles.Parameter(behavior, "maxPressure"),
        BuiltInDeviceProfiles.Parameter(behavior, "heatingRatePerSecond"),
        BuiltInDeviceProfiles.Parameter(behavior, "coolingRatePerSecond"),
        BuiltInDeviceProfiles.Parameter(behavior, "overheatTemperature"),
        BuiltInDeviceProfiles.Parameter(behavior, "normalOperatingTemperature")));

    private static Motor CreateMotor(StateStore state, DeviceBehaviorDefinition behavior) => new(state, new MotorParameters(
        (int)Math.Round(BuiltInDeviceProfiles.Parameter(behavior, "ratedSpeed")),
        TimeSpan.FromSeconds(BuiltInDeviceProfiles.Parameter(behavior, "accelerationSeconds")),
        BuiltInDeviceProfiles.Parameter(behavior, "ratedCurrent"),
        BuiltInDeviceProfiles.Parameter(behavior, "heatingRatePerSecond"),
        BuiltInDeviceProfiles.Parameter(behavior, "coolingRatePerSecond"),
        BuiltInDeviceProfiles.Parameter(behavior, "overheatTemperature")));

    private static Sensor CreateSensor(StateStore state, DeviceBehaviorDefinition behavior) => new(
        state,
        new SensorParameters(BuiltInDeviceProfiles.Parameter(behavior, "ratePerSecond")));
}
