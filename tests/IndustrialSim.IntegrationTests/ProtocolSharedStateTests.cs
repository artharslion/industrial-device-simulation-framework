using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Protocols.Modbus;
using IndustrialSim.Protocols.OpcUa;

namespace IndustrialSim.IntegrationTests;

public class ProtocolSharedStateTests
{
    [Fact]
    public async Task OpcUa_and_Modbus_observe_one_runtime_state()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("pump-001"), "pump", new[] { new DataPointDefinition("speed", DataType.UInt16, DataPointAccess.ReadWrite, (ushort)0) }));
        var opc = new OpcUaAdapter(); var modbus = new ModbusAdapter(); modbus.Configure(new[] { new ValidatedModbusMapping("speed", 100, 1, "register", "uint16", null, null, null) });
        await opc.StartAsync(runtime, new ProtocolOptions()); await modbus.StartAsync(runtime, new ProtocolOptions()); opc.Write("pump-001/speed", (ushort)321);
        Assert.Equal((ushort)321, modbus.Read("speed")); Assert.Equal(new byte[] { 1, 65 }, modbus.ReadRegisters(100, 1));
    }
}
