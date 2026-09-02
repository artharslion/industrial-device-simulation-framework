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

    [Fact]
    public void Canonical_pump_example_is_loadable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "devices", "pump.yaml");
        var loaded = new YamlConfigurationLoader().Load(File.ReadAllText(path));

        Assert.Equal("pump-001", loaded.Device.Id.Value);
        Assert.Equal(4, loaded.ModbusMappings.Count);
    }

    [Theory]
    [InlineData("motor.yaml", "motor-001", "motor", 4)]
    [InlineData("sensor.yaml", "sensor-001", "sensor", 1)]
    public void Canonical_built_in_device_examples_are_loadable(string fileName, string deviceId, string deviceType, int mappingCount)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "devices", fileName);
        var loaded = new YamlConfigurationLoader().Load(File.ReadAllText(path));

        Assert.Equal(deviceId, loaded.Device.Id.Value);
        Assert.Equal(deviceType, loaded.Device.Type);
        Assert.Equal(mappingCount, loaded.ModbusMappings.Count);
    }

    [Fact]
    public void Validates_modbus_numeric_widths_orders_and_access_contracts()
    {
        var valid = new ModbusConfiguration
        {
            Mappings = new Dictionary<string, ModbusMappingConfiguration>
            {
                ["i8"] = new() { Register = 0, Type = "int8", Access = "readwrite" },
                ["u64"] = new() { Register = 1, Type = "uint64", ByteOrder = "little", WordOrder = "little" },
                ["double"] = new() { InputRegister = 5, Type = "double", Access = "read" }
            }
        };
        var mappings = ModbusMappingValidator.Validate(valid);
        Assert.Equal(new[] { 1, 4, 4 }, mappings.Select(mapping => mapping.Width));

        Assert.Throws<ArgumentException>(() => ModbusMappingValidator.Validate(ConfigurationWith(new() { Register = 0, Type = "string" })));
        Assert.Throws<ArgumentException>(() => ModbusMappingValidator.Validate(ConfigurationWith(new() { Register = 0, Type = "uint16", Access = "invalid" })));
        Assert.Throws<ArgumentException>(() => ModbusMappingValidator.Validate(ConfigurationWith(new() { Register = 0, Type = "uint16", ByteOrder = "middle" })));
        Assert.Throws<ArgumentException>(() => ModbusMappingValidator.Validate(ConfigurationWith(new() { InputRegister = 0, Type = "uint16", Access = "write" })));
        Assert.Throws<ArgumentException>(() => ModbusMappingValidator.Validate(ConfigurationWith(new() { Register = 0, HoldingRegister = 1, Type = "uint16" })));
    }

    [Theory]
    [InlineData("missing", "uint16", "read", "unknown data point")]
    [InlineData("speed", "uint16", "readwrite", "access")]
    public void Rejects_modbus_mappings_that_do_not_match_the_device_contract(string mappingName, string mappingType, string mappingAccess, string expected)
    {
        var yaml = $$"""
            device:
              id: pump-001
              type: pump
              datapoints:
                speed: { type: int32, initial: 0, access: read }
            protocols:
              modbus:
                enabled: true
                mappings:
                  {{mappingName}}: { holdingRegister: 10, type: {{mappingType}}, access: {{mappingAccess}} }
            """;

        var exception = Assert.Throws<ArgumentException>(() => new YamlConfigurationLoader().Load(yaml));
        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("protocols:\n  modbus:\n    enabled: true\n    port: 70000", "modbus")]
    [InlineData("protocols:\n  opcua:\n    enabled: true\n    endpoint: http://localhost:4840", "OPC UA")]
    [InlineData("web:\n  enabled: true\n  port: 0", "web")]
    public void Rejects_invalid_enabled_host_endpoints_and_ports(string hostYaml, string expected)
    {
        var yaml = "device:\n  id: pump\n  type: pump\n  datapoints:\n    speed: { type: int32, initial: 0, access: readwrite }\n" + hostYaml;

        var exception = Assert.Throws<ArgumentException>(() => new YamlConfigurationLoader().Load(yaml));
        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ModbusConfiguration ConfigurationWith(ModbusMappingConfiguration mapping) => new()
    {
        Mappings = new Dictionary<string, ModbusMappingConfiguration> { ["value"] = mapping }
    };
}
