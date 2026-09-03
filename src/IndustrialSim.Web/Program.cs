using IndustrialSim.Web;
using IndustrialSim.Web.Api.V1;
using IndustrialSim.Web.Hubs;
using IndustrialSim.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
var configuredPath = Environment.GetEnvironmentVariable("INDUSTRIALSIM_DEVICE_CONFIG");
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
    builder.Configuration["Auth:Mode"] ?? "Disabled");

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
await simulation.StartAsync(app.Lifetime.ApplicationStopping);

app.MapIndustrialSimApi(simulation, requireAuthorization: true);
app.MapIndustrialSimIdentity();
app.MapIndustrialSimV1Api();
app.MapRuntimeHub();
app.MapOpenApi("/openapi/v1.json");
app.MapIndustrialSimDeveloperConsole();

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
