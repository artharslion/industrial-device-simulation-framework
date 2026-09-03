using System.Text.Json;

namespace IndustrialSim.Templates;

public sealed record TemplateDataPoint(
    string Name,
    string DataType,
    string Access,
    object? Initial = null,
    string? Unit = null,
    string? Description = null);

public sealed record DeviceTemplateDocument(
    string Id,
    string Version,
    string DisplayName,
    string DeviceType,
    string? Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<TemplateDataPoint> DataPoints,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> Events,
    string BehaviorJson);

public sealed record ProtocolMappingEntry(
    string DataPoint,
    string Address,
    string? DataType = null,
    string? ByteOrder = null,
    string? WordOrder = null);

public sealed record ProtocolMappingProfile(
    string TemplateId,
    string TemplateVersion,
    string Protocol,
    string Name,
    IReadOnlyList<ProtocolMappingEntry> Entries);

public sealed record TemplatePackage(
    DeviceTemplateDocument Template,
    IReadOnlyList<ProtocolMappingProfile> Mappings);

public sealed class TemplateValidationException(string message) : Exception(message);
