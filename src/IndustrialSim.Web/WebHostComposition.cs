using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Protocols.OpcUa;

namespace IndustrialSim.Web;

public static class WebHostComposition
{
    public static Task<SimulationHost> CreateAsync(string configuredPath, CancellationToken cancellationToken = default) =>
        SimulationHost.LoadAsync(configuredPath, cancellationToken);

    public static async Task<SimulationHost> CreateAsync(string? configuredPath, bool allowDevelopmentFallback, CancellationToken cancellationToken = default)
        => await CreateAsync(configuredPath, allowDevelopmentFallback, new SimulationHostOptions(), cancellationToken);

    public static async Task<SimulationHost> CreateAsync(string? configuredPath, bool allowDevelopmentFallback, SimulationHostOptions options, CancellationToken cancellationToken = default)
        => await CreateAsync(configuredPath, allowDevelopmentFallback, options, null, cancellationToken);

    public static async Task<SimulationHost> CreateAsync(
        string? configuredPath,
        bool allowDevelopmentFallback,
        SimulationHostOptions options,
        OpcUaEndpointHostManager? opcUaServers,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)) return await SimulationHost.LoadAsync(configuredPath, options, opcUaServers, cancellationToken);
        if (!allowDevelopmentFallback) throw new InvalidOperationException("INDUSTRIALSIM_DEVICE_CONFIG must identify a YAML device configuration outside Development.");
        var launch = new DeviceLaunchDefinition(new DeviceDefinition(
            new DeviceId("pump-001"),
            "pump",
            new[]
            {
                new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0),
                new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, false),
                new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false)
            }), options);
        return opcUaServers is null ? SimulationHost.Create(launch) : SimulationHost.Create(launch, opcUaServers);
    }
}
