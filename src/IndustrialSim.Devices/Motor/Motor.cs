using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Devices.Motor;

public sealed record MotorParameters
{
    public int RatedSpeed { get; init; } = 1800;
    public TimeSpan Acceleration { get; init; } = TimeSpan.FromSeconds(10);
    public double RatedCurrent { get; init; } = 12;
    public double HeatingRatePerSecond { get; init; } = 0.4;
    public double CoolingRatePerSecond { get; init; } = 0.2;
    public double OverheatTemperature { get; init; } = 90;

    public MotorParameters() { }

    public MotorParameters(int ratedSpeed, TimeSpan acceleration, double ratedCurrent, double heatingRatePerSecond, double coolingRatePerSecond, double overheatTemperature)
    {
        RatedSpeed = ratedSpeed;
        Acceleration = acceleration;
        RatedCurrent = ratedCurrent;
        HeatingRatePerSecond = heatingRatePerSecond;
        CoolingRatePerSecond = coolingRatePerSecond;
        OverheatTemperature = overheatTemperature;
    }
}

public sealed class Motor
{
    private readonly MotorParameters _parameters;
    private readonly StateStore _state;
    private TimeSpan _runningFor;

    public Motor(StateStore state, MotorParameters? parameters = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _parameters = parameters ?? new MotorParameters();
    }

    public static DeviceDefinition CreateDefinition(DeviceId id) => BuiltInDeviceProfiles.Get("motor").CreateDefinition(id);

    public StateTransitionResult Start(SimulationTime? timestamp = null)
    {
        _runningFor = TimeSpan.Zero;
        return _state.SetInternal(new DataPointId("running"), true, timestamp);
    }

    public StateTransitionResult Stop(SimulationTime? timestamp = null) => _state.SetInternal(new DataPointId("running"), false, timestamp);

    public void Update(TimeSpan elapsed, SimulationTime? timestamp = null)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        var running = Convert.ToBoolean(_state.GetInternal(new DataPointId("running"))?.Value ?? false);
        var speed = Convert.ToInt32(_state.GetInternal(new DataPointId("speed"))?.Value ?? 0);
        var temperature = Convert.ToDouble(_state.GetInternal(new DataPointId("temperature"))?.Value ?? 25d);
        if (running)
        {
            _runningFor += elapsed;
            speed = (int)Math.Round(_parameters.RatedSpeed * Math.Min(1d, _runningFor.TotalSeconds / _parameters.Acceleration.TotalSeconds));
            temperature += _parameters.HeatingRatePerSecond * elapsed.TotalSeconds;
        }
        else
        {
            speed = Math.Max(0, speed - (int)Math.Round(_parameters.RatedSpeed * elapsed.TotalSeconds / _parameters.Acceleration.TotalSeconds));
            temperature = Math.Max(25d, temperature - _parameters.CoolingRatePerSecond * elapsed.TotalSeconds);
        }
        var current = running && _parameters.RatedSpeed > 0 ? _parameters.RatedCurrent * speed / _parameters.RatedSpeed : 0d;
        _state.SetInternal(new DataPointId("speed"), speed, timestamp);
        _state.SetInternal(new DataPointId("temperature"), temperature, timestamp);
        _state.SetInternal(new DataPointId("current"), current, timestamp);
        _state.SetInternal(new DataPointId("alarm"), temperature >= _parameters.OverheatTemperature, timestamp);
    }
}
