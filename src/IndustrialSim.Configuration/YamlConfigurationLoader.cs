using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace IndustrialSim.Configuration;

public sealed record LoadedConfiguration(RootConfiguration Configuration, DeviceDefinition Device, IReadOnlyList<ValidatedModbusMapping> ModbusMappings);

public sealed class YamlConfigurationLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();

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
        ValidateHostConfiguration(configuration);
        ValidateMappings(definition, mappings);
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

    private static void ValidateHostConfiguration(RootConfiguration configuration)
    {
        if (configuration.Protocols?.Modbus is { Enabled: true } modbus && modbus.Port is < 1 or > 65535)
            throw new ArgumentException("Enabled Modbus port must be between 1 and 65535.");
        if (configuration.Web is { Enabled: true } web && web.Port is < 1 or > 65535)
            throw new ArgumentException("Enabled Web port must be between 1 and 65535.");
        if (configuration.Protocols?.Opcua is { Enabled: true, Endpoint: { Length: > 0 } endpoint })
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("opc.tcp", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(uri.Host) || uri.Port is < 1 or > 65535)
                throw new ArgumentException($"Enabled OPC UA endpoint '{endpoint}' must be an absolute opc.tcp URI with a valid host and port.");
        }
    }

    private static void ValidateMappings(DeviceDefinition definition, IReadOnlyList<ValidatedModbusMapping> mappings)
    {
        var points = definition.DataPoints.ToDictionary(point => point.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (!points.TryGetValue(mapping.Name, out var point))
                throw new ArgumentException($"Modbus mapping '{mapping.Name}' targets an unknown data point.");
            var bitMapping = mapping.Kind is "coil" or "discrete";
            if (bitMapping != (point.DataType == DataType.Boolean))
                throw new ArgumentException($"Modbus mapping '{mapping.Name}' type is incompatible with data point type {point.DataType}.");
            if (mapping.Access is null) continue;
            var reads = mapping.Access is "read" or "readwrite";
            var writes = mapping.Access is "write" or "readwrite";
            if ((reads && point.Access == DataPointAccess.Write) || (writes && point.Access == DataPointAccess.Read))
                throw new ArgumentException($"Modbus mapping '{mapping.Name}' access '{mapping.Access}' is incompatible with data point access '{point.Access}'.");
        }
    }
}
