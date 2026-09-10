using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Devices.Sensor;

public sealed record SensorParameters(double RatePerSecond = 1);

public sealed class Sensor
{
    private readonly SensorParameters _parameters;
    private readonly StateStore _state;

    public Sensor(StateStore state, SensorParameters? parameters = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _parameters = parameters ?? new SensorParameters();
    }

    public static DeviceDefinition CreateDefinition(DeviceId id) => BuiltInDeviceProfiles.Get("sensor").CreateDefinition(id);

    public void Update(TimeSpan elapsed, SimulationTime? timestamp = null)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (!string.Equals(Convert.ToString(_state.GetInternal(new DataPointId("quality"))?.Value), "Good", StringComparison.OrdinalIgnoreCase)) return;
        var value = Convert.ToDouble(_state.GetInternal(new DataPointId("value"))?.Value ?? 0d);
        _state.SetInternal(new DataPointId("value"), value + _parameters.RatePerSecond * elapsed.TotalSeconds, timestamp);
    }

    public void Reset(SimulationTime? timestamp = null)
    {
        _state.SetInternal(new DataPointId("value"), 0d, timestamp);
        _state.SetInternal(new DataPointId("quality"), "Good", timestamp);
    }
}
