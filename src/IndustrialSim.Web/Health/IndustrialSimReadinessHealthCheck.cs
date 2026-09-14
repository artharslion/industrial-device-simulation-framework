using IndustrialSim.Hosting;
using IndustrialSim.Observability.Events;
using IndustrialSim.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialSim.Web.Health;

public sealed class IndustrialSimReadinessHealthCheck(
    IServiceScopeFactory scopeFactory,
    ISimulationRegistry registry,
    RuntimeEventLog eventLog) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = registry.List();
            if (!eventLog.IsRunning || eventLog.Failure is not null)
                return HealthCheckResult.Unhealthy("runtime event observation is unavailable");

            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<IndustrialSimDbContext>();
            if (!await database.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("control-plane database is unavailable");

            return HealthCheckResult.Healthy("control plane is ready");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return HealthCheckResult.Unhealthy("control-plane readiness check failed");
        }
    }
}
