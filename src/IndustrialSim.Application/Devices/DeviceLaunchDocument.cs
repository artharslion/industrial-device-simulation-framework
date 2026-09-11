namespace IndustrialSim.Application.Devices;

public sealed record DeviceLaunchDocument(
    int SchemaVersion,
    LaunchDeviceDocument Device,
    LaunchSimulationDocument Simulation,
    LaunchProtocolsDocument Protocols,
    LaunchSourceDocument Source);

public sealed record LaunchDeviceDocument(
    string Id,
    string Type,
    IReadOnlyList<LaunchDataPointDocument> DataPoints,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> Events,
    LaunchBehaviorDocument? Behavior);

public sealed record LaunchDataPointDocument(
    string Name,
    string DataType,
    string Access,
    object? Initial,
    string? Unit,
    string? Description);

public sealed record LaunchBehaviorDocument(string Profile, IReadOnlyDictionary<string, double> Parameters);
public sealed record LaunchSimulationDocument(bool Deterministic, int Seed);
public sealed record LaunchProtocolsDocument(LaunchOpcUaDocument? Opcua, LaunchModbusDocument? Modbus);
public sealed record LaunchOpcUaDocument(string Endpoint, IReadOnlyDictionary<string, string> DataPointNodeIds);
public sealed record LaunchModbusDocument(int Port, IReadOnlyList<LaunchModbusMappingDocument> Mappings);
public sealed record LaunchModbusMappingDocument(string DataPoint, int Address, int Width, string Kind, string DataType, string? Access, string? ByteOrder, string? WordOrder);
public sealed record LaunchSourceDocument(string Kind, string? TemplateId, string? TemplateVersion, IReadOnlyList<LaunchMappingProfileReferenceDocument> MappingProfiles);
public sealed record LaunchMappingProfileReferenceDocument(string Protocol, string Name);

public sealed class DeviceLaunchDocumentException(string message, string errorCode) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
