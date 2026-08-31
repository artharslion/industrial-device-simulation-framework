using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Protocols.OpcUa;

namespace IndustrialSim.Protocols.OpcUa.Tests;

public class ProtocolContractTests
{
    [Fact]
    public async Task Runtime_contract_supports_state_reads_writes_commands_and_events()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("pump-001"), "pump", new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) }, new[] { new CommandDefinition("start") }));
        var changes = 0; runtime.State.DataPointChanged += _ => changes++;
        Assert.Equal(0, runtime.Read("speed")!.Value);
        Assert.True(runtime.Write("speed", 12).Succeeded);
        await runtime.InvokeCommandAsync("start");
        Assert.Equal(1, runtime.CommandsInvoked);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task OpcUa_maps_nodes_and_methods_to_runtime()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("pump-001"), "pump", new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) }, new[] { new CommandDefinition("start") }));
        var adapter = new OpcUaAdapter(); await adapter.StartAsync(runtime, new ProtocolOptions()); adapter.Write("pump-001/speed", 8); Assert.Equal(8, adapter.Read("pump-001/speed")); await adapter.InvokeMethodAsync("pump-001/start"); Assert.Equal(1, runtime.CommandsInvoked);
    }

    [Fact]
    public async Task Transport_disconnect_does_not_stop_runtime()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("p"), "pump", new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.Read, 1) }));
        var adapter = new OpcUaAdapter(); await adapter.StartAsync(runtime, new ProtocolOptions()); adapter.ApplyTransportFault("disconnect", TimeSpan.Zero);
        Assert.Throws<IOException>(() => adapter.Read("speed")); Assert.True(adapter.IsRunning); adapter.RecoverTransportFault(); Assert.Equal(1, adapter.Read("speed"));
    }

    [Fact]
    public void Raw_listener_is_not_claimed_as_standard_opcua_server()
    {
        Assert.False(new OpcUaAdapter().IsStandardOpcUaServer);
    }
}
