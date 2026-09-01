using System.Collections.Concurrent;
using IndustrialSim.Configuration;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
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
    private bool _disposed;

    private SimulationHost(LoadedConfiguration configuration, SimulationHostOptions options)
    {
        _configuration = configuration;
        _options = options;
        Runtime = new InMemoryDeviceRuntime(configuration.Device);
        Engine = new SimulationEngine(options.Deterministic ? new DeterministicClock() : new RealTimeClock());
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
            (_, command) => Runtime.InvokeCommandAsync(command).GetAwaiter().GetResult(),
            (device, type) => ScheduleScenarioFault(device, type));
        ScenarioRunner.Start();
        return ScenarioRunner;
    }

    public void Tick(TimeSpan amount) => Engine.Tick(amount);

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

    private void ScheduleScenarioFault(string device, string type)
    {
        FaultManager.Schedule(new FaultSpec($"scenario-{Guid.NewGuid():N}", FaultCategory.Device, device, null, Engine.CurrentTime.Elapsed, Type: type));
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
        _loopCts = new CancellationTokenSource();
        _loopTask = RunRealTimeLoopAsync(_loopCts.Token);
    }

    private async Task RunRealTimeLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken)) Engine.Update();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
