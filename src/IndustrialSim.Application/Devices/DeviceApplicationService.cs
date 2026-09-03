using IndustrialSim.Hosting;

namespace IndustrialSim.Application.Devices;

public sealed class DeviceApplicationService(ISimulationRegistry registry)
{
    public Task<SimulationHandle> CreateAsync(DeviceLaunchDefinition definition, CancellationToken cancellationToken = default) =>
        registry.CreateAsync(definition, cancellationToken);

    public Task StartAsync(string deviceId, CancellationToken cancellationToken = default) =>
        registry.StartAsync(deviceId, cancellationToken);

    public Task StopAsync(string deviceId, CancellationToken cancellationToken = default) =>
        registry.StopAsync(deviceId, cancellationToken);

    public Task RemoveAsync(string deviceId, CancellationToken cancellationToken = default) =>
        registry.RemoveAsync(deviceId, cancellationToken);

    public Task<SimulationBatchResult> StartManyAsync(IEnumerable<string> deviceIds, CancellationToken cancellationToken = default) =>
        registry.StartManyAsync(deviceIds, cancellationToken);

    public Task<SimulationBatchResult> StopManyAsync(IEnumerable<string> deviceIds, CancellationToken cancellationToken = default) =>
        registry.StopManyAsync(deviceIds, cancellationToken);

    public Task<SimulationBatchResult> RemoveManyAsync(IEnumerable<string> deviceIds, CancellationToken cancellationToken = default) =>
        registry.RemoveManyAsync(deviceIds, cancellationToken);

    public IReadOnlyList<SimulationSummary> List() => registry.List();
}
