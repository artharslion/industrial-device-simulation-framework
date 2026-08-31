using IndustrialSim.Core.Domain;
using IndustrialSim.Faults;
using IndustrialSim.Runtime.Engine;
using IndustrialSim.Runtime.Time;

namespace IndustrialSim.Faults.Tests;

public class FaultLifecycleTests
{
    [Fact]
    public async Task Fault_emits_scheduled_active_and_recovered_in_order()
    {
        var engine = new SimulationEngine(new DeterministicClock());
        var manager = new FaultManager(engine);
        var states = new List<FaultLifecycle>(); manager.LifecycleChanged += e => states.Add(e.Lifecycle);
        manager.Schedule(new FaultSpec("f1", FaultCategory.Data, "pump-001", "temperature", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        await engine.StartAsync(); engine.Tick(TimeSpan.FromSeconds(1)); engine.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { FaultLifecycle.Scheduled, FaultLifecycle.Active, FaultLifecycle.Recovered }, states);
    }
}
