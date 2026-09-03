using IndustrialSim.Application.Catalogs;
using IndustrialSim.Hosting;
using IndustrialSim.Persistence;
using IndustrialSim.Persistence.Repositories;
using IndustrialSim.Web.Hubs;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Web.Api.V1;

public static class ControlPlaneServices
{
    public static IServiceCollection AddIndustrialSimControlPlane(
        this IServiceCollection services,
        SimulationRegistry registry,
        string connectionString)
    {
        services.AddSingleton<ISimulationRegistry>(registry);
        services.AddSingleton(registry);
        services.AddDbContext<IndustrialSimDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IDeviceCatalogRepository, DeviceCatalogRepository>();
        services.AddScoped<IScenarioCatalogRepository, ScenarioCatalogRepository>();
        services.AddScoped<ISettingCatalogRepository, SettingCatalogRepository>();
        services.AddScoped<ISnapshotCatalogRepository, SnapshotCatalogRepository>();
        services.AddSingleton(provider =>
        {
            var broker = new RuntimeStreamBroker();
            broker.Attach(provider.GetRequiredService<ISimulationRegistry>());
            return broker;
        });
        services.AddSignalR();
        services.AddOpenApi();
        return services;
    }
}
