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

public interface IProtocolOperationObserver
{
    IProtocolOperationScope Start(string deviceId, string protocol, string operation);
}

public interface IProtocolOperationScope : IDisposable
{
    void SetResult(bool succeeded, string? errorCode);
}
