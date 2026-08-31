using IndustrialSim.Configuration;
using IndustrialSim.Configuration.Models;

namespace IndustrialSim.Configuration.Tests;

public class YamlConfigurationTests
{
    private const string ValidYaml = """
device:
  id: pump-001
  type: pump
  datapoints:
    speed:
      type: int32
      initial: 0
      access: readwrite
    running:
      type: boolean
      initial: false
      access: read
  commands:
    start:
    stop:
protocols:
  modbus:
    enabled: true
    mappings:
      speed:
        register: 100
        type: uint16
""";

    [Fact]
    public void Loads_valid_pump_configuration_and_maps_domain()
    {
        var loaded = new YamlConfigurationLoader().Load(ValidYaml);
        Assert.Equal("pump-001", loaded.Device.Id.Value);
        Assert.Equal("pump", loaded.Device.Type);
        Assert.Equal(2, loaded.Device.DataPoints.Count);
        Assert.Equal(2, loaded.Device.Commands.Count);
        Assert.Single(loaded.ModbusMappings);
    }

    [Theory]
    [InlineData("device:\n  type: pump\n  datapoints: {}", "device.id")]
    [InlineData("device:\n  id: pump\n  type: pump", "datapoints")]
    [InlineData("device:\n  id: pump\n  type: pump\n  datapoints:\n    speed:\n      type: nope", "unsupported")]
    public void Rejects_invalid_configuration(string yaml, string expected)
    {
        var exception = Assert.Throws<ArgumentException>(() => new YamlConfigurationLoader().Load(yaml));
        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_overlapping_modbus_mappings()
    {
        var configuration = new ModbusConfiguration
        {
            Mappings = new Dictionary<string, ModbusMappingConfiguration>
            {
                ["a"] = new() { Register = 100, Type = "float32" },
                ["b"] = new() { Register = 101, Type = "uint16" }
            }
        };

        Assert.Throws<ArgumentException>(() => ModbusMappingValidator.Validate(configuration));
    }
}
