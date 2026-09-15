using IndustrialSim.Core.Domain;
using IndustrialSim.Devices;
using IndustrialSim.Devices.Motor;
using IndustrialSim.Devices.Pump;
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

    [Fact]
    public void Built_in_profiles_create_complete_public_contracts_and_reject_incompatible_definitions()
    {
        var pump = BuiltInDeviceProfiles.Get("pump").CreateDefinition(new DeviceId("pump-configured"));

        Assert.Equal(["start", "stop"], pump.Commands.Select(command => command.Name));
        Assert.Equal("pump", pump.Behavior!.Profile);
        Assert.Contains(BuiltInDeviceProfiles.Get("pump").Parameters, parameter => parameter.Name == "normalOperatingTemperature" && parameter.DefaultValue == 70);
        BuiltInDeviceProfiles.Validate(pump, pump.Behavior);

        var incompatible = new DeviceDefinition(
            new DeviceId("broken"),
            "pump",
            [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)],
            [new CommandDefinition("start"), new CommandDefinition("stop")],
            behavior: new DeviceBehaviorDefinition("pump"));

        var error = Assert.Throws<ArgumentException>(() => BuiltInDeviceProfiles.Validate(incompatible, incompatible.Behavior!));
        Assert.Contains("temperature", error.Message, StringComparison.OrdinalIgnoreCase);

        var unsafeThresholds = BuiltInDeviceProfiles.Get("pump").CreateDefinition(
            new DeviceId("unsafe-pump"),
            new Dictionary<string, double>
            {
                ["normalOperatingTemperature"] = 90,
                ["overheatTemperature"] = 90
            });
        var thresholdError = Assert.Throws<ArgumentException>(() => BuiltInDeviceProfiles.Validate(unsafeThresholds, unsafeThresholds.Behavior!));
        Assert.Contains("must be lower", thresholdError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Typed_behavior_parameters_change_pump_motor_and_sensor_updates()
    {
        var pumpState = new StateStore(PumpDefinition());
        var pump = new Pump(pumpState, new PumpParameters(ratedSpeed: 1000, acceleration: TimeSpan.FromSeconds(2), heatingRatePerSecond: 2));
        pump.Start();
        pump.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(500, pumpState.Get(new("speed"))!.Value);
        Assert.Equal(27d, pumpState.Get(new("temperature"))!.Value);

        var motorState = new StateStore(Motor.CreateDefinition(new DeviceId("motor-parameters")));
        var motor = new Motor(motorState, new MotorParameters(900, TimeSpan.FromSeconds(1), 6, 1, 0.2, 90));
        motor.Start();
        motor.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(900, motorState.Get(new("speed"))!.Value);
        Assert.Equal(6d, motorState.Get(new("current"))!.Value);

        var sensorState = new StateStore(Sensor.CreateDefinition(new DeviceId("sensor-parameters")));
        new Sensor(sensorState, new SensorParameters(2.5)).Update(TimeSpan.FromSeconds(2));
        Assert.Equal(5d, sensorState.Get(new("value"))!.Value);
    }

    private static DeviceDefinition PumpDefinition() => BuiltInDeviceProfiles.Get("pump").CreateDefinition(new DeviceId("pump-parameters"));
}
