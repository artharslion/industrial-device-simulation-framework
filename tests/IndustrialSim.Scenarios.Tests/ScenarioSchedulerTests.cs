using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.Engine;
using IndustrialSim.Runtime.State;
using IndustrialSim.Runtime.Time;
using IndustrialSim.Scenarios;

namespace IndustrialSim.Scenarios.Tests;

public class ScenarioSchedulerTests
{
    [Fact]
    public async Task Stopped_scenario_does_not_execute_pending_actions()
    {
        var definition = new DeviceDefinition(
            new DeviceId("pump-001"),
            "pump",
            new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) },
            new[] { new CommandDefinition("start"), new CommandDefinition("pulse") });
        var engine = new SimulationEngine(new DeterministicClock());
        var state = new StateStore(definition);
        var scenario = new ScenarioDefinition("stoppable", new[] { new ScenarioStep(new AtTrigger(TimeSpan.FromSeconds(1)), new SetAction("pump-001", "speed", 10)) });
        var runner = new ScenarioRunner(scenario, engine, state);
        await engine.StartAsync();
        runner.Start();
        runner.Stop();
        engine.Tick(TimeSpan.FromSeconds(1));
        Assert.False(runner.IsRunning);
        Assert.Equal(0, state.Get(new DataPointId("speed"))!.Value);
    }
    [Fact]
    public async Task Every_is_anchor_based_and_preserves_step_order()
    {
        var definition = new DeviceDefinition(
            new DeviceId("pump-001"),
            "pump",
            new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) },
            new[] { new CommandDefinition("start"), new CommandDefinition("pulse") });
        var clock = new DeterministicClock();
        var engine = new SimulationEngine(clock);
        var calls = new List<string>();
        var scenario = new ScenarioDefinition("test", new ScenarioStep[]
        {
            new(new AtTrigger(TimeSpan.FromSeconds(1)), new CommandAction("pump-001", "start")),
            new(new EveryTrigger(TimeSpan.FromSeconds(2)), new CommandAction("pump-001", "pulse"))
        });
        var runner = new ScenarioRunner(scenario, engine, new StateStore(definition), (_, name) => calls.Add(name));
        await engine.StartAsync(); runner.Start();
        engine.Tick(TimeSpan.FromSeconds(1));
        engine.Tick(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "start", "pulse", "pulse", "pulse" }, calls);
    }

    [Fact]
    public async Task Scenario_rejects_wrong_device_and_missing_references_before_scheduling()
    {
        var definition = new DeviceDefinition(
            new DeviceId("pump-001"),
            "pump",
            new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) },
            new[] { new CommandDefinition("start") });
        var engine = new SimulationEngine(new DeterministicClock());
        var state = new StateStore(definition);
        await engine.StartAsync();

        var wrongDevice = new ScenarioRunner(
            new ScenarioDefinition("wrong-device", new[]
            {
                new ScenarioStep(new AtTrigger(TimeSpan.Zero), new SetAction("other-device", "speed", 10))
            }),
            engine,
            state);
        Assert.Throws<ArgumentException>(() => wrongDevice.Start());

        var missingCommand = new ScenarioRunner(
            new ScenarioDefinition("missing-command", new[]
            {
                new ScenarioStep(new AtTrigger(TimeSpan.Zero), new CommandAction("pump-001", "missing"))
            }),
            engine,
            state);
        Assert.Throws<ArgumentException>(() => missingCommand.Start());

        engine.Tick(TimeSpan.Zero);
        Assert.Equal(0, state.Get(new DataPointId("speed"))!.Value);
    }

    [Fact]
    public async Task Wait_offsets_subsequent_after_actions()
    {
        var definition = new DeviceDefinition(
            new DeviceId("pump-001"),
            "pump",
            new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) });
        var engine = new SimulationEngine(new DeterministicClock());
        var state = new StateStore(definition);
        var scenario = new ScenarioDefinition("wait", new ScenarioStep[]
        {
            new(new AtTrigger(TimeSpan.Zero), new SetAction("pump-001", "speed", 1)),
            new(new AfterTrigger(TimeSpan.Zero), new WaitAction(TimeSpan.FromSeconds(2))),
            new(new AfterTrigger(TimeSpan.Zero), new SetAction("pump-001", "speed", 2))
        });
        var runner = new ScenarioRunner(scenario, engine, state);
        await engine.StartAsync();
        runner.Start();

        engine.Tick(TimeSpan.Zero);
        Assert.Equal(1, state.Get(new DataPointId("speed"))!.Value);
        engine.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(1, state.Get(new DataPointId("speed"))!.Value);
        engine.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(2, state.Get(new DataPointId("speed"))!.Value);
    }
}
