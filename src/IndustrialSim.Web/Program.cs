using IndustrialSim.Web;
using IndustrialSim.Web.Api.V1;
using IndustrialSim.Web.Hubs;
using IndustrialSim.Hosting;
using IndustrialSim.Application.Devices;
using IndustrialSim.Persistence;
using IndustrialSim.Observability.Events;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using IndustrialSim.Web.Health;

var builder = WebApplication.CreateBuilder(args);
var configuredPath = builder.Configuration["IndustrialSim:DeviceConfig"]
    ?? Environment.GetEnvironmentVariable("INDUSTRIALSIM_DEVICE_CONFIG");
var overrides = HostConfigurationOverrides.Resolve(
    cliOpcUaEndpoint: Option(args, "--opcua-endpoint"),
    cliModbusPort: Option(args, "--modbus-port"),
    cliWebPort: Option(args, "--web-port"),
    cliLogLevel: Option(args, "--log-level"));
if (overrides.LogLevel is { } configuredLogLevel)
{
    if (!Enum.TryParse<LogLevel>(configuredLogLevel, true, out var logLevel)) throw new ArgumentException($"INDUSTRIALSIM_LOG_LEVEL '{configuredLogLevel}' is invalid.");
    builder.Logging.SetMinimumLevel(logLevel);
}
var simulation = await WebHostComposition.CreateAsync(configuredPath, builder.Environment.IsDevelopment(), new SimulationHostOptions(Overrides: overrides));
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls($"http://0.0.0.0:{simulation.WebPort}");
builder.Services.AddSingleton(simulation);
var registry = new SimulationRegistry();
await registry.AddAsync(simulation);
builder.Services.AddIndustrialSimControlPlane(
    registry,
    builder.Configuration.GetConnectionString("IndustrialSim") ?? "Data Source=industrial-sim.db",
    builder.Configuration["Auth:Mode"] ?? "Disabled",
    new RuntimeEventLogOptions(
        builder.Configuration.GetValue("IndustrialSim:Observability:IngressCapacity", 2048),
        builder.Configuration.GetValue("IndustrialSim:Observability:RetentionCapacity", 1000),
        builder.Configuration.GetValue("IndustrialSim:Observability:SubscriberCapacity", 256)));

var app = builder.Build();
app.UseIndustrialSimProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self' ws: wss:; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});
app.Logger.LogInformation("Starting industrial simulation for device {DeviceId} on Web port {WebPort}", simulation.Runtime.Definition.Id.Value, simulation.WebPort);
var runtimeEventLog = app.Services.GetRequiredService<RuntimeEventLog>();
runtimeEventLog.Attach(registry);
await runtimeEventLog.StartAsync(app.Lifetime.ApplicationStopping);
await simulation.StartAsync(app.Lifetime.ApplicationStopping);

app.MapIndustrialSimApi(simulation, requireAuthorization: true);
app.MapIndustrialSimIdentity();
app.MapIndustrialSimV1Api();
app.MapRuntimeHub();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();
app.MapOpenApi("/openapi/v1.json");
app.MapIndustrialSimDeveloperConsole();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IndustrialSimDbContext>().Database.MigrateAsync(app.Lifetime.ApplicationStopping);
    var autoStart = builder.Configuration.GetValue("IndustrialSim:Restore:AutoStartDesiredRunning", false);
    var results = await scope.ServiceProvider.GetRequiredService<DeviceCatalogRestoreService>().RestoreAsync(autoStart, app.Lifetime.ApplicationStopping);
    foreach (var result in results.Where(result => !result.Restored || result.ErrorCode is not null))
        app.Logger.LogError("Device restore for {DeviceId} completed with code {ErrorCode}: {Error}", result.DeviceId, result.ErrorCode, result.Error);
}

try
{
    await app.RunAsync();
}
finally
{
    await registry.DisposeAsync();
}

static string? Option(string[] values, string name)
{
    var index = Array.FindIndex(values, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
}

public partial class Program { }
