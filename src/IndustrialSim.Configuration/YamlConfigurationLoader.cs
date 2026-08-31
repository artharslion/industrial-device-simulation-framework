using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace IndustrialSim.Configuration;

public sealed record LoadedConfiguration(RootConfiguration Configuration, DeviceDefinition Device, IReadOnlyList<ValidatedModbusMapping> ModbusMappings);

public sealed class YamlConfigurationLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();

    public LoadedConfiguration Load(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) throw new ArgumentException("YAML configuration cannot be empty.", nameof(yaml));
        RootConfiguration configuration;
        try { configuration = _deserializer.Deserialize<RootConfiguration>(yaml) ?? throw new ArgumentException("Configuration is empty."); }
        catch (YamlDotNet.Core.YamlException exception) { throw new ArgumentException($"Invalid YAML: {exception.Message}", nameof(yaml), exception); }
        var device = configuration.Device ?? throw new ArgumentException("Missing required 'device' section.");
        if (string.IsNullOrWhiteSpace(device.Id)) throw new ArgumentException("Missing required 'device.id'.");
        if (string.IsNullOrWhiteSpace(device.Type)) throw new ArgumentException("Missing required 'device.type'.");
        var points = (device.Datapoints ?? throw new ArgumentException("Missing required 'device.datapoints'."))
            .Select(pair => ToDataPoint(pair.Key, pair.Value)).ToArray();
        var commands = (device.Commands ?? new Dictionary<string, object?>()).Keys.Select(name => new CommandDefinition(name)).ToArray();
        var definition = new DeviceDefinition(new DeviceId(device.Id), device.Type, points, commands);
        var mappings = configuration.Protocols?.Modbus is { } modbus ? ModbusMappingValidator.Validate(modbus) : [];
        return new LoadedConfiguration(configuration, definition, mappings);
    }

    private static DataPointDefinition ToDataPoint(string name, DataPointConfiguration value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Type)) throw new ArgumentException($"Data point '{name}' is missing 'type'.");
        if (!Enum.TryParse<DataType>(value.Type, true, out var type)) throw new ArgumentException($"Data point '{name}' has unsupported type '{value.Type}'.");
        if (!Enum.TryParse<DataPointAccess>(value.Access ?? "Read", true, out var access)) throw new ArgumentException($"Data point '{name}' has invalid access.");
        return new DataPointDefinition(name, type, access, NormalizeInitial(type, value.Initial), value.Unit, value.Description);
    }

    private static object? NormalizeInitial(DataType type, object? initial)
    {
        if (initial is null) return null;
        if (type == DataType.Boolean && initial is string text && bool.TryParse(text, out var boolean)) return boolean;
        return initial;
    }
}
