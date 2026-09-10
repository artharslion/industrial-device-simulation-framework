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
    CreateBehaviorRequest? Behavior = null);
public sealed record CreatePortBindingRequest(string Protocol, int Port);
public sealed record BatchLifecycleRequest(IReadOnlyList<string> DeviceIds, string Operation);
public sealed record UpsertScenarioRequest(string Name, string Yaml, long Version = 0, string EditorJson = "{}");
public sealed record ImportScenarioRequest(string Id, string Name, string Yaml, string EditorJson = "{}");
public sealed record InstantiateTemplateRequest(
    string DeviceId,
    bool Deterministic = false,
    int Seed = 0,
    IReadOnlyList<CreatePortBindingRequest>? PortBindings = null);
