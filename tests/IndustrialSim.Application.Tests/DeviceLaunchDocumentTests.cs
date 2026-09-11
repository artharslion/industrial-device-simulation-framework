using IndustrialSim.Application.Devices;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;

namespace IndustrialSim.Application.Tests;

public sealed class DeviceLaunchDocumentTests
{
    [Fact]
    public void Complete_launch_document_round_trips_all_host_semantics()
    {
        var launch = new DeviceLaunchDefinition(
            new DeviceDefinition(
                new DeviceId("pump-doc"),
                "pump",
                [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)],
                [new CommandDefinition("start")],
                [new EventDefinition("started")],
                new DeviceBehaviorDefinition("none")),
            new SimulationHostOptions(true, 42),
            new OpcUaLaunchDefinition("opc.tcp://0.0.0.0:14840", new Dictionary<string, string> { ["speed"] = "line/speed" }),
            new ModbusLaunchDefinition(15020, [new ValidatedModbusMapping("speed", 100, 2, "register", "int32", "readwrite", "big", "little")]),
            new DeviceLaunchSource("template", "pump", "1.0.0", [new MappingProfileReference("modbus", "holding")]));

        var restored = DeviceLaunchDocumentSerializer.Deserialize(DeviceLaunchDocumentSerializer.Serialize(launch));

        Assert.Equal("pump-doc", restored.Definition.Id.Value);
        Assert.Equal(42, restored.Options.Seed);
        Assert.Equal("line/speed", restored.OpcUa!.DataPointNodeIds!["speed"]);
        Assert.Equal(100, restored.Modbus!.Mappings.Single().Address);
        Assert.Equal("holding", restored.Source!.MappingProfiles!.Single().Name);
    }

    [Fact]
    public void Legacy_port_reservations_do_not_enable_protocols()
    {
        const string json = """
            {"id":"legacy","type":"custom","deterministic":true,"seed":7,
             "dataPoints":[{"name":"speed","dataType":"Int32","access":"ReadWrite","initial":0}],
             "portBindings":[{"protocol":"modbus","port":15020}]}
            """;

        var restored = DeviceLaunchDocumentSerializer.Deserialize(json);

        Assert.Null(restored.OpcUa);
        Assert.Null(restored.Modbus);
        Assert.Equal("legacyQuickCreate", restored.Source!.Kind);
    }

    [Fact]
    public void Unsupported_versions_have_a_stable_error_code()
    {
        var exception = Assert.Throws<DeviceLaunchDocumentException>(() =>
            DeviceLaunchDocumentSerializer.Deserialize("{\"schemaVersion\":99}"));

        Assert.Equal("deviceLaunchDocumentUnsupported", exception.ErrorCode);
    }
}
