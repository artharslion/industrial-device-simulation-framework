using System.Text.Json;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;

namespace IndustrialSim.Web;

public static class IndustrialSimApi
{
    public static IEndpointRouteBuilder MapIndustrialSimApi(this IEndpointRouteBuilder endpoints, SimulationHost simulation)
    {
        endpoints.MapGet("/api/state", () => Results.Ok(simulation.State.Snapshot().ToDictionary(item => item.Key, item => item.Value?.Value)));
        endpoints.MapGet("/api/runtime", () => Results.Ok(new { state = simulation.Engine.State.ToString(), time = simulation.Engine.CurrentTime.Elapsed }));
        endpoints.MapGet("/api/protocols", () => Results.Ok(new
        {
            opcua = simulation.Protocols.TryGetValue("opcua", out var opcua) && opcua.IsRunning,
            modbus = simulation.Protocols.TryGetValue("modbus", out var modbus) && modbus.IsRunning
        }));
        endpoints.MapGet("/api/events", () => Results.Ok(simulation.Events));
        endpoints.MapPost("/api/runtime/{command}", async (string command) =>
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
        endpoints.MapPost("/api/state/{name}", (string name, JsonElement value) => Results.Ok(simulation.Runtime.Write(name, JsonValue(value))));
        endpoints.MapPost("/api/scenario", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            simulation.RunScenario(await reader.ReadToEndAsync());
            return Results.Ok(new { scenario = simulation.ActiveScenarioName });
        });
        endpoints.MapPost("/api/fault", (FaultSpec fault) => { simulation.ScheduleFault(fault); return Results.Accepted(); });
        endpoints.MapPost("/api/fault/recover/{id}", (string id) => simulation.RecoverFault(id) ? Results.Ok() : Results.NotFound());
        return endpoints;
    }

    private static object? JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null => null,
        _ => throw new ArgumentException("State values must be scalar JSON values.")
    };
}
