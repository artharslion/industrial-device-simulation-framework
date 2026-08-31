using IndustrialSim.Core.Domain;
using IndustrialSim.Devices.Pump;
using IndustrialSim.Runtime.Engine;
using IndustrialSim.Runtime.State;
using IndustrialSim.Runtime.Time;
using IndustrialSim.Scenarios;

namespace IndustrialSim.Scenarios.Tests;

public class ScenarioPumpTests
{
    [Fact]
    public async Task Startup_executes_command_ramp_and_publishes_state_events()
    {
        var template = new Pump(new StateStore(new DeviceDefinition(new DeviceId("pump-001"), "pump"))).Definition;
        var state = new StateStore(template);
        var pump = new Pump(state);
        var engine = new SimulationEngine(new DeterministicClock());
        var changed = new List<DataPointChanged>(); state.DataPointChanged += changed.Add;
        var scenario = new ScenarioDefinition("startup", new ScenarioStep[]
        {
            new(new AtTrigger(TimeSpan.Zero), new CommandAction("pump-001", "start")),
            new(new AfterTrigger(TimeSpan.FromSeconds(1)), new RampAction("pump-001", "speed", 0, 1450, TimeSpan.FromSeconds(1)))
        });
        var runner = new ScenarioRunner(scenario, engine, state, (_, command) => { if (command == "start") pump.Start(engine.CurrentTime); });
        await engine.StartAsync(); runner.Start(); engine.Tick(TimeSpan.FromSeconds(1)); engine.Tick(TimeSpan.FromSeconds(1));

        Assert.True((bool)state.Get(new DataPointId("running"))!.Value!);
        Assert.Equal(1450, state.Get(new DataPointId("speed"))!.Value);
        Assert.Contains(changed, e => e.DataPointId.Value == "running");
        Assert.Contains(changed, e => e.DataPointId.Value == "speed");
    }

    [Fact]
    public async Task Overheating_condition_sets_alarm()
    {
        var definition = new DeviceDefinition(new DeviceId("pump-001"), "pump", new[]
        {
            new DataPointDefinition("temperature", DataType.Double, DataPointAccess.Read, 95d),
            new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false)
        });
        var state = new StateStore(definition); var engine = new SimulationEngine(new DeterministicClock());
        var scenario = new ScenarioDefinition("overheating", new[] { new ScenarioStep(new WhenTrigger("pump-001", "temperature > 90"), new SetAction("pump-001", "alarm", true)) });
        var runner = new ScenarioRunner(scenario, engine, state);
        await engine.StartAsync(); runner.Start(); engine.Tick(TimeSpan.Zero);
        Assert.True((bool)state.Get(new DataPointId("alarm"))!.Value!);
    }
}
