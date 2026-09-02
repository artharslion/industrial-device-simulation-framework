using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Devices.Sensor;

public sealed class Sensor
{
    private readonly StateStore _state;

    public Sensor(StateStore state) => _state = state ?? throw new ArgumentNullException(nameof(state));

    public static DeviceDefinition CreateDefinition(DeviceId id) => new(
        id,
        "sensor",
        new[]
        {
            new DataPointDefinition("value", DataType.Double, DataPointAccess.ReadWrite, 0d),
            new DataPointDefinition("quality", DataType.String, DataPointAccess.Read, "Good")
        },
        new[] { new CommandDefinition("reset") });

    public void Update(TimeSpan elapsed, SimulationTime? timestamp = null)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (!string.Equals(Convert.ToString(_state.GetInternal(new DataPointId("quality"))?.Value), "Good", StringComparison.OrdinalIgnoreCase)) return;
        var value = Convert.ToDouble(_state.GetInternal(new DataPointId("value"))?.Value ?? 0d);
        _state.SetInternal(new DataPointId("value"), value + elapsed.TotalSeconds, timestamp);
    }

    public void Reset(SimulationTime? timestamp = null)
    {
        _state.SetInternal(new DataPointId("value"), 0d, timestamp);
        _state.SetInternal(new DataPointId("quality"), "Good", timestamp);
    }
}
