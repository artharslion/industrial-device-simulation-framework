using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.Engine;
using IndustrialSim.Runtime.Time;

namespace IndustrialSim.Runtime.Tests;

public class SimulationEngineTests
{
    [Fact]
    public async Task Lifecycle_supports_start_pause_resume_stop_and_reset()
    {
        var clock = new DeterministicClock();
        var engine = new SimulationEngine(clock);

        Assert.Equal(EngineState.Stopped, engine.State);
        await engine.StartAsync();
        Assert.Equal(EngineState.Running, engine.State);
        engine.Pause();
        Assert.Equal(EngineState.Paused, engine.State);
        engine.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
        await engine.StartAsync();
        engine.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Elapsed);
        await engine.StopAsync();
        Assert.Equal(EngineState.Stopped, engine.State);
        engine.Reset();
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public async Task Scheduled_callbacks_run_in_stable_time_order()
    {
        var clock = new DeterministicClock();
        var engine = new SimulationEngine(clock);
        var calls = new List<string>();
        engine.Schedule(SimulationTime.FromSeconds(2), () => calls.Add("late"));
        engine.Schedule(SimulationTime.FromSeconds(1), () => calls.Add("first"));
        engine.Schedule(SimulationTime.FromSeconds(1), () => calls.Add("same-time"));
        await engine.StartAsync();

        engine.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { "first", "same-time" }, calls);
        engine.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { "first", "same-time", "late" }, calls);
    }

    [Fact]
    public async Task Callback_failure_isolated_from_engine_loop()
    {
        var engine = new SimulationEngine(new DeterministicClock());
        var errors = 0;
        engine.CallbackFailed += (_, _) => errors++;
        engine.Schedule(SimulationTime.FromSeconds(1), () => throw new InvalidOperationException("adapter down"));
        engine.Schedule(SimulationTime.FromSeconds(1), () => { });
        await engine.StartAsync();

        engine.Tick(TimeSpan.FromSeconds(1));

        Assert.Equal(EngineState.Running, engine.State);
        Assert.Equal(1, errors);
    }
}
