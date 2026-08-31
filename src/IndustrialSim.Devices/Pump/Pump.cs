using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Devices.Pump;

public sealed record PumpParameters
{
    public int RatedSpeed { get; init; } = 1450;
    public TimeSpan Acceleration { get; init; } = TimeSpan.FromSeconds(10);
    public double MaxPressure { get; init; } = 3.2;
    public double HeatingRatePerSecond { get; init; } = 0.5;
    public double CoolingRatePerSecond { get; init; } = 0.2;
    public double OverheatTemperature { get; init; } = 90;
    public PumpParameters() { }
    public PumpParameters(int ratedSpeed = 1450, TimeSpan acceleration = default, double maxPressure = 3.2, double heatingRatePerSecond = 0.5, double coolingRatePerSecond = 0.2, double overheatTemperature = 90)
    {
        RatedSpeed = ratedSpeed;
        Acceleration = acceleration == default ? TimeSpan.FromSeconds(10) : acceleration;
        MaxPressure = maxPressure;
        HeatingRatePerSecond = heatingRatePerSecond;
        CoolingRatePerSecond = coolingRatePerSecond;
        OverheatTemperature = overheatTemperature;
    }
}

public sealed class Pump
{
    private readonly PumpParameters _parameters;
    private readonly StateStore _state;
    private TimeSpan _runningFor;

    public Pump(StateStore state, PumpParameters? parameters = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _parameters = parameters ?? new PumpParameters();
    }

    public DeviceDefinition Definition => new(
        new DeviceId("pump-001"),
        "pump",
        new[]
        {
            new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0, "rpm"),
            new DataPointDefinition("temperature", DataType.Double, DataPointAccess.Read, 25d, "°C"),
            new DataPointDefinition("pressure", DataType.Double, DataPointAccess.Read, 0d, "bar"),
            new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, false),
            new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false)
        },
        new[] { new CommandDefinition("start"), new CommandDefinition("stop") },
        new[] { new EventDefinition("PumpStarted"), new EventDefinition("PumpStopped"), new EventDefinition("Overheated") });

    public StateTransitionResult Start(SimulationTime? timestamp = null)
    {
        _runningFor = TimeSpan.Zero;
        return _state.SetInternal(new DataPointId("running"), true, timestamp);
    }

    public StateTransitionResult Stop(SimulationTime? timestamp = null) =>
        _state.SetInternal(new DataPointId("running"), false, timestamp);

    public void Update(TimeSpan elapsed, SimulationTime? timestamp = null)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        var running = (bool?)_state.Get(new DataPointId("running"))?.Value ?? false;
        var speed = (int?)_state.Get(new DataPointId("speed"))?.Value ?? 0;
        var temperature = (double?)_state.Get(new DataPointId("temperature"))?.Value ?? 25d;

        if (running)
        {
            _runningFor += elapsed;
            var fraction = _parameters.Acceleration == TimeSpan.Zero ? 1d : Math.Min(1d, _runningFor.TotalSeconds / _parameters.Acceleration.TotalSeconds);
            speed = (int)Math.Round(_parameters.RatedSpeed * fraction);
            temperature += _parameters.HeatingRatePerSecond * elapsed.TotalSeconds;
        }
        else
        {
            speed = Math.Max(0, speed - (int)Math.Round(_parameters.RatedSpeed * elapsed.TotalSeconds / Math.Max(1, _parameters.Acceleration.TotalSeconds)));
            temperature = Math.Max(25d, temperature - _parameters.CoolingRatePerSecond * elapsed.TotalSeconds);
        }

        var pressure = _parameters.RatedSpeed == 0 ? 0d : _parameters.MaxPressure * speed / _parameters.RatedSpeed;
        _state.SetInternal(new DataPointId("speed"), speed, timestamp);
        _state.SetInternal(new DataPointId("temperature"), temperature, timestamp);
        _state.SetInternal(new DataPointId("pressure"), pressure, timestamp);
        _state.SetInternal(new DataPointId("alarm"), temperature >= _parameters.OverheatTemperature, timestamp);
    }
}
