using System.Text.Json;

namespace IndustrialSim.Web.Api.V1;

public sealed record CreateDataPointRequest(string Name, string DataType, string Access, JsonElement? Initial = null, string? Unit = null, string? Description = null);
public sealed record CreateDeviceRequest(
    string Id,
    string Type,
    IReadOnlyList<CreateDataPointRequest> DataPoints,
    bool Deterministic = false,
    int Seed = 0,
    IReadOnlyList<CreatePortBindingRequest>? PortBindings = null);
public sealed record CreatePortBindingRequest(string Protocol, int Port);
public sealed record BatchLifecycleRequest(IReadOnlyList<string> DeviceIds, string Operation);
public sealed record UpsertScenarioRequest(string Name, string Yaml, long Version = 0);
