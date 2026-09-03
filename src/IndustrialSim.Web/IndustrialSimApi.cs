using System.Text.Json;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;

namespace IndustrialSim.Web;

public sealed record FaultRequest(string Id, string Category, string? Target, string Type, double? DurationSeconds = null, IReadOnlyDictionary<string, string>? Metadata = null);

public static class IndustrialSimApi
{
    public static IEndpointRouteBuilder MapIndustrialSimApi(this IEndpointRouteBuilder endpoints, SimulationHost simulation)
    {
        var legacy = endpoints.MapGroup("/api").AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers["Deprecation"] = "true";
            context.HttpContext.Response.Headers["Sunset"] = "Wed, 03 Mar 2027 00:00:00 GMT";
            context.HttpContext.Response.Headers.Link = "</api/v1>; rel=\"successor-version\"";
            return await next(context);
        });
        legacy.MapGet("/state", () => Results.Ok(simulation.State.Snapshot().ToDictionary(item => item.Key, item => item.Value?.Value)));
        legacy.MapGet("/runtime", () => Results.Ok(new
        {
            state = simulation.Engine.State.ToString(),
            time = simulation.Engine.CurrentTime.Elapsed,
            deviceId = simulation.Runtime.Definition.Id.Value,
            deviceType = simulation.Runtime.Definition.Type,
            deterministic = simulation.IsDeterministic,
            seed = simulation.Seed,
            scenario = new { name = simulation.ActiveScenarioName, running = simulation.ScenarioRunner?.IsRunning == true },
            activeFaults = simulation.FaultManager.ActiveFaults.Count
        }));
        legacy.MapGet("/protocols", () => Results.Ok(new
        {
            opcua = simulation.Protocols.TryGetValue("opcua", out var opcua) && opcua.IsRunning,
            modbus = simulation.Protocols.TryGetValue("modbus", out var modbus) && modbus.IsRunning
        }));
        legacy.MapGet("/events", () => Results.Ok(simulation.Events));
        legacy.MapGet("/faults", () => Results.Ok(simulation.FaultManager.ActiveFaults));
        legacy.MapPost("/runtime/{command}", async (string command) =>
        {
            switch (command.ToLowerInvariant())
            {
                case "start": await simulation.StartAsync(); break;
                case "stop": await simulation.StopAsync(); break;
                case "pause": simulation.Engine.Pause(); break;
                case "reset": simulation.Reset(); break;
                default: return Results.NotFound();
            }
            return Results.Ok(new { state = simulation.Engine.State.ToString() });
        });
        legacy.MapPost("/runtime/tick/{seconds:double}", (double seconds) =>
        {
            if (!simulation.IsDeterministic) return Results.BadRequest(new { error = "Explicit ticks require deterministic mode." });
            if (seconds < 0) return Results.BadRequest(new { error = "Tick duration cannot be negative." });
            simulation.Tick(TimeSpan.FromSeconds(seconds));
            return Results.Ok(new { time = simulation.Engine.CurrentTime.Elapsed });
        });
        legacy.MapPost("/state/{name}", (string name, JsonElement value) =>
        {
            var result = simulation.Runtime.Write(name, JsonValue(value));
            return result.Succeeded ? Results.Ok(result) : Results.BadRequest(new { error = result.Error });
        });
        legacy.MapPost("/scenario", async (HttpRequest request) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body);
                simulation.RunScenario(await reader.ReadToEndAsync());
                simulation.Update();
                return Results.Ok(new { scenario = simulation.ActiveScenarioName, running = true });
            }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        });
        legacy.MapDelete("/scenario", () => simulation.StopScenario() ? Results.Ok(new { running = false }) : Results.NotFound(new { error = "No scenario is running." }));
        legacy.MapPost("/fault", (FaultRequest request) =>
        {
            if (!Enum.TryParse<FaultCategory>(request.Category, true, out var category)) return Results.BadRequest(new { error = $"Unknown fault category '{request.Category}'." });
            try
            {
                var fault = new FaultSpec(
                    string.IsNullOrWhiteSpace(request.Id) ? $"fault-{Guid.NewGuid():N}" : request.Id,
                    category,
                    simulation.Runtime.Definition.Id.Value,
                    request.Target,
                    simulation.Engine.CurrentTime.Elapsed,
                    request.DurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                    request.Type,
                    request.Metadata);
                simulation.ActivateFault(fault);
                return Results.Accepted(value: fault);
            }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        });
        legacy.MapPost("/fault/recover/{id}", (string id) => simulation.RecoverFault(id) ? Results.Ok() : Results.NotFound());
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
