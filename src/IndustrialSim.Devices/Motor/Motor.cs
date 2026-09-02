using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Devices.Motor;

public sealed class Motor
{
    private readonly StateStore _state;
    private TimeSpan _runningFor;

    public Motor(StateStore state) => _state = state ?? throw new ArgumentNullException(nameof(state));

    public static DeviceDefinition CreateDefinition(DeviceId id) => new(
        id,
        "motor",
        new[]
        {
            new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0, "rpm"),
            new DataPointDefinition("temperature", DataType.Double, DataPointAccess.Read, 25d, "°C"),
            new DataPointDefinition("current", DataType.Double, DataPointAccess.Read, 0d, "A"),
            new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, false),
            new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false)
        },
        new[] { new CommandDefinition("start"), new CommandDefinition("stop") });

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
            speed = (int)Math.Round(1800 * Math.Min(1d, _runningFor.TotalSeconds / 10d));
            temperature += 0.4d * elapsed.TotalSeconds;
        }
        else
        {
            speed = Math.Max(0, speed - (int)Math.Round(180d * elapsed.TotalSeconds));
            temperature = Math.Max(25d, temperature - 0.2d * elapsed.TotalSeconds);
        }
        var current = running ? 12d * speed / 1800d : 0d;
        _state.SetInternal(new DataPointId("speed"), speed, timestamp);
        _state.SetInternal(new DataPointId("temperature"), temperature, timestamp);
        _state.SetInternal(new DataPointId("current"), current, timestamp);
        _state.SetInternal(new DataPointId("alarm"), temperature >= 90d, timestamp);
    }
}
