using IndustrialSim.Core.Domain;
using IndustrialSim.Devices.Pump;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Runtime.Tests;

public class PumpTests
{
    [Fact]
    public void Existing_positional_overheat_parameter_keeps_its_meaning()
    {
        var parameters = new PumpParameters(1450, TimeSpan.FromSeconds(10), 3.2, 0.5, 0.2, 80);

        Assert.Equal(80, parameters.OverheatTemperature);
        Assert.Equal(70, parameters.NormalOperatingTemperature);
    }

    [Fact]
    public void Start_and_update_produce_predictable_state()
    {
        var definition = new Pump(new StateStore(new DeviceDefinition(new DeviceId("pump-001"), "pump"))).Definition;
        var state = new StateStore(definition);
        var pump = new Pump(state);

        pump.Start();
        pump.Update(TimeSpan.FromSeconds(5));

        Assert.Equal(true, state.Get(new DataPointId("running"))!.Value);
        Assert.Equal(725, state.Get(new DataPointId("speed"))!.Value);
        Assert.Equal(1.6d, state.Get(new DataPointId("pressure"))!.Value);
        Assert.Equal(27.5d, state.Get(new DataPointId("temperature"))!.Value);
    }

    [Fact]
    public void Stop_cools_and_alarm_is_raised_at_threshold()
    {
        var definition = new Pump(new StateStore(new DeviceDefinition(new DeviceId("pump-001"), "pump"))).Definition;
        var state = new StateStore(definition);
        var pump = new Pump(state, new PumpParameters(heatingRatePerSecond: 10, overheatTemperature: 30));

        pump.Start();
        pump.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(true, state.Get(new DataPointId("alarm"))!.Value);
        pump.Stop();
        pump.Update(TimeSpan.FromSeconds(5));
        Assert.Equal(false, state.Get(new DataPointId("running"))!.Value);
        Assert.Equal(34d, state.Get(new DataPointId("temperature"))!.Value);
    }

    [Fact]
    public void Running_behavior_continues_heating_and_recalculates_speed_after_external_writes()
    {
        var template = new Pump(new StateStore(new DeviceDefinition(new DeviceId("pump-001"), "pump"))).Definition;
        var state = new StateStore(template);
        var pump = new Pump(state);

        pump.Start();
        state.SetInternal(new DataPointId("speed"), 0);
        pump.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(25.5d, state.Get(new DataPointId("temperature"))!.Value);
        Assert.Equal(145, state.Get(new DataPointId("speed"))!.Value);
    }

    [Fact]
    public void Normal_running_temperature_stabilizes_below_the_alarm_threshold()
    {
        var definition = new Pump(new StateStore(new DeviceDefinition(new DeviceId("pump-001"), "pump"))).Definition;
        var state = new StateStore(definition);
        var pump = new Pump(state);

        pump.Start();
        pump.Update(TimeSpan.FromMinutes(10));
        pump.Update(TimeSpan.FromMinutes(10));

        Assert.Equal(70d, state.Get(new DataPointId("temperature"))!.Value);
        Assert.Equal(false, state.Get(new DataPointId("alarm"))!.Value);
    }
}
