using System.Collections.Concurrent;
using IndustrialSim.Configuration;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Devices.Pump;
using IndustrialSim.Faults;
using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Protocols.Modbus;
using IndustrialSim.Protocols.OpcUa;
using IndustrialSim.Runtime.Engine;
using IndustrialSim.Runtime.State;
using IndustrialSim.Runtime.Time;
using IndustrialSim.Scenarios;

namespace IndustrialSim.Hosting;

public sealed record SimulationHostOptions(bool Deterministic = false, int Seed = 0);

public sealed class SimulationHost : IAsyncDisposable
{
    private readonly LoadedConfiguration _configuration;
    private readonly Dictionary<string, IProtocolAdapter> _protocols = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<object> _events = new();
    private readonly Dictionary<string, object?> _faultPreviousValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataFaultProcessor> _dataFaultProcessors = new(StringComparer.OrdinalIgnoreCase);
    private readonly DeviceFaultController _deviceFaultController;
    private readonly SimulationHostOptions _options;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private Pump? _pump;
    private TimeSpan _lastBehaviorTime;
    private bool _disposed;

    private SimulationHost(LoadedConfiguration configuration, SimulationHostOptions options)
    {
        _configuration = configuration;
        _options = options;
        Engine = new SimulationEngine(options.Deterministic ? new DeterministicClock() : new RealTimeClock());
        var state = new StateStore(configuration.Device);
        var commandHandlers = new Dictionary<string, Func<CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase);
        if (CanAttachPump(configuration.Device))
        {
            _pump = new Pump(state);
            commandHandlers["start"] = _ => { _pump.Start(Engine.CurrentTime); return Task.CompletedTask; };
            commandHandlers["stop"] = _ => { _pump.Stop(Engine.CurrentTime); return Task.CompletedTask; };
        }
        Runtime = new InMemoryDeviceRuntime(configuration.Device, state, commandHandlers);
        FaultManager = new FaultManager(Engine);
        _deviceFaultController = new DeviceFaultController(Runtime.State);
        Runtime.State.DataPointChanged += change => _events.Enqueue(change);
        FaultManager.LifecycleChanged += OnFaultLifecycleChanged;

        if (configuration.Configuration.Protocols?.Opcua?.Enabled == true)
            _protocols.Add("opcua", new OpcUaAdapter());
        if (configuration.Configuration.Protocols?.Modbus?.Enabled == true)
        {
            var modbus = new ModbusAdapter();
            modbus.Configure(configuration.ModbusMappings);
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
    public bool IsRunning { get; private set; }
    public bool IsDeterministic => _options.Deterministic;
    public int Seed => _options.Seed;

    public static Task<SimulationHost> LoadAsync(string path, CancellationToken cancellationToken = default) => LoadAsync(path, new SimulationHostOptions(), cancellationToken);

    public static async Task<SimulationHost> LoadAsync(string path, SimulationHostOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Configuration path cannot be blank.", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException($"Device configuration file '{path}' was not found.", path);
        var yaml = await File.ReadAllTextAsync(path, cancellationToken);
        return new SimulationHost(new YamlConfigurationLoader().Load(yaml), options);
    }

    public static SimulationHost Create(DeviceDefinition definition, SimulationHostOptions? options = null) => new(new LoadedConfiguration(new RootConfiguration(), definition, []), options ?? new SimulationHostOptions());

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
                switch (name)
                {
                    case "opcua":
                        var endpoint = _configuration.Configuration.Protocols?.Opcua?.Endpoint ?? "opc.tcp://0.0.0.0:4840";
                        var opcPort = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Port > 0 ? uri.Port : 4840;
                        await protocol.StartAsync(Runtime, new ProtocolOptions(endpoint, opcPort), cancellationToken);
                        break;
                    case "modbus":
                        var modbusPort = _configuration.Configuration.Protocols?.Modbus?.Port ?? 5020;
                        await protocol.StartAsync(Runtime, new ProtocolOptions(Port: modbusPort), cancellationToken);
                        if (modbusPort == 0) await ((ModbusAdapter)protocol).StartServerAsync(0, cancellationToken);
                        break;
                }
                started.Add(protocol);
            }
            IsRunning = true;
            if (!IsDeterministic) StartRealTimeLoop();
        }
        catch
        {
            foreach (var protocol in started.AsEnumerable().Reverse()) await protocol.StopAsync(CancellationToken.None);
            await Engine.StopAsync(CancellationToken.None);
            throw;
        }
    }

    public ScenarioRunner RunScenario(string yaml)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var scenario = new ScenarioParser().Parse(yaml);
        ScenarioRunner?.Stop();
        ActiveScenarioName = scenario.Name;
        ScenarioRunner = new ScenarioRunner(
            scenario,
            Engine,
            State,
            command: (_, command) => Runtime.InvokeCommandAsync(command).GetAwaiter().GetResult(),
            faultAction: ScheduleScenarioFault);
        ScenarioRunner.Start();
        return ScenarioRunner;
    }

    public void Tick(TimeSpan amount)
    {
        if (Engine.State != EngineState.Running) return;
        Engine.Tick(amount);
        UpdateDeviceBehavior(amount);
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
        if (_pump is not null) _pump = new Pump(State);
    }

    public void ScheduleFault(FaultSpec fault) => FaultManager.Schedule(fault);

    public void ActivateFault(FaultSpec fault) => FaultManager.Activate(fault);

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
        foreach (var protocol in _protocols.Values.Reverse())
            if (protocol.IsRunning) await protocol.StopAsync(cancellationToken);
        await Engine.StopAsync(cancellationToken);
        IsRunning = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync(CancellationToken.None);
        _disposed = true;
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
        FaultManager.Schedule(new FaultSpec(
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
        _events.Enqueue(change);
        if (change.Lifecycle == FaultLifecycle.Active) ApplyFault(change.Fault);
        else if (change.Lifecycle == FaultLifecycle.Recovered) RecoverFaultEffect(change.Fault);
    }

    private void ApplyFault(FaultSpec fault)
    {
        switch (fault.Category)
        {
            case FaultCategory.Data:
                if (string.IsNullOrWhiteSpace(fault.Target)) throw new ArgumentException($"Data fault '{fault.Id}' requires a datapoint target.");
                var current = State.Get(new DataPointId(fault.Target))?.Value;
                _faultPreviousValues[fault.Id] = current;
                if (!Enum.TryParse<DataFaultType>(fault.Type, true, out var dataType)) throw new ArgumentException($"Data fault '{fault.Id}' has unsupported type '{fault.Type}'.");
                var seed = fault.Metadata is not null && fault.Metadata.ContainsKey("seed") ? MetadataInt(fault, "seed") : Seed;
                var parameter = MetadataDouble(fault, "parameter");
                var processor = new DataFaultProcessor(seed);
                _dataFaultProcessors[fault.Id] = processor;
                State.SetInternal(new DataPointId(fault.Target), processor.Apply(dataType, current, parameter), Engine.CurrentTime);
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
                if (_faultPreviousValues.Remove(fault.Id, out var previous)) State.SetInternal(new DataPointId(fault.Target), previous, Engine.CurrentTime);
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
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void UpdateDeviceBehavior(TimeSpan elapsed)
    {
        if (_pump is not null && elapsed >= TimeSpan.Zero) _pump.Update(elapsed, Engine.CurrentTime);
    }

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
}
