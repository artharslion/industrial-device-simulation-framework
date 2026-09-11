using System.Text.Json;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;

namespace IndustrialSim.Application.Devices;

public static class DeviceLaunchDocumentSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Serialize(DeviceLaunchDefinition launch) => JsonSerializer.Serialize(ToDocument(launch), JsonOptions);

    public static DeviceLaunchDefinition Deserialize(string json, Func<string, string, DeviceDefinition?>? templateResolver = null)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.TryGetProperty("schemaVersion", out var version))
            {
                if (version.GetInt32() != 1)
                    throw new DeviceLaunchDocumentException($"Device launch document schema version {version.GetInt32()} is not supported.", "deviceLaunchDocumentUnsupported");
                var document = JsonSerializer.Deserialize<DeviceLaunchDocument>(json, JsonOptions)
                    ?? throw Invalid("Device launch document is empty.");
                return FromDocument(document);
            }
            return ReadLegacy(parsed.RootElement, templateResolver);
        }
        catch (DeviceLaunchDocumentException) { throw; }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            throw Invalid($"Device launch document is invalid: {exception.Message}");
        }
    }

    public static DeviceLaunchDocument ToDocument(DeviceLaunchDefinition launch) => new(
        1,
        new LaunchDeviceDocument(
            launch.Definition.Id.Value,
            launch.Definition.Type,
            launch.Definition.DataPoints.Select(point => new LaunchDataPointDocument(point.Name, point.DataType.ToString(), point.Access.ToString(), point.InitialValue?.Value, point.Unit, point.Description)).ToArray(),
            launch.Definition.Commands.Select(command => command.Name).ToArray(),
            launch.Definition.Events.Select(@event => @event.Name).ToArray(),
            launch.Definition.Behavior is { } behavior ? new LaunchBehaviorDocument(behavior.Profile, behavior.Parameters) : null),
        new LaunchSimulationDocument(launch.Options.Deterministic, launch.Options.Seed),
        new LaunchProtocolsDocument(
            launch.OpcUa is { } opcUa ? new LaunchOpcUaDocument(opcUa.Endpoint, opcUa.DataPointNodeIds ?? new Dictionary<string, string>()) : null,
            launch.Modbus is { } modbus ? new LaunchModbusDocument(modbus.Port, modbus.Mappings.Select(mapping => new LaunchModbusMappingDocument(mapping.Name, mapping.Address, mapping.Width, mapping.Kind, mapping.DataType, mapping.Access, mapping.ByteOrder, mapping.WordOrder)).ToArray()) : null),
        new LaunchSourceDocument(
            launch.Source?.Kind ?? "unknown",
            launch.Source?.TemplateId,
            launch.Source?.TemplateVersion,
            launch.Source?.MappingProfiles?.Select(reference => new LaunchMappingProfileReferenceDocument(reference.Protocol, reference.Name)).ToArray() ?? []));

    private static DeviceLaunchDefinition FromDocument(DeviceLaunchDocument document)
    {
        if (document.SchemaVersion != 1) throw new DeviceLaunchDocumentException($"Device launch document schema version {document.SchemaVersion} is not supported.", "deviceLaunchDocumentUnsupported");
        var definition = Definition(document.Device);
        var opcUa = document.Protocols.Opcua is { } opc
            ? new OpcUaLaunchDefinition(opc.Endpoint, new Dictionary<string, string>(opc.DataPointNodeIds, StringComparer.OrdinalIgnoreCase))
            : null;
        var modbus = document.Protocols.Modbus is { } mod
            ? new ModbusLaunchDefinition(mod.Port, mod.Mappings.Select(mapping => new ValidatedModbusMapping(mapping.DataPoint, mapping.Address, mapping.Width, mapping.Kind, mapping.DataType, mapping.Access, mapping.ByteOrder, mapping.WordOrder)).ToArray())
            : null;
        var source = new DeviceLaunchSource(document.Source.Kind, document.Source.TemplateId, document.Source.TemplateVersion,
            document.Source.MappingProfiles.Select(reference => new MappingProfileReference(reference.Protocol, reference.Name)).ToArray());
        return new DeviceLaunchDefinition(definition, new SimulationHostOptions(document.Simulation.Deterministic, document.Simulation.Seed), opcUa, modbus, source);
    }

    private static DeviceLaunchDefinition ReadLegacy(JsonElement root, Func<string, string, DeviceDefinition?>? templateResolver)
    {
        DeviceDefinition definition;
        if (root.TryGetProperty("templateId", out var templateIdNode) && root.TryGetProperty("templateVersion", out var templateVersionNode))
        {
            var templateId = templateIdNode.GetString() ?? throw Invalid("Legacy template id is missing.");
            var templateVersion = templateVersionNode.GetString() ?? throw Invalid("Legacy template version is missing.");
            definition = templateResolver?.Invoke(templateId, templateVersion)
                ?? throw Invalid($"Legacy template instance {templateId}@{templateVersion} cannot be resolved.");
            var deviceId = RequiredString(root, "id");
            definition = new DeviceDefinition(new DeviceId(deviceId), definition.Type, definition.DataPoints, definition.Commands, definition.Events, definition.Behavior);
            return new DeviceLaunchDefinition(definition, LegacyOptions(root), Source: new DeviceLaunchSource("legacyTemplate", templateId, templateVersion));
        }

        definition = Definition(new LaunchDeviceDocument(
            RequiredString(root, "id"),
            RequiredString(root, "type"),
            ReadLegacyPoints(root),
            ReadStrings(root, "commands"),
            ReadStrings(root, "events"),
            ReadBehavior(root)));
        return new DeviceLaunchDefinition(definition, LegacyOptions(root), Source: new DeviceLaunchSource("legacyQuickCreate"));
    }

    private static DeviceDefinition Definition(LaunchDeviceDocument device) => new(
        new DeviceId(device.Id),
        device.Type,
        device.DataPoints.Select(point => new DataPointDefinition(
            point.Name,
            Enum.Parse<DataType>(point.DataType, true),
            Enum.Parse<DataPointAccess>(point.Access, true),
            Scalar(point.Initial),
            point.Unit,
            point.Description)),
        device.Commands.Select(name => new CommandDefinition(name)),
        device.Events.Select(name => new EventDefinition(name)),
        device.Behavior is null ? null : new DeviceBehaviorDefinition(device.Behavior.Profile, device.Behavior.Parameters));

    private static IReadOnlyList<LaunchDataPointDocument> ReadLegacyPoints(JsonElement root)
    {
        if (!root.TryGetProperty("dataPoints", out var points) || points.ValueKind != JsonValueKind.Array) throw Invalid("Legacy device datapoints are missing.");
        return points.EnumerateArray().Select(point => new LaunchDataPointDocument(
            RequiredString(point, "name"),
            RequiredString(point, "dataType"),
            RequiredString(point, "access"),
            point.TryGetProperty("initial", out var initial) ? Scalar(initial) : null,
            OptionalString(point, "unit"),
            OptionalString(point, "description"))).ToArray();
    }

    private static LaunchBehaviorDocument? ReadBehavior(JsonElement root)
    {
        if (!root.TryGetProperty("behavior", out var behavior) || behavior.ValueKind == JsonValueKind.Null) return null;
        var parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (behavior.TryGetProperty("parameters", out var values) && values.ValueKind == JsonValueKind.Object)
            foreach (var property in values.EnumerateObject()) parameters[property.Name] = property.Value.GetDouble();
        return new LaunchBehaviorDocument(RequiredString(behavior, "profile"), parameters);
    }

    private static SimulationHostOptions LegacyOptions(JsonElement root) => new(
        root.TryGetProperty("deterministic", out var deterministic) && deterministic.GetBoolean(),
        root.TryGetProperty("seed", out var seed) ? seed.GetInt32() : 0);

    private static IReadOnlyList<string> ReadStrings(JsonElement root, string name) =>
        root.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(value => value.GetString()!).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            : [];

    private static object? Scalar(object? value) => value is JsonElement element ? element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => throw Invalid("Datapoint initial values must be scalar JSON values.")
    } : value;

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw Invalid($"Required property '{name}' is missing.");

    private static string? OptionalString(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static DeviceLaunchDocumentException Invalid(string message) => new(message, "deviceLaunchDocumentInvalid");
}
