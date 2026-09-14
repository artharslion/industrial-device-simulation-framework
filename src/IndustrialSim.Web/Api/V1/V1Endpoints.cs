using System.Text.Json;
using IndustrialSim.Application.Catalogs;
using IndustrialSim.Application.Devices;
using IndustrialSim.Application.Security;
using IndustrialSim.Core.Domain;
using IndustrialSim.Devices;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;
using IndustrialSim.Persistence;
using IndustrialSim.Scenarios;
using IndustrialSim.Web.Hubs;
using IndustrialSim.Observability.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using IndustrialSim.Observability.Security;
using IndustrialSim.Observability.Tracing;

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
        api.MapGet("/device-profiles", () => Results.Ok(BuiltInDeviceProfiles.All.Select(profile => new
        {
            profile.Name,
            profile.DisplayName,
            profile.Description,
            dataPoints = profile.DataPoints.Select(point => new
            {
                name = point.Name,
                dataType = point.DataType.ToString(),
                access = point.Access.ToString(),
                initial = point.InitialValue?.Value,
                point.Unit,
                point.Description
            }),
            commands = profile.Commands.Select(command => command.Name),
            events = profile.Events.Select(@event => @event.Name),
            parameters = profile.Parameters.Select(parameter => new
            {
                parameter.Name,
                parameter.DefaultValue,
                parameter.Minimum,
                parameter.Unit,
                parameter.Description
            })
        })));
        api.MapGet("/devices/{deviceId}", DeviceDetailsAsync);
        api.MapPost("/devices", CreateDeviceAsync).RequireAuthorization(IndustrialPolicies.Admin);
        api.MapPut("/devices/{deviceId}", UpdateDeviceAsync).RequireAuthorization(IndustrialPolicies.Admin);
        api.MapDelete("/devices/{deviceId}", RemoveDeviceAsync).RequireAuthorization(IndustrialPolicies.Admin);
        api.MapPost("/devices/{deviceId}/{operation}", RunLifecycleAsync).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapPost("/devices/{deviceId}/tick/{seconds:double}", Tick).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapPost("/devices/batch", RunBatchAsync).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapGet("/devices/{deviceId}/state", (string deviceId, ISimulationRegistry registry) =>
            Results.Ok(registry.Get(deviceId).Host.State.Snapshot().ToDictionary(item => item.Key, item => item.Value?.Value)));
        api.MapPut("/devices/{deviceId}/state/{dataPoint}", WriteState).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapGet("/devices/{deviceId}/runtime", (string deviceId, ISimulationRegistry registry) => Results.Ok(Runtime(registry.Get(deviceId).Host)));
        api.MapGet("/devices/{deviceId}/events", RuntimeEvents);
        api.MapGet("/devices/{deviceId}/faults", (string deviceId, ISimulationRegistry registry) => Results.Ok(registry.Get(deviceId).Host.FaultManager.ActiveFaults));
        api.MapPost("/devices/{deviceId}/faults", ActivateFault).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapPost("/devices/{deviceId}/faults/{faultId}/recover", RecoverFault)
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
        api.MapDelete("/devices/{deviceId}/scenario", StopScenario)
            .RequireAuthorization(IndustrialPolicies.Operator);

        return endpoints;
    }

    private static async Task<IResult> CreateDeviceAsync(
        CreateDeviceRequest request,
        ISimulationRegistry registry,
        IDeviceCatalogRepository repository,
        IndustrialSimDbContext db,
        SecretRedactor redactor,
        CancellationToken cancellationToken)
    {
        using var trace = IndustrialSimOperation.Start("industrial.device.create", redactor, request.Id);
        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Type) || request.DataPoints.Count == 0)
        {
            trace.SetResult("rejected", "invalidDevice");
            return IndustrialSimProblemDetails.Result(400, "Validation failed", "Device id, type, and at least one data point are required.", "invalidDevice");
        }
        var definition = DeviceLaunchRequestMapper.ToDefinition(request);
        if (definition.Behavior is not null)
        {
            try { BuiltInDeviceProfiles.Validate(definition, definition.Behavior); }
            catch (ArgumentException exception)
            {
                trace.SetResult("rejected", "invalidBehaviorProfile");
                return IndustrialSimProblemDetails.Result(400, "Invalid behavior profile", exception.Message, "invalidBehaviorProfile");
            }
        }
        var launch = DeviceLaunchRequestMapper.ToLaunch(request);
        var handle = await registry.CreateAsync(launch, cancellationToken);
        try
        {
            await repository.UpsertAsync(new DeviceCatalogItem(request.Id, DeviceLaunchDocumentSerializer.Serialize(launch), "Stopped", 0), cancellationToken);
            await db.CommitAsync(cancellationToken);
        }
        catch
        {
            await registry.RemoveAsync(handle.DeviceId, CancellationToken.None);
            throw;
        }
        trace.SetResult("success");
        return Results.Created($"/api/v1/devices/{request.Id}", Summary(handle));
    }

    private static async Task<IResult> DeviceDetailsAsync(
        string deviceId,
        ISimulationRegistry registry,
        IDeviceCatalogRepository repository,
        IScenarioCatalogRepository scenarios,
        RuntimeEventLog eventLog,
        CancellationToken cancellationToken)
    {
        var handle = registry.Get(deviceId);
        var catalog = await repository.FindAsync(deviceId, cancellationToken);
        return Results.Ok(new
        {
            summary = Summary(handle),
            runtime = Runtime(handle.Host),
            state = handle.Host.State.Snapshot().ToDictionary(item => item.Key, item => item.Value?.Value),
            definition = new
            {
                id = handle.DeviceId,
                type = handle.Host.Runtime.Definition.Type,
                deterministic = handle.Host.IsDeterministic,
                seed = handle.Host.Seed,
                version = catalog?.Version ?? 0,
                dataPoints = handle.Host.Runtime.Definition.DataPoints.Select(point => new
                {
                    name = point.Name,
                    dataType = point.DataType.ToString(),
                    access = point.Access.ToString(),
                    initial = point.InitialValue?.Value,
                    unit = point.Unit,
                    description = point.Description
                }),
                commands = handle.Host.Runtime.Definition.Commands.Select(command => command.Name),
                events = handle.Host.Runtime.Definition.Events.Select(@event => @event.Name),
                behavior = handle.Host.Behavior is { } behavior
                    ? new { profile = behavior.Profile, parameters = behavior.Parameters }
                    : null,
                portBindings = handle.PortBindings.Select(binding => new { protocol = binding.Protocol, port = binding.Port }),
                protocols = new
                {
                    opcua = handle.Host.LaunchDefinition.OpcUa is { } opcUa
                        ? new { enabled = true, endpoint = opcUa.Endpoint, port = (int?)null, mappingProfile = (string?)null }
                        : null,
                    modbus = handle.Host.LaunchDefinition.Modbus is { } modbus
                        ? new
                        {
                            enabled = true,
                            port = modbus.Port,
                            mappingProfile = (string?)null,
                            mappings = modbus.Mappings.Select(mapping => new
                            {
                                dataPoint = mapping.Name,
                                kind = mapping.Kind,
                                mapping.Address,
                                dataType = mapping.DataType,
                                mapping.Access,
                                mapping.ByteOrder,
                                mapping.WordOrder
                            })
                        }
                        : null
                }
            },
            protocols = handle.Host.Protocols.Select(protocol => new { name = protocol.Key, running = protocol.Value.IsRunning }),
            scenarios = new { active = handle.Host.ActiveScenarioName, running = handle.Host.ScenarioRunner?.IsRunning == true, available = await scenarios.ListAsync(cancellationToken) },
            faults = handle.Host.FaultManager.ActiveFaults,
            events = eventLog.Query(new RuntimeEventQuery(DeviceId: deviceId, Limit: 100))
        });
    }

    private static IResult RuntimeEvents(
        string deviceId,
        string? eventType,
        long? afterSequence,
        int? limit,
        ISimulationRegistry registry,
        RuntimeEventLog eventLog)
    {
        registry.Get(deviceId);
        IReadOnlyList<string>? eventTypes = string.IsNullOrWhiteSpace(eventType)
            ? null
            : eventType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Results.Ok(eventLog.Query(new RuntimeEventQuery(
            deviceId,
            eventTypes,
            afterSequence,
            limit ?? 100)));
    }

    private static async Task<IResult> UpdateDeviceAsync(
        string deviceId,
        CreateDeviceRequest request,
        ISimulationRegistry registry,
        IDeviceCatalogRepository repository,
        IndustrialSimDbContext db,
        SecretRedactor redactor,
        CancellationToken cancellationToken)
    {
        using var trace = IndustrialSimOperation.Start("industrial.device.update", redactor, deviceId);
        if (!deviceId.Equals(request.Id, StringComparison.OrdinalIgnoreCase))
        {
            trace.SetResult("rejected", "deviceIdMismatch");
            return IndustrialSimProblemDetails.Result(400, "Validation failed", "The request device id must match the route device id.", "deviceIdMismatch");
        }
        if (registry.Get(deviceId).Host.IsRunning)
        {
            trace.SetResult("rejected", "deviceMustBeStopped");
            return IndustrialSimProblemDetails.Result(409, "Simulation conflict", $"Simulation '{deviceId}' must be stopped before its definition can be replaced.", "deviceMustBeStopped");
        }
        if (request.DataPoints.Count == 0)
        {
            trace.SetResult("rejected", "invalidDevice");
            return IndustrialSimProblemDetails.Result(400, "Validation failed", "At least one data point is required.", "invalidDevice");
        }

        var definition = DeviceLaunchRequestMapper.ToDefinition(request);
        if (definition.Behavior is not null)
        {
            try { BuiltInDeviceProfiles.Validate(definition, definition.Behavior); }
            catch (ArgumentException exception)
            {
                trace.SetResult("rejected", "invalidBehaviorProfile");
                return IndustrialSimProblemDetails.Result(400, "Invalid behavior profile", exception.Message, "invalidBehaviorProfile");
            }
        }
        var launch = DeviceLaunchRequestMapper.ToLaunch(request);
        var item = new DeviceCatalogItem(request.Id, DeviceLaunchDocumentSerializer.Serialize(launch), "Stopped", request.Version);
        var handle = await registry.ReplaceAsync(deviceId, launch, async token =>
        {
            await repository.UpsertAsync(item, token);
            await db.CommitAsync(token);
        }, cancellationToken);
        trace.SetResult("success");
        return Results.Ok(await repository.FindAsync(handle.DeviceId, cancellationToken) is { } saved
            ? new { details = Summary(handle), version = saved.Version }
            : new { details = Summary(handle), version = request.Version + 1 });
    }

    private static async Task<IResult> RemoveDeviceAsync(
        string deviceId,
        ISimulationRegistry registry,
        IDeviceCatalogRepository repository,
        IndustrialSimDbContext db,
        SecretRedactor redactor,
        CancellationToken cancellationToken)
    {
        using var trace = IndustrialSimOperation.Start("industrial.device.remove", redactor, deviceId);
        registry.Get(deviceId);
        await repository.RemoveAsync(deviceId, cancellationToken);
        await db.CommitAsync(cancellationToken);
        await registry.RemoveAsync(deviceId, cancellationToken);
        trace.SetResult("success");
        return Results.NoContent();
    }

    private static async Task<IResult> RunLifecycleAsync(
        string deviceId,
        string operation,
        ISimulationRegistry registry,
        IDeviceCatalogRepository repository,
        IndustrialSimDbContext db,
        SecretRedactor redactor,
        CancellationToken cancellationToken)
    {
        using var trace = IndustrialSimOperation.Start("industrial.device.lifecycle", redactor, deviceId, operation);
        if (operation.Equals("start", StringComparison.OrdinalIgnoreCase) || operation.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            var desired = operation.Equals("start", StringComparison.OrdinalIgnoreCase) ? "Running" : "Stopped";
            await repository.SetDesiredStateAsync(deviceId, desired, cancellationToken);
        }
        switch (operation.ToLowerInvariant())
        {
            case "start": await registry.StartAsync(deviceId, cancellationToken); break;
            case "stop": await registry.StopAsync(deviceId, cancellationToken); break;
            case "pause": registry.Get(deviceId).Host.Engine.Pause(); break;
            case "reset": registry.Get(deviceId).Host.Reset(); break;
            default:
                trace.SetResult("rejected", "invalidLifecycleOperation");
                return IndustrialSimProblemDetails.Result(400, "Invalid operation", $"Lifecycle operation '{operation}' is not supported.", "invalidLifecycleOperation");
        }
        trace.SetResult("success");
        return Results.Ok(Runtime(registry.Get(deviceId).Host));
    }

    private static async Task<IResult> RunBatchAsync(
        BatchLifecycleRequest request,
        ISimulationRegistry registry,
        IDeviceCatalogRepository repository,
        ClaimsPrincipal user,
        IAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (request.Operation.Equals("remove", StringComparison.OrdinalIgnoreCase) &&
            !(await authorization.AuthorizeAsync(user, IndustrialPolicies.Admin)).Succeeded)
            return IndustrialSimProblemDetails.Result(403, "Forbidden", "Batch removal requires the Admin role.", "adminRoleRequired");
        var operation = request.Operation.ToLowerInvariant();
        if (operation is "start" or "stop")
        {
            var desiredState = operation == "start" ? "Running" : "Stopped";
            foreach (var deviceId in request.DeviceIds.Distinct(StringComparer.OrdinalIgnoreCase))
                await repository.SetDesiredStateAsync(deviceId, desiredState, cancellationToken);
        }
        var result = operation switch
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

    private static IResult WriteState(
        string deviceId,
        string dataPoint,
        JsonElement value,
        ISimulationRegistry registry,
        SecretRedactor redactor)
    {
        using var trace = IndustrialSimOperation.Start("industrial.state.write", redactor, deviceId, "write");
        var result = registry.Get(deviceId).Host.Runtime.Write(dataPoint, JsonValue(value));
        trace.SetResult(result.Succeeded ? "success" : "rejected", result.Succeeded ? null : "stateWriteRejected");
        return result.Succeeded
            ? Results.Ok(result)
            : IndustrialSimProblemDetails.Result(400, "State write rejected", result.Error ?? "State write was rejected.", "stateWriteRejected");
    }

    private static IResult ActivateFault(
        string deviceId,
        FaultRequest request,
        ISimulationRegistry registry,
        SecretRedactor redactor)
    {
        using var trace = IndustrialSimOperation.Start("industrial.fault.activate", redactor, deviceId, "activate");
        if (!Enum.TryParse<FaultCategory>(request.Category, true, out var category))
        {
            trace.SetResult("rejected", "invalidFaultCategory");
            return IndustrialSimProblemDetails.Result(400, "Invalid fault", $"Unknown fault category '{request.Category}'.", "invalidFaultCategory");
        }
        var host = registry.Get(deviceId).Host;
        var fault = new FaultSpec(
            string.IsNullOrWhiteSpace(request.Id) ? $"fault-{Guid.NewGuid():N}" : request.Id,
            category, deviceId, request.Target, host.Engine.CurrentTime.Elapsed,
            request.DurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            request.Type, request.Metadata);
        host.ActivateFault(fault);
        trace.SetResult("success");
        return Results.Accepted(value: fault);
    }

    private static IResult RecoverFault(
        string deviceId,
        string faultId,
        ISimulationRegistry registry,
        SecretRedactor redactor)
    {
        using var trace = IndustrialSimOperation.Start("industrial.fault.recover", redactor, deviceId, "recover");
        if (registry.Get(deviceId).Host.RecoverFault(faultId))
        {
            trace.SetResult("success");
            return Results.Ok();
        }
        trace.SetResult("rejected", "faultNotFound");
        return IndustrialSimProblemDetails.Result(404, "Fault not found", $"Fault '{faultId}' is not active.", "faultNotFound");
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
        SecretRedactor redactor,
        CancellationToken cancellationToken)
    {
        using var trace = IndustrialSimOperation.Start("industrial.scenario.start", redactor, deviceId, "start");
        var scenario = await repository.FindAsync(scenarioId, cancellationToken);
        if (scenario is null)
        {
            trace.SetResult("rejected", "scenarioNotFound");
            return IndustrialSimProblemDetails.Result(404, "Scenario not found", $"Scenario '{scenarioId}' was not found.", "scenarioNotFound");
        }
        var host = registry.Get(deviceId).Host;
        host.RunScenario(scenario.Yaml);
        host.Update();
        trace.SetResult("success");
        return Results.Ok(new { scenario = host.ActiveScenarioName, running = true });
    }

    private static async Task<IResult> RunInlineScenarioAsync(
        string deviceId,
        HttpRequest request,
        ISimulationRegistry registry,
        SecretRedactor redactor)
    {
        using var trace = IndustrialSimOperation.Start("industrial.scenario.start", redactor, deviceId, "start");
        using var reader = new StreamReader(request.Body);
        var host = registry.Get(deviceId).Host;
        host.RunScenario(await reader.ReadToEndAsync());
        host.Update();
        trace.SetResult("success");
        return Results.Ok(new { scenario = host.ActiveScenarioName, running = true });
    }

    private static IResult StopScenario(
        string deviceId,
        ISimulationRegistry registry,
        SecretRedactor redactor)
    {
        using var trace = IndustrialSimOperation.Start("industrial.scenario.stop", redactor, deviceId, "stop");
        if (registry.Get(deviceId).Host.StopScenario())
        {
            trace.SetResult("success");
            return Results.Ok(new { running = false });
        }
        trace.SetResult("rejected", "scenarioNotRunning");
        return IndustrialSimProblemDetails.Result(404, "Scenario not running", "No scenario is running.", "scenarioNotRunning");
    }

    private static DataPointDefinition ToDefinition(CreateDataPointRequest request)
    {
        if (!Enum.TryParse<DataType>(request.DataType, true, out var dataType)) throw new ArgumentException($"Unknown data type '{request.DataType}'.");
        if (!Enum.TryParse<DataPointAccess>(request.Access, true, out var access)) throw new ArgumentException($"Unknown access mode '{request.Access}'.");
        return new DataPointDefinition(request.Name, dataType, access, request.Initial is { } initial ? JsonValue(initial) : null, request.Unit, request.Description);
    }

    private static DeviceDefinition ToDefinition(CreateDeviceRequest request) => new(
        new DeviceId(request.Id),
        request.Type,
        request.DataPoints.Select(ToDefinition),
        request.Commands?.Select(name => new CommandDefinition(name)),
        request.Events?.Select(name => new EventDefinition(name)),
        request.Behavior is null ? null : new DeviceBehaviorDefinition(request.Behavior.Profile, request.Behavior.Parameters));

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
