using IndustrialSim.Core.Domain;

namespace IndustrialSim.Hosting;

public sealed record ScenarioActionObservation(
    string DeviceId,
    string ScenarioName,
    string Action,
    SimulationTime Timestamp);

public sealed record ProtocolLifecycleObservation(
    string DeviceId,
    string Protocol,
    string Operation,
    bool Succeeded,
    string? ErrorCode,
    SimulationTime Timestamp);
