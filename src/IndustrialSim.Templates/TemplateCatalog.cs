using System.Text.Json;
using System.Text.RegularExpressions;
using IndustrialSim.Core.Domain;

namespace IndustrialSim.Templates;

public static partial class TemplateCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Validate(DeviceTemplateDocument template, IReadOnlyList<ProtocolMappingProfile> mappings)
    {
        if (string.IsNullOrWhiteSpace(template.Id) || !IdPattern().IsMatch(template.Id))
            throw new TemplateValidationException("Template id must contain only lowercase letters, numbers, dots, or hyphens.");
        if (!Version.TryParse(template.Version, out var version) || version.Build < 0)
            throw new TemplateValidationException($"Template version '{template.Version}' must be a three-part semantic version.");
        if (string.IsNullOrWhiteSpace(template.DisplayName) || string.IsNullOrWhiteSpace(template.DeviceType))
            throw new TemplateValidationException("Display name and device type are required.");
        try { using var _ = JsonDocument.Parse(template.BehaviorJson); }
        catch (JsonException exception) { throw new TemplateValidationException($"Behavior JSON is invalid: {exception.Message}"); }
        if (template.DataPoints.Count == 0)
            throw new TemplateValidationException("A template requires at least one datapoint.");

        var duplicate = template.DataPoints.GroupBy(point => point.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new TemplateValidationException($"Duplicate datapoint '{duplicate.Key}'.");

        foreach (var point in template.DataPoints)
        {
            if (!Enum.TryParse<DataType>(point.DataType, true, out _))
                throw new TemplateValidationException($"Datapoint '{point.Name}' has unsupported data type '{point.DataType}'.");
            if (!Enum.TryParse<DataPointAccess>(point.Access, true, out _))
                throw new TemplateValidationException($"Datapoint '{point.Name}' has unsupported access '{point.Access}'.");
        }

        var names = template.DataPoints.Select(point => point.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (!mapping.TemplateId.Equals(template.Id, StringComparison.OrdinalIgnoreCase) || mapping.TemplateVersion != template.Version)
                throw new TemplateValidationException($"Mapping '{mapping.Name}' does not target template {template.Id}@{template.Version}.");
            if (string.IsNullOrWhiteSpace(mapping.Protocol) || string.IsNullOrWhiteSpace(mapping.Name))
                throw new TemplateValidationException("Mapping protocol and name are required.");
            var duplicateAddress = mapping.Entries.GroupBy(entry => entry.Address, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateAddress is not null)
                throw new TemplateValidationException($"Mapping '{mapping.Name}' contains duplicate address '{duplicateAddress.Key}'.");
            foreach (var entry in mapping.Entries)
            {
                if (!names.Contains(entry.DataPoint))
                    throw new TemplateValidationException($"Mapping '{mapping.Name}' references unknown datapoint '{entry.DataPoint}'.");
                if (string.IsNullOrWhiteSpace(entry.Address))
                    throw new TemplateValidationException($"Mapping for '{entry.DataPoint}' requires an address.");
            }
        }
    }

    public static DeviceDefinition Instantiate(DeviceTemplateDocument template, string deviceId)
    {
        Validate(template, []);
        return new DeviceDefinition(
            new DeviceId(deviceId),
            template.DeviceType,
            template.DataPoints.Select(point => new DataPointDefinition(
                point.Name,
                Enum.Parse<DataType>(point.DataType, true),
                Enum.Parse<DataPointAccess>(point.Access, true),
                Scalar(point.Initial),
                point.Unit,
                point.Description)),
            template.Commands.Select(name => new CommandDefinition(name)),
            template.Events.Select(name => new EventDefinition(name)));
    }

    public static string Export(DeviceTemplateDocument template, IReadOnlyList<ProtocolMappingProfile> mappings)
    {
        Validate(template, mappings);
        return JsonSerializer.Serialize(new TemplatePackage(template, mappings), JsonOptions);
    }

    public static TemplatePackage Import(string json)
    {
        TemplatePackage package;
        try
        {
            package = JsonSerializer.Deserialize<TemplatePackage>(json, JsonOptions)
                ?? throw new TemplateValidationException("Template package is empty.");
        }
        catch (JsonException exception)
        {
            throw new TemplateValidationException($"Template package JSON is invalid: {exception.Message}");
        }
        Validate(package.Template, package.Mappings);
        return package;
    }

    public static IReadOnlyList<DeviceTemplateDocument> Search(
        IEnumerable<DeviceTemplateDocument> templates,
        string? query = null,
        string? tag = null) =>
        templates.Where(template =>
                (string.IsNullOrWhiteSpace(query) ||
                 template.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 template.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 template.DeviceType.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(tag) || template.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(template => template.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(template => Version.Parse(template.Version))
            .ToArray();

    private static object? Scalar(object? value) => value is JsonElement element ? element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new TemplateValidationException("Datapoint initial values must be scalar JSON values.")
    } : value;

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]*$")]
    private static partial Regex IdPattern();
}
