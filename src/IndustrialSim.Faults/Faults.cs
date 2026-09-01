using System.Globalization;
using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.Engine;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Faults;

public enum FaultCategory { Data, Device, Network }
public enum FaultLifecycle { Scheduled, Active, Recovered }
public sealed record FaultSpec(string Id, FaultCategory Category, string Device, string? Target, TimeSpan Start, TimeSpan? Duration = null, string? Type = null, IReadOnlyDictionary<string, string>? Metadata = null);
public sealed record FaultEvent(FaultSpec Fault, FaultLifecycle Lifecycle, SimulationTime Timestamp);

public sealed class FaultManager
{
    private readonly SimulationEngine _engine;
    public FaultManager(SimulationEngine engine) => _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    public event Action<FaultEvent>? LifecycleChanged;
    private readonly Dictionary<string, FaultSpec> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    public IReadOnlyCollection<FaultSpec> ActiveFaults { get { lock (_gate) return _active.Values.ToArray(); } }
    public void Schedule(FaultSpec fault)
    {
        Validate(fault);
        Emit(fault, FaultLifecycle.Scheduled, SimulationTime.Zero);
        _engine.Schedule(new SimulationTime(fault.Start), () => ActivateCore(fault));
    }

    public void Activate(FaultSpec fault)
    {
        Validate(fault);
        Emit(fault, FaultLifecycle.Scheduled, _engine.CurrentTime);
        ActivateCore(fault);
    }

    public bool Recover(string id)
    {
        FaultSpec? fault;
        lock (_gate) { if (!_active.Remove(id, out fault)) return false; }
        Emit(fault, FaultLifecycle.Recovered, _engine.CurrentTime);
        return true;
    }

    private void ActivateCore(FaultSpec fault)
    {
        lock (_gate)
        {
            if (_active.ContainsKey(fault.Id)) throw new InvalidOperationException($"Fault '{fault.Id}' is already active.");
            _active[fault.Id] = fault;
        }
        Emit(fault, FaultLifecycle.Active, _engine.CurrentTime);
        if (fault.Duration is { } duration) _engine.Schedule(new SimulationTime(_engine.CurrentTime.Elapsed + duration), () => Recover(fault.Id));
    }

    private static void Validate(FaultSpec fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        if (string.IsNullOrWhiteSpace(fault.Id)) throw new ArgumentException("Fault id cannot be blank.");
        if (fault.Start < TimeSpan.Zero || fault.Duration < TimeSpan.Zero) throw new ArgumentException("Fault times cannot be negative.");
    }
    private void Emit(FaultSpec fault, FaultLifecycle lifecycle, SimulationTime time) => LifecycleChanged?.Invoke(new FaultEvent(fault, lifecycle, time));
}

public enum DataFaultType { Stale, Freeze, OutOfRange, Noise, Spike }
public sealed class DataFaultProcessor
{
    private readonly Random _random;
    private object? _frozen;
    public DataFaultProcessor(int seed = 0) => _random = new Random(seed);
    public object? Apply(DataFaultType type, object? value, double parameter = 0)
    {
        return type switch
        {
            DataFaultType.Stale => _frozen ??= value,
            DataFaultType.Freeze => _frozen ??= value,
            DataFaultType.OutOfRange => value is IConvertible ? Convert.ToDouble(value, CultureInfo.InvariantCulture) > parameter ? parameter : value : value,
            DataFaultType.Noise => value is IConvertible ? Convert.ToDouble(value, CultureInfo.InvariantCulture) + (_random.NextDouble() * 2 - 1) * parameter : value,
            DataFaultType.Spike => value is IConvertible ? Convert.ToDouble(value, CultureInfo.InvariantCulture) + parameter : value,
            _ => value
        };
    }
    public void Recover() => _frozen = null;
}

public enum DeviceFaultType { SensorFailure, Overheat, PowerLoss, EmergencyStop }
public sealed class DeviceFaultController
{
    private readonly StateStore _state;
    public DeviceFaultController(StateStore state) => _state = state ?? throw new ArgumentNullException(nameof(state));
    public void Activate(DeviceFaultType type, SimulationTime? timestamp = null)
    {
        switch (type)
        {
            case DeviceFaultType.SensorFailure: _state.SetInternal(new DataPointId("alarm"), true, timestamp); break;
            case DeviceFaultType.Overheat: _state.SetInternal(new DataPointId("alarm"), true, timestamp); break;
            case DeviceFaultType.PowerLoss:
            case DeviceFaultType.EmergencyStop: _state.SetInternal(new DataPointId("running"), false, timestamp); break;
        }
    }
    public void Recover(DeviceFaultType type, SimulationTime? timestamp = null) { if (type is DeviceFaultType.SensorFailure or DeviceFaultType.Overheat) _state.SetInternal(new DataPointId("alarm"), false, timestamp); }
}

public enum NetworkFaultType { Disconnect, Timeout, Latency }
public sealed class NetworkFaultController
{
    private readonly object _gate = new();
    public bool Disconnected { get; private set; }
    public TimeSpan Timeout { get; private set; }
    public TimeSpan Latency { get; private set; }
    public void Activate(NetworkFaultType type, TimeSpan duration) { lock (_gate) { if (type == NetworkFaultType.Disconnect) Disconnected = true; if (type == NetworkFaultType.Timeout) Timeout = duration; if (type == NetworkFaultType.Latency) Latency = duration; } }
    public void Recover() { lock (_gate) { Disconnected = false; Timeout = TimeSpan.Zero; Latency = TimeSpan.Zero; } }
}
