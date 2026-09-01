using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;

namespace IndustrialSim.Web;

public static class WebHostComposition
{
    public static Task<SimulationHost> CreateAsync(string configuredPath, CancellationToken cancellationToken = default) =>
        SimulationHost.LoadAsync(configuredPath, cancellationToken);

    public static async Task<SimulationHost> CreateAsync(string? configuredPath, bool allowDevelopmentFallback, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)) return await SimulationHost.LoadAsync(configuredPath, cancellationToken);
        if (!allowDevelopmentFallback) throw new InvalidOperationException("INDUSTRIALSIM_DEVICE_CONFIG must identify a YAML device configuration outside Development.");
        return SimulationHost.Create(new DeviceDefinition(
            new DeviceId("pump-001"),
            "pump",
            new[]
            {
                new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0),
                new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, false),
                new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false)
            }));
    }
}
