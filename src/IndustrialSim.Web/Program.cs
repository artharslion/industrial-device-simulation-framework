using IndustrialSim.Core.Domain;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;
using IndustrialSim.Web;

var builder = WebApplication.CreateBuilder(args);
var configuredPath = Environment.GetEnvironmentVariable("INDUSTRIALSIM_DEVICE_CONFIG");
var simulation = await WebHostComposition.CreateAsync(configuredPath, builder.Environment.IsDevelopment());
builder.Services.AddSingleton(simulation);

var app = builder.Build();
await simulation.StartAsync(app.Lifetime.ApplicationStopping);

app.MapGet("/api/state", () => Results.Ok(simulation.State.Snapshot().ToDictionary(item => item.Key, item => item.Value?.Value)));
app.MapGet("/api/runtime", () => Results.Ok(new { state = simulation.Engine.State.ToString(), time = simulation.Engine.CurrentTime.Elapsed }));
app.MapGet("/api/protocols", () => Results.Ok(new
{
    opcua = simulation.Protocols.TryGetValue("opcua", out var opcua) && opcua.IsRunning,
    modbus = simulation.Protocols.TryGetValue("modbus", out var modbus) && modbus.IsRunning
}));
app.MapGet("/api/events", () => Results.Ok(simulation.Events));
app.MapPost("/api/runtime/{command}", async (string command) =>
{
    switch (command.ToLowerInvariant())
    {
        case "start": await simulation.StartAsync(); break;
        case "stop": await simulation.StopAsync(); break;
        case "pause": simulation.Engine.Pause(); break;
        case "reset": simulation.Engine.Reset(); break;
        default: return Results.NotFound();
    }
    return Results.Ok(new { state = simulation.Engine.State.ToString() });
});
app.MapPost("/api/state/{name}", (string name, object value) => Results.Ok(simulation.Runtime.Write(name, value)));
app.MapPost("/api/scenario", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    simulation.RunScenario(await reader.ReadToEndAsync());
    return Results.Ok(new { scenario = simulation.ActiveScenarioName });
});
app.MapPost("/api/fault", (FaultSpec fault) => { simulation.FaultManager.Schedule(fault); return Results.Accepted(); });
app.MapPost("/api/fault/recover/{id}", (string id) => simulation.FaultManager.Recover(id) ? Results.Ok() : Results.NotFound());
app.MapGet("/", () => Results.Content("IndustrialSim Developer Console", "text/plain"));

try
{
    await app.RunAsync();
}
finally
{
    await simulation.DisposeAsync();
}

public partial class Program { }
