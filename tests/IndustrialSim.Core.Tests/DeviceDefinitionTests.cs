using IndustrialSim.Core.Domain;

namespace IndustrialSim.Core.Tests;

public class DeviceDefinitionTests
{
    [Fact]
    public void Device_definition_exposes_immutable_domain_metadata()
    {
        var definition = new DeviceDefinition(
            new DeviceId("pump-001"),
            "pump",
            new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) },
            new[] { new CommandDefinition("start") },
            new[] { new EventDefinition("started") });

        Assert.Equal("pump-001", definition.Id.Value);
        Assert.Single(definition.DataPoints);
        Assert.Equal(DataType.Int32, definition.DataPoints.Single().DataType);
        Assert.Equal(0, definition.DataPoints.Single().InitialValue!.Value);
        Assert.DoesNotContain("Address", typeof(DeviceDefinition).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void Device_definition_rejects_duplicate_names()
    {
        var points = new[]
        {
            new DataPointDefinition("speed", DataType.Int32, DataPointAccess.Read, 0),
            new DataPointDefinition("speed", DataType.Int32, DataPointAccess.Read, 0)
        };

        Assert.Throws<ArgumentException>(() => new DeviceDefinition(new DeviceId("pump"), "pump", points));
    }

    [Fact]
    public void Data_point_definition_rejects_invalid_initial_value_and_access()
    {
        Assert.Throws<ArgumentException>(() =>
            new DataPointDefinition("speed", DataType.Int32, DataPointAccess.Read, "fast"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DataPointDefinition("speed", DataType.Int32, (DataPointAccess)99, 0));
    }

    [Fact]
    public void Device_definition_rejects_blank_type_and_command_names()
    {
        Assert.Throws<ArgumentException>(() => new DeviceDefinition(new DeviceId("pump"), " "));
        Assert.Throws<ArgumentException>(() => new CommandDefinition(" "));
        Assert.Throws<ArgumentException>(() => new EventDefinition(" "));
    }
}
