using IndustrialSim.Application.Catalogs;
using IndustrialSim.Application.Devices;
using IndustrialSim.Application.Abstractions;
using IndustrialSim.Application.Security;
using IndustrialSim.Application.Templates;
using IndustrialSim.Hosting;
using IndustrialSim.Persistence;
using IndustrialSim.Persistence.Repositories;
using IndustrialSim.Web.Hubs;
using IndustrialSim.Observability.Events;
using IndustrialSim.Observability.Security;
using IndustrialSim.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using IndustrialSim.Web.Health;
using IndustrialSim.Observability.Metrics;
using Prometheus;
using IndustrialSim.Observability.Tracing;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace IndustrialSim.Web.Api.V1;

public static class ControlPlaneServices
{
    public static IServiceCollection AddIndustrialSimTracing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var otlpEndpoint = configuration["OpenTelemetry:Otlp:Endpoint"];
        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(otlpEndpoint) &&
            (!Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out endpoint) || endpoint.Scheme is not ("http" or "https")))
            throw new ArgumentException("OpenTelemetry:Otlp:Endpoint must be an absolute HTTP or HTTPS URI.");

        services.AddOpenTelemetry().WithTracing(tracing =>
        {
            tracing
                .AddSource(IndustrialSimActivitySource.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();
            if (endpoint is not null)
                tracing.AddOtlpExporter(options =>
                {
                    options.Endpoint = endpoint;
                    options.ExportProcessorType = ExportProcessorType.Batch;
                });
        });
        return services;
    }

    public static IServiceCollection AddIndustrialSimControlPlane(
        this IServiceCollection services,
        SimulationRegistry registry,
        string connectionString,
        string authMode = "Disabled",
        RuntimeEventLogOptions? observabilityOptions = null)
    {
        services.AddSingleton<ISimulationRegistry>(registry);
        services.AddSingleton(registry);
        services.AddDbContext<IndustrialSimDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IDeviceCatalogRepository, DeviceCatalogRepository>();
        services.AddScoped<IScenarioCatalogRepository, ScenarioCatalogRepository>();
        services.AddScoped<ISettingCatalogRepository, SettingCatalogRepository>();
        services.AddScoped<ISnapshotCatalogRepository, SnapshotCatalogRepository>();
        services.AddScoped<ITemplateCatalogRepository, TemplateCatalogRepository>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<IndustrialSimDbContext>());
        services.AddScoped<DeviceCatalogRestoreService>();
        services.AddSingleton(observabilityOptions ?? new RuntimeEventLogOptions());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SecretRedactor>();
        services.AddSingleton<IRuntimeEventEnvelopeFactory, RuntimeEventEnvelopeFactory>();
        services.AddSingleton(_ => Prometheus.Metrics.NewCustomRegistry());
        services.AddSingleton<IndustrialSimMetrics>();
        services.AddSingleton<IProtocolOperationObserver, ProtocolOperationObserver>();
        services.AddSingleton<RuntimeEventLog>();
        services.AddSingleton<IHostedService, RuntimeEventLogLifecycle>();
        services.AddSingleton(provider => new RuntimeStreamBroker(
            provider.GetRequiredService<RuntimeEventLog>(),
            provider.GetRequiredService<IndustrialSimMetrics>()));
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<IndustrialSimReadinessHealthCheck>("control-plane", tags: ["ready"]);
        services.AddSignalR();
        services.AddOpenApi();
        services.AddSingleton(new IndustrialAuthOptions(authMode));
        services.AddIdentityApiEndpoints<IndustrialSimUser>(options =>
        {
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.User.RequireUniqueEmail = false;
        })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<IndustrialSimDbContext>();
        services.AddAuthorizationBuilder()
            .AddPolicy(IndustrialPolicies.Viewer, policy => policy.RequireAssertion(context =>
                IsDisabled(authMode) || context.User.IsInRole(IndustrialRoles.Viewer) || context.User.IsInRole(IndustrialRoles.Operator) || context.User.IsInRole(IndustrialRoles.Admin)))
            .AddPolicy(IndustrialPolicies.Operator, policy => policy.RequireAssertion(context =>
                IsDisabled(authMode) || context.User.IsInRole(IndustrialRoles.Operator) || context.User.IsInRole(IndustrialRoles.Admin)))
            .AddPolicy(IndustrialPolicies.Admin, policy => policy.RequireAssertion(context =>
                IsDisabled(authMode) || context.User.IsInRole(IndustrialRoles.Admin)));
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, IndustrialAuthorizationResultHandler>();
        return services;
    }

    private static bool IsDisabled(string mode) => mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
}
