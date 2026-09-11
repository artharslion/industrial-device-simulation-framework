using IndustrialSim.Configuration;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Templates;

namespace IndustrialSim.Application.Templates;

public sealed record TemplateProtocolSelection(string Protocol, string ProfileName, int Port);

public static class TemplateLaunchComposer
{
    public static DeviceLaunchDefinition Compose(
        DeviceTemplateDocument template,
        IReadOnlyList<ProtocolMappingProfile> profiles,
        string deviceId,
        SimulationHostOptions options,
        IReadOnlyList<TemplateProtocolSelection> selections)
    {
        var definition = TemplateCatalog.Instantiate(template, deviceId);
        OpcUaLaunchDefinition? opcUa = null;
        ModbusLaunchDefinition? modbus = null;
        var references = new List<MappingProfileReference>();

        foreach (var selection in selections)
        {
            var profile = profiles.SingleOrDefault(item =>
                item.Protocol.Equals(selection.Protocol, StringComparison.OrdinalIgnoreCase) &&
                item.Name.Equals(selection.ProfileName, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
                throw new DeviceLaunchException($"Mapping profile '{selection.Protocol}/{selection.ProfileName}' was not found for template {template.Id}@{template.Version}.", "mappingProfileInvalid");
            references.Add(new MappingProfileReference(profile.Protocol.ToLowerInvariant(), profile.Name));
            switch (profile.Protocol.ToLowerInvariant())
            {
                case "opcua":
                    if (opcUa is not null) throw new DeviceLaunchException("Only one OPC UA mapping profile may be selected.", "mappingProfileInvalid");
                    opcUa = new OpcUaLaunchDefinition($"opc.tcp://0.0.0.0:{selection.Port}", CompileOpcUa(profile, definition));
                    break;
                case "modbus":
                    if (modbus is not null) throw new DeviceLaunchException("Only one Modbus mapping profile may be selected.", "mappingProfileInvalid");
                    modbus = new ModbusLaunchDefinition(selection.Port, CompileModbus(profile, definition));
                    break;
                default:
                    throw new DeviceLaunchException($"Protocol '{profile.Protocol}' is not supported.", "unknownProtocol");
            }
        }

        return new DeviceLaunchDefinition(
            definition,
            options,
            opcUa,
            modbus,
            new DeviceLaunchSource("template", template.Id, template.Version, references));
    }

    private static IReadOnlyDictionary<string, string> CompileOpcUa(ProtocolMappingProfile profile, DeviceDefinition definition)
    {
        var points = definition.DataPoints.Select(point => point.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in profile.Entries)
        {
            if (!points.Contains(entry.DataPoint)) throw new DeviceLaunchException($"OPC UA mapping targets unknown data point '{entry.DataPoint}'.", "mappingProfileInvalid");
            if (!mappings.TryAdd(entry.DataPoint, entry.Address)) throw new DeviceLaunchException($"OPC UA mapping repeats data point '{entry.DataPoint}'.", "mappingProfileInvalid");
        }
        if (mappings.Values.Distinct(StringComparer.Ordinal).Count() != mappings.Count)
            throw new DeviceLaunchException("OPC UA mapping contains duplicate node ids.", "mappingProfileInvalid");
        return mappings;
    }

    private static IReadOnlyList<ValidatedModbusMapping> CompileModbus(ProtocolMappingProfile profile, DeviceDefinition definition)
    {
        var configuration = new ModbusConfiguration { Enabled = true, Mappings = new Dictionary<string, ModbusMappingConfiguration>(StringComparer.OrdinalIgnoreCase) };
        foreach (var entry in profile.Entries)
        {
            var (kind, address) = ParseAddress(entry.Address);
            var mapping = new ModbusMappingConfiguration
            {
                Type = entry.DataType,
                ByteOrder = NormalizeOrder(entry.ByteOrder),
                WordOrder = NormalizeOrder(entry.WordOrder)
            };
            switch (kind)
            {
                case "coil": mapping.Coil = address; break;
                case "discrete": mapping.DiscreteInput = address; break;
                case "input": mapping.InputRegister = address; break;
                default: mapping.HoldingRegister = address; break;
            }
            if (!configuration.Mappings.TryAdd(entry.DataPoint, mapping))
                throw new DeviceLaunchException($"Modbus mapping repeats data point '{entry.DataPoint}'.", "mappingProfileInvalid");
        }
        try
        {
            var result = ModbusMappingValidator.Validate(configuration);
            YamlConfigurationLoader.ValidateMappings(definition, result);
            return result;
        }
        catch (ArgumentException exception)
        {
            throw new DeviceLaunchException(exception.Message, "mappingProfileInvalid");
        }
    }

    private static (string Kind, int Address) ParseAddress(string value)
    {
        var trimmed = value.Trim();
        var separator = trimmed.IndexOf(':');
        if (separator > 0)
        {
            var kind = trimmed[..separator].ToLowerInvariant();
            if (kind is not ("coil" or "discrete" or "input" or "holding") || !int.TryParse(trimmed[(separator + 1)..], out var address))
                throw new DeviceLaunchException($"Modbus address '{value}' is invalid.", "mappingProfileInvalid");
            return (kind, address);
        }
        if (trimmed.Length == 5 && int.TryParse(trimmed, out var reference) && reference % 100000 > 0)
        {
            var kind = trimmed[0] switch { '0' => "coil", '1' => "discrete", '3' => "input", '4' => "holding", _ => null };
            if (kind is not null) return (kind, int.Parse(trimmed[1..]) - 1);
        }
        throw new DeviceLaunchException($"Modbus address '{value}' must use coil:/discrete:/input:/holding: zero-based syntax or 0xxxx/1xxxx/3xxxx/4xxxx reference notation.", "mappingProfileInvalid");
    }

    private static string? NormalizeOrder(string? value) => value?.ToLowerInvariant() switch
    {
        "bigendian" or "highlow" or "big" => "big",
        "littleendian" or "lowhigh" or "little" => "little",
        null or "" => null,
        _ => value
    };
}
