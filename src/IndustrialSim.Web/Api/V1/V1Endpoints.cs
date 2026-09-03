using System.Text.Json;
using IndustrialSim.Application.Catalogs;
using IndustrialSim.Application.Security;
using IndustrialSim.Core.Domain;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;
using IndustrialSim.Persistence;
using IndustrialSim.Scenarios;
using IndustrialSim.Web.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace IndustrialSim.Web.Api.V1;

public static class V1Endpoints
{
    public static IEndpointRouteBuilder MapIndustrialSimV1Api(this IEndpointRouteBuilder endpoints)
    {
        using (var scope = endpoints.ServiceProvider.CreateScope())
            scope.ServiceProvider.GetRequiredService<IndustrialSimDbContext>().Database.Migrate();

        var api = endpoints.MapGroup("/api/v1").WithTags("IndustrialSim v1").RequireAuthorization(IndustrialPolicies.Viewer);
        api.MapVisualModelingEndpoints();

        api.MapGet("/devices", (ISimulationRegistry registry) => Results.Ok(registry.List()));
        api.MapGet("/devices/{deviceId}", (string deviceId, ISimulationRegistry registry) => Results.Ok(Summary(registry.Get(deviceId))));
        api.MapPost("/devices", CreateDeviceAsync).RequireAuthorization(IndustrialPolicies.Admin);
        api.MapDelete("/devices/{deviceId}", RemoveDeviceAsync).RequireAuthorization(IndustrialPolicies.Admin);
        api.MapPost("/devices/{deviceId}/{operation}", RunLifecycleAsync).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapPost("/devices/{deviceId}/tick/{seconds:double}", Tick).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapPost("/devices/batch", RunBatchAsync).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapGet("/devices/{deviceId}/state", (string deviceId, ISimulationRegistry registry) =>
            Results.Ok(registry.Get(deviceId).Host.State.Snapshot().ToDictionary(item => item.Key, item => item.Value?.Value)));
        api.MapPut("/devices/{deviceId}/state/{dataPoint}", WriteState).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapGet("/devices/{deviceId}/runtime", (string deviceId, ISimulationRegistry registry) => Results.Ok(Runtime(registry.Get(deviceId).Host)));
        api.MapGet("/devices/{deviceId}/events", (string deviceId, ISimulationRegistry registry) => Results.Ok(registry.Get(deviceId).Host.Events));
        api.MapGet("/devices/{deviceId}/faults", (string deviceId, ISimulationRegistry registry) => Results.Ok(registry.Get(deviceId).Host.FaultManager.ActiveFaults));
        api.MapPost("/devices/{deviceId}/faults", ActivateFault).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapPost("/devices/{deviceId}/faults/{faultId}/recover", (string deviceId, string faultId, ISimulationRegistry registry) =>
            registry.Get(deviceId).Host.RecoverFault(faultId)
                ? Results.Ok()
                : IndustrialSimProblemDetails.Result(404, "Fault not found", $"Fault '{faultId}' is not active.", "faultNotFound"))
            .RequireAuthorization(IndustrialPolicies.Operator);

        api.MapGet("/protocols", (ISimulationRegistry registry) => Results.Ok(registry.List().Select(summary =>
        {
            var handle = registry.Get(summary.DeviceId);
            return new
            {
                deviceId = summary.DeviceId,
                configured = handle.Host.Protocols.Select(protocol => new { name = protocol.Key, running = protocol.Value.IsRunning }),
                reserved = handle.PortBindings.Select(binding => new { name = binding.Protocol, port = binding.Port })
            };
        })));

        api.MapGet("/scenarios", async (IScenarioCatalogRepository repository, CancellationToken token) => Results.Ok(await repository.ListAsync(token)));
        api.MapGet("/scenarios/{scenarioId}", async (string scenarioId, IScenarioCatalogRepository repository, CancellationToken token) =>
            await repository.FindAsync(scenarioId, token) is { } scenario
                ? Results.Ok(scenario)
                : IndustrialSimProblemDetails.Result(404, "Scenario not found", $"Scenario '{scenarioId}' was not found.", "scenarioNotFound"));
        api.MapPut("/scenarios/{scenarioId}", UpsertScenarioAsync).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapDelete("/scenarios/{scenarioId}", RemoveScenarioAsync).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapPost("/devices/{deviceId}/scenarios/{scenarioId}/start", StartScenarioAsync).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapPost("/devices/{deviceId}/scenario", RunInlineScenarioAsync).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapDelete("/devices/{deviceId}/scenario", (string deviceId, ISimulationRegistry registry) =>
            registry.Get(deviceId).Host.StopScenario()
                ? Results.Ok(new { running = false })
                : IndustrialSimProblemDetails.Result(404, "Scenario not running", "No scenario is running.", "scenarioNotRunning"))
            .RequireAuthorization(IndustrialPolicies.Operator);

        return endpoints;
    }

    private static async Task<IResult> CreateDeviceAsync(
        CreateDeviceRequest request,
        ISimulationRegistry registry,
        IDeviceCatalogRepository repository,
        IndustrialSimDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Type) || request.DataPoints.Count == 0)
            return IndustrialSimProblemDetails.Result(400, "Validation failed", "Device id, type, and at least one data point are required.", "invalidDevice");
        var definition = new DeviceDefinition(
            new DeviceId(request.Id),
            request.Type,
            request.DataPoints.Select(ToDefinition),
            [], []);
        var launch = new DeviceLaunchDefinition(
            definition,
            new SimulationHostOptions(request.Deterministic, request.Seed),
            request.PortBindings?.Select(binding => new ProtocolPortBinding(binding.Protocol, binding.Port)).ToArray());
        var handle = await registry.CreateAsync(launch, cancellationToken);
        try
        {
            await repository.UpsertAsync(new DeviceCatalogItem(request.Id, JsonSerializer.Serialize(request), "Stopped", 0), cancellationToken);
            await db.CommitAsync(cancellationToken);
        }
        catch
        {
            await registry.RemoveAsync(handle.DeviceId, CancellationToken.None);
            throw;
        }
        return Results.Created($"/api/v1/devices/{request.Id}", Summary(handle));
    }

    private static async Task<IResult> RemoveDeviceAsync(
        string deviceId,
        ISimulationRegistry registry,
        IDeviceCatalogRepository repository,
        IndustrialSimDbContext db,
        CancellationToken cancellationToken)
    {
        registry.Get(deviceId);
        await repository.RemoveAsync(deviceId, cancellationToken);
        await db.CommitAsync(cancellationToken);
        await registry.RemoveAsync(deviceId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RunLifecycleAsync(string deviceId, string operation, ISimulationRegistry registry, CancellationToken cancellationToken)
    {
        switch (operation.ToLowerInvariant())
        {
            case "start": await registry.StartAsync(deviceId, cancellationToken); break;
            case "stop": await registry.StopAsync(deviceId, cancellationToken); break;
            case "pause": registry.Get(deviceId).Host.Engine.Pause(); break;
            case "reset": registry.Get(deviceId).Host.Reset(); break;
            default: return IndustrialSimProblemDetails.Result(400, "Invalid operation", $"Lifecycle operation '{operation}' is not supported.", "invalidLifecycleOperation");
        }
        return Results.Ok(Runtime(registry.Get(deviceId).Host));
    }

    private static async Task<IResult> RunBatchAsync(
        BatchLifecycleRequest request,
        ISimulationRegistry registry,
        ClaimsPrincipal user,
        IAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (request.Operation.Equals("remove", StringComparison.OrdinalIgnoreCase) &&
            !(await authorization.AuthorizeAsync(user, IndustrialPolicies.Admin)).Succeeded)
            return IndustrialSimProblemDetails.Result(403, "Forbidden", "Batch removal requires the Admin role.", "adminRoleRequired");
        var result = request.Operation.ToLowerInvariant() switch
        {
            "start" => await registry.StartManyAsync(request.DeviceIds, cancellationToken),
            "stop" => await registry.StopManyAsync(request.DeviceIds, cancellationToken),
            "remove" => await registry.RemoveManyAsync(request.DeviceIds, cancellationToken),
            _ => null
        };
        return result is null
            ? IndustrialSimProblemDetails.Result(400, "Invalid operation", $"Batch operation '{request.Operation}' is not supported.", "invalidBatchOperation")
            : Results.Ok(result);
    }

    private static IResult Tick(string deviceId, double seconds, ISimulationRegistry registry)
    {
        var host = registry.Get(deviceId).Host;
        if (!host.IsDeterministic)
            return IndustrialSimProblemDetails.Result(400, "Tick rejected", "Explicit ticks require deterministic mode.", "deterministicModeRequired");
        if (seconds < 0)
            return IndustrialSimProblemDetails.Result(400, "Tick rejected", "Tick duration cannot be negative.", "invalidTickDuration");
        host.Tick(TimeSpan.FromSeconds(seconds));
        return Results.Ok(new { time = host.Engine.CurrentTime.Elapsed });
    }

    private static IResult WriteState(string deviceId, string dataPoint, JsonElement value, ISimulationRegistry registry)
    {
        var result = registry.Get(deviceId).Host.Runtime.Write(dataPoint, JsonValue(value));
        return result.Succeeded
            ? Results.Ok(result)
            : IndustrialSimProblemDetails.Result(400, "State write rejected", result.Error ?? "State write was rejected.", "stateWriteRejected");
    }

    private static IResult ActivateFault(string deviceId, FaultRequest request, ISimulationRegistry registry)
    {
        if (!Enum.TryParse<FaultCategory>(request.Category, true, out var category))
            return IndustrialSimProblemDetails.Result(400, "Invalid fault", $"Unknown fault category '{request.Category}'.", "invalidFaultCategory");
        var host = registry.Get(deviceId).Host;
        var fault = new FaultSpec(
            string.IsNullOrWhiteSpace(request.Id) ? $"fault-{Guid.NewGuid():N}" : request.Id,
            category, deviceId, request.Target, host.Engine.CurrentTime.Elapsed,
            request.DurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            request.Type, request.Metadata);
        host.ActivateFault(fault);
        return Results.Accepted(value: fault);
    }

    private static async Task<IResult> UpsertScenarioAsync(
        string scenarioId,
        UpsertScenarioRequest request,
        IScenarioCatalogRepository repository,
        IndustrialSimDbContext db,
        CancellationToken cancellationToken)
    {
        _ = new ScenarioParser().Parse(request.Yaml);
        _ = JsonDocument.Parse(request.EditorJson);
        await repository.UpsertAsync(new ScenarioCatalogItem(scenarioId, request.Name, request.Yaml, request.Version, request.EditorJson), cancellationToken);
        await db.CommitAsync(cancellationToken);
        return Results.Ok(await repository.FindAsync(scenarioId, cancellationToken));
    }

    private static async Task<IResult> RemoveScenarioAsync(
        string scenarioId,
        IScenarioCatalogRepository repository,
        IndustrialSimDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await repository.RemoveAsync(scenarioId, cancellationToken))
            return IndustrialSimProblemDetails.Result(404, "Scenario not found", $"Scenario '{scenarioId}' was not found.", "scenarioNotFound");
        await db.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> StartScenarioAsync(
        string deviceId,
        string scenarioId,
        ISimulationRegistry registry,
        IScenarioCatalogRepository repository,
        CancellationToken cancellationToken)
    {
        var scenario = await repository.FindAsync(scenarioId, cancellationToken);
        if (scenario is null)
            return IndustrialSimProblemDetails.Result(404, "Scenario not found", $"Scenario '{scenarioId}' was not found.", "scenarioNotFound");
        var host = registry.Get(deviceId).Host;
        host.RunScenario(scenario.Yaml);
        host.Update();
        return Results.Ok(new { scenario = host.ActiveScenarioName, running = true });
    }

    private static async Task<IResult> RunInlineScenarioAsync(
        string deviceId,
        HttpRequest request,
        ISimulationRegistry registry)
    {
        using var reader = new StreamReader(request.Body);
        var host = registry.Get(deviceId).Host;
        host.RunScenario(await reader.ReadToEndAsync());
        host.Update();
        return Results.Ok(new { scenario = host.ActiveScenarioName, running = true });
    }

    private static DataPointDefinition ToDefinition(CreateDataPointRequest request)
    {
        if (!Enum.TryParse<DataType>(request.DataType, true, out var dataType)) throw new ArgumentException($"Unknown data type '{request.DataType}'.");
        if (!Enum.TryParse<DataPointAccess>(request.Access, true, out var access)) throw new ArgumentException($"Unknown access mode '{request.Access}'.");
        return new DataPointDefinition(request.Name, dataType, access, request.Initial is { } initial ? JsonValue(initial) : null, request.Unit, request.Description);
    }

    private static object Summary(SimulationHandle handle) => new
    {
        deviceId = handle.DeviceId,
        deviceType = handle.Host.Runtime.Definition.Type,
        isRunning = handle.Host.IsRunning,
        deterministic = handle.Host.IsDeterministic,
        seed = handle.Host.Seed,
        simulationTime = handle.Host.Engine.CurrentTime.Elapsed
    };

    private static object Runtime(SimulationHost simulation) => new
    {
        state = simulation.Engine.State.ToString(),
        time = simulation.Engine.CurrentTime.Elapsed,
        deviceId = simulation.Runtime.Definition.Id.Value,
        deviceType = simulation.Runtime.Definition.Type,
        deterministic = simulation.IsDeterministic,
        seed = simulation.Seed,
        scenario = new { name = simulation.ActiveScenarioName, running = simulation.ScenarioRunner?.IsRunning == true },
        activeFaults = simulation.FaultManager.ActiveFaults.Count
    };

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
