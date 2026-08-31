using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Protocols.Modbus;

namespace IndustrialSim.Protocols.Modbus.Tests;

public class ModbusAdapterTests
{
    [Fact]
    public async Task Adapter_reads_explicit_mapping_and_shares_runtime_state()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("pump-001"), "pump", new[] { new DataPointDefinition("speed", DataType.UInt16, DataPointAccess.ReadWrite, (ushort)12) }));
        var adapter = new ModbusAdapter(); adapter.Configure(new[] { new ValidatedModbusMapping("speed", 100, 1, "register", "uint16", null, null, null) }); await adapter.StartAsync(runtime, new ProtocolOptions());
        Assert.Equal(new byte[] { 0, 12 }, adapter.ReadRegisters(100, 1)); adapter.Write("speed", (ushort)20); Assert.Equal((ushort)20, runtime.Read("speed")!.Value);
    }
}
