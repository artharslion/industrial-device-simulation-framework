using System.Text.Json;

namespace IndustrialSim.Web.Api.V1;

public sealed record CreateDataPointRequest(string Name, string DataType, string Access, JsonElement? Initial = null, string? Unit = null, string? Description = null);
public sealed record CreateBehaviorRequest(string Profile, IReadOnlyDictionary<string, double>? Parameters = null);
public sealed record CreateDeviceRequest(
    string Id,
    string Type,
    IReadOnlyList<CreateDataPointRequest> DataPoints,
    bool Deterministic = false,
    int Seed = 0,
    IReadOnlyList<CreatePortBindingRequest>? PortBindings = null,
    long Version = 0,
    IReadOnlyList<string>? Commands = null,
    IReadOnlyList<string>? Events = null,
    CreateBehaviorRequest? Behavior = null,
    DeviceProtocolsRequest? Protocols = null);
public sealed record CreatePortBindingRequest(string Protocol, int Port);
public sealed record DeviceProtocolsRequest(OpcUaProtocolRequest? Opcua = null, ModbusProtocolRequest? Modbus = null);
public sealed record OpcUaProtocolRequest(bool Enabled = false, string? Endpoint = null, int? Port = null, string? MappingProfile = null);
public sealed record ModbusProtocolRequest(bool Enabled = false, int Port = 5020, IReadOnlyList<ModbusMappingRequest>? Mappings = null, string? MappingProfile = null);
public sealed record ModbusMappingRequest(string DataPoint, string Kind, int Address, string? DataType = null, string? Access = null, string? ByteOrder = null, string? WordOrder = null);
public sealed record BatchLifecycleRequest(IReadOnlyList<string> DeviceIds, string Operation);
public sealed record UpsertScenarioRequest(string Name, string Yaml, long Version = 0, string EditorJson = "{}");
public sealed record ImportScenarioRequest(string Id, string Name, string Yaml, string EditorJson = "{}");
public sealed record InstantiateTemplateRequest(
    string DeviceId,
    bool Deterministic = false,
    int Seed = 0,
    IReadOnlyList<CreatePortBindingRequest>? PortBindings = null,
    DeviceProtocolsRequest? Protocols = null);
