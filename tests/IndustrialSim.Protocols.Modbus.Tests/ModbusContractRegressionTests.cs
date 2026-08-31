using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Protocols.Modbus;

namespace IndustrialSim.Protocols.Modbus.Tests;

public class ModbusContractRegressionTests
{
    [Fact]
    public async Task Input_and_holding_registers_are_selected_by_function_code()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("p"), "pump", new[]
        {
            new DataPointDefinition("holding", DataType.UInt16, DataPointAccess.Read, (ushort)11),
            new DataPointDefinition("input", DataType.UInt16, DataPointAccess.Read, (ushort)22)
        }));
        var adapter = new ModbusAdapter(); adapter.Configure(new[]
        {
            new ValidatedModbusMapping("holding", 10, 1, "register", "uint16", null, null, null),
            new ValidatedModbusMapping("input", 10, 1, "input", "uint16", null, null, null)
        });
        await adapter.StartAsync(runtime, new ProtocolOptions());
        Assert.Equal(new byte[] { 0, 11 }, adapter.ReadRegisters(10, 1, "register"));
        Assert.Equal(new byte[] { 0, 22 }, adapter.ReadRegisters(10, 1, "input"));
    }

    [Fact]
    public async Task Read_only_mapping_rejects_protocol_writes()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("p"), "pump", new[] { new DataPointDefinition("speed", DataType.UInt16, DataPointAccess.ReadWrite, (ushort)1) }));
        var adapter = new ModbusAdapter(); adapter.Configure(new[] { new ValidatedModbusMapping("speed", 10, 1, "register", "uint16", "read", null, null) }); await adapter.StartAsync(runtime, new ProtocolOptions());
        Assert.Throws<InvalidOperationException>(() => adapter.Write("speed", (ushort)2));
    }
}
