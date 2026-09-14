using IndustrialSim.Hosting;
using IndustrialSim.Observability.Events;

namespace IndustrialSim.Web.Hubs;

public sealed class RuntimeEventLogLifecycle(RuntimeEventLog eventLog, ISimulationRegistry registry) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        eventLog.Attach(registry);
        return eventLog.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => eventLog.StopAsync(cancellationToken);
}
