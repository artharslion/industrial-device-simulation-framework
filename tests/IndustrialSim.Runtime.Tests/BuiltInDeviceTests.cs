using IndustrialSim.Core.Domain;
using IndustrialSim.Devices.Motor;
using IndustrialSim.Devices.Sensor;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Runtime.Tests;

public sealed class BuiltInDeviceTests
{
    [Fact]
    public void Motor_start_stop_and_update_drive_documented_state()
    {
        var template = Motor.CreateDefinition(new DeviceId("motor-001"));
        var state = new StateStore(template);
        var motor = new Motor(state);

        motor.Start(SimulationTime.Zero);
        motor.Update(TimeSpan.FromSeconds(5), SimulationTime.FromSeconds(5));

        Assert.True(Convert.ToBoolean(state.Get(new("running"))!.Value));
        Assert.True(Convert.ToInt32(state.Get(new("speed"))!.Value) > 0);
        Assert.True(Convert.ToDouble(state.Get(new("current"))!.Value) > 0);
        Assert.True(Convert.ToDouble(state.Get(new("temperature"))!.Value) > 25);
        motor.Stop(SimulationTime.FromSeconds(5));
        Assert.False(Convert.ToBoolean(state.Get(new("running"))!.Value));
    }

    [Fact]
    public void Sensor_updates_deterministically_and_reset_restores_value_and_quality()
    {
        var template = Sensor.CreateDefinition(new DeviceId("sensor-001"));
        var state = new StateStore(template);
        var sensor = new Sensor(state);

        sensor.Update(TimeSpan.FromSeconds(2), SimulationTime.FromSeconds(2));
        Assert.Equal(2d, state.Get(new("value"))!.Value);
        state.SetInternal(new("quality"), "Bad", SimulationTime.FromSeconds(2));
        sensor.Reset(SimulationTime.FromSeconds(3));

        Assert.Equal(0d, state.Get(new("value"))!.Value);
        Assert.Equal("Good", state.Get(new("quality"))!.Value);
    }
}
