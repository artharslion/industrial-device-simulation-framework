using IndustrialSim.Core.Domain;
using IndustrialSim.Faults;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Faults.Tests;

public class FaultBehaviorTests
{
    [Fact]
    public void Data_noise_is_seeded_and_freeze_recovers()
    {
        var a = new DataFaultProcessor(42); var b = new DataFaultProcessor(42);
        Assert.Equal(a.Apply(DataFaultType.Noise, 10d, 1), b.Apply(DataFaultType.Noise, 10d, 1));
        Assert.Equal(10d, a.Apply(DataFaultType.Freeze, 10d)); Assert.Equal(10d, a.Apply(DataFaultType.Freeze, 20d)); a.Recover(); Assert.Equal(30d, a.Apply(DataFaultType.Freeze, 30d));
    }

    [Fact]
    public void Device_fault_changes_state_and_recovers()
    {
        var state = new StateStore(new DeviceDefinition(new DeviceId("pump-001"), "pump", new[] { new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, true), new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false) }));
        var faults = new DeviceFaultController(state); faults.Activate(DeviceFaultType.EmergencyStop); Assert.False((bool)state.Get(new DataPointId("running"))!.Value!); faults.Activate(DeviceFaultType.Overheat); Assert.True((bool)state.Get(new DataPointId("alarm"))!.Value!); faults.Recover(DeviceFaultType.Overheat); Assert.False((bool)state.Get(new DataPointId("alarm"))!.Value!);
    }

    [Fact]
    public void Network_faults_are_transport_state_only()
    {
        var faults = new NetworkFaultController(); faults.Activate(NetworkFaultType.Disconnect, TimeSpan.Zero); Assert.True(faults.Disconnected); faults.Activate(NetworkFaultType.Latency, TimeSpan.FromSeconds(1)); Assert.Equal(TimeSpan.FromSeconds(1), faults.Latency); faults.Recover(); Assert.False(faults.Disconnected); Assert.Equal(TimeSpan.Zero, faults.Latency);
    }
}
