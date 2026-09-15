using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialSim.Application.Catalogs;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Web;
using IndustrialSim.Web.Api.V1;
using IndustrialSim.Web.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialSim.Web.Tests;

public sealed class V1ApiContractTests
{
    [Fact]
    public async Task Runtime_events_use_bounded_structured_log_filters_and_sequence_cursor()
    {
        await using var fixture = await V1Fixture.StartAsync();
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/v1/devices", DeviceRequest("event-device"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PutAsJsonAsync("/api/v1/devices/event-device/state/speed", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PutAsJsonAsync("/api/v1/devices/event-device/state/speed", 2)).StatusCode);

        JsonElement events = default;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        do
        {
            events = await fixture.Client.GetFromJsonAsync<JsonElement>(
                "/api/v1/devices/event-device/events?eventType=DataPointChanged&limit=1",
                timeout.Token);
            if (events.GetArrayLength() == 0) await Task.Delay(10, timeout.Token);
        } while (events.GetArrayLength() == 0);

        var latest = events[0];
        Assert.Equal("DataPointChanged", latest.GetProperty("eventType").GetString());
        Assert.Equal(2, latest.GetProperty("data").GetProperty("newValue").GetInt32());
        var sequence = latest.GetProperty("sequence").GetInt64();
        var after = await fixture.Client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/devices/event-device/events?afterSequence={sequence}&limit=10");
        Assert.Equal(0, after.GetArrayLength());
    }

    [Fact]
    public async Task Pump_commands_drive_behavior_and_publish_datapoint_events()
    {
        await using var fixture = await V1Fixture.StartAsync();
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/v1/devices", PumpRequest("command-pump"))).StatusCode);

        var stoppedCommand = await fixture.Client.PostAsync("/api/v1/devices/command-pump/commands/start", null);
        Assert.Equal(HttpStatusCode.Conflict, stoppedCommand.StatusCode);
        using (var problem = JsonDocument.Parse(await stoppedCommand.Content.ReadAsStringAsync()))
            Assert.Equal("deviceNotRunning", problem.RootElement.GetProperty("errorCode").GetString());

        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/command-pump/start", null)).StatusCode);
        var unknownCommand = await fixture.Client.PostAsync("/api/v1/devices/command-pump/commands/missing", null);
        Assert.Equal(HttpStatusCode.BadRequest, unknownCommand.StatusCode);
        using (var problem = JsonDocument.Parse(await unknownCommand.Content.ReadAsStringAsync()))
            Assert.Equal("commandNotFound", problem.RootElement.GetProperty("errorCode").GetString());

        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/command-pump/commands/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/command-pump/tick/1", null)).StatusCode);

        JsonElement events = default;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        do
        {
            events = await fixture.Client.GetFromJsonAsync<JsonElement>(
                "/api/v1/devices/command-pump/events?eventType=DataPointChanged&limit=100",
                timeout.Token);
            var observed = events.EnumerateArray()
                .Select(item => item.GetProperty("data").GetProperty("dataPoint").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (new[] { "running", "speed", "temperature", "pressure" }.All(observed.Contains)) break;
            await Task.Delay(10, timeout.Token);
        } while (true);

        var changedPoints = events.EnumerateArray()
            .Select(item => item.GetProperty("data").GetProperty("dataPoint").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("running", changedPoints);
        Assert.Contains("speed", changedPoints);
        Assert.Contains("temperature", changedPoints);
        Assert.Contains("pressure", changedPoints);

        var state = await fixture.Client.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/v1/devices/command-pump/state");
        Assert.True(state!["running"].GetBoolean());
        Assert.True(state["speed"].GetInt32() > 0);
        Assert.True(state["temperature"].GetDouble() > 25d);
        Assert.True(state["pressure"].GetDouble() > 0d);
    }

    [Fact]
    public async Task V1_devices_protocols_scenarios_state_and_openapi_are_available()
    {
        await using var fixture = await V1Fixture.StartAsync();
        var create = await fixture.Client.PostAsJsonAsync("/api/v1/devices", new
        {
            id = "api-pump",
            type = "custom",
            deterministic = true,
            seed = 9,
            dataPoints = new[] { new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 } },
            protocols = new
            {
                modbus = new
                {
                    enabled = true,
                    port = FreePort(),
                    mappings = new[] { new { dataPoint = "speed", kind = "holding", address = 100, dataType = "int32", access = "readwrite" } }
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/api-pump/start", null)).StatusCode);
        var write = await fixture.Client.PutAsJsonAsync("/api/v1/devices/api-pump/state/speed", 750);
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        var state = await fixture.Client.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/v1/devices/api-pump/state");
        Assert.Equal(750, state!["speed"].GetInt32());

        var protocols = await fixture.Client.GetStringAsync("/api/v1/protocols");
        Assert.Contains("modbus", protocols, StringComparison.OrdinalIgnoreCase);
        var scenario = await fixture.Client.PutAsJsonAsync("/api/v1/scenarios/startup", new
        {
            name = "Startup",
            yaml = "scenario:\n  name: startup\n  target:\n    type: custom\n  steps:\n    - at: 0s\n      set:\n        datapoint: speed\n        value: 900"
        });
        Assert.Equal(HttpStatusCode.OK, scenario.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/api-pump/scenarios/startup/start", null)).StatusCode);
        state = await fixture.Client.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/v1/devices/api-pump/state");
        Assert.Equal(900, state!["speed"].GetInt32());
        Assert.Contains("startup", await fixture.Client.GetStringAsync("/api/v1/scenarios"), StringComparison.OrdinalIgnoreCase);

        var openApi = await fixture.Client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/v1/devices", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/protocols", openApi, StringComparison.Ordinal);
        Assert.Contains("/api/v1/scenarios", openApi, StringComparison.Ordinal);
    }

    [Fact]
    public async Task V1_errors_are_problem_details_with_stable_error_codes()
    {
        await using var fixture = await V1Fixture.StartAsync();
        await fixture.Client.PostAsJsonAsync("/api/v1/devices", DeviceRequest("duplicate"));
        var response = await fixture.Client.PostAsJsonAsync("/api/v1/devices", DeviceRequest("duplicate"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("duplicateDeviceId", problem.RootElement.GetProperty("errorCode").GetString());

        var missing = await fixture.Client.GetAsync("/api/v1/devices/absent/state");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var missingProblem = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Equal("simulationNotFound", missingProblem.RootElement.GetProperty("errorCode").GetString());

        var reservedPort = FreePort();
        var firstPort = await fixture.Client.PostAsJsonAsync("/api/v1/devices", DeviceRequest("port-owner", reservedPort));
        Assert.Equal(HttpStatusCode.Created, firstPort.StatusCode);
        var conflict = await fixture.Client.PostAsJsonAsync("/api/v1/devices", DeviceRequest("port-conflict", reservedPort));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var conflictProblem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal("portConflict", conflictProblem.RootElement.GetProperty("errorCode").GetString());

        var missingMappings = await fixture.Client.PostAsJsonAsync("/api/v1/devices", new
        {
            id = "missing-mappings", type = "custom",
            dataPoints = new[] { new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 } },
            protocols = new { modbus = new { enabled = true, port = FreePort() } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingMappings.StatusCode);
        using var mappingProblem = JsonDocument.Parse(await missingMappings.Content.ReadAsStringAsync());
        Assert.Equal("modbusMappingRequired", mappingProblem.RootElement.GetProperty("errorCode").GetString());

        var unknownProtocol = await fixture.Client.PostAsJsonAsync("/api/v1/devices", new
        {
            id = "unknown-protocol", type = "custom",
            dataPoints = new[] { new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 } },
            portBindings = new[] { new { protocol = "mqtt", port = FreePort() } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknownProtocol.StatusCode);
        using var protocolProblem = JsonDocument.Parse(await unknownProtocol.Content.ReadAsStringAsync());
        Assert.Equal("unknownProtocol", protocolProblem.RootElement.GetProperty("errorCode").GetString());

        var invalidOpcUa = await fixture.Client.PostAsJsonAsync("/api/v1/devices", new
        {
            id = "invalid-opcua", type = "custom",
            dataPoints = new[] { new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 } },
            protocols = new { opcua = new { enabled = true, endpoint = "http://localhost:4840" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidOpcUa.StatusCode);
        using var opcUaProblem = JsonDocument.Parse(await invalidOpcUa.Content.ReadAsStringAsync());
        Assert.Equal("invalidOpcUaConfiguration", opcUaProblem.RootElement.GetProperty("errorCode").GetString());

        using var occupied = new TcpListener(IPAddress.Any, 0);
        occupied.Start();
        var occupiedPort = ((IPEndPoint)occupied.LocalEndpoint).Port;
        var listenerDevice = await fixture.Client.PostAsJsonAsync("/api/v1/devices", new
        {
            id = "listener-conflict", type = "custom",
            dataPoints = new[] { new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 } },
            protocols = new
            {
                modbus = new
                {
                    enabled = true,
                    port = occupiedPort,
                    mappings = new[] { new { dataPoint = "speed", kind = "holding", address = 100, dataType = "int32", access = "readwrite" } }
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, listenerDevice.StatusCode);
        var listenerStart = await fixture.Client.PostAsync("/api/v1/devices/listener-conflict/start", null);
        Assert.Equal(HttpStatusCode.Conflict, listenerStart.StatusCode);
        using var listenerProblem = JsonDocument.Parse(await listenerStart.Content.ReadAsStringAsync());
        Assert.Equal("protocolStartFailed", listenerProblem.RootElement.GetProperty("errorCode").GetString());
        occupied.Stop();
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/listener-conflict/start", null)).StatusCode);
    }

    [Fact]
    public async Task Legacy_api_remains_available_with_deprecation_headers()
    {
        await using var fixture = await V1Fixture.StartAsync();
        var response = await fixture.Client.GetAsync("/api/state");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", response.Headers.GetValues("Deprecation").Single());
        Assert.True(response.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task Batch_start_and_stop_persist_desired_lifecycle_state()
    {
        await using var fixture = await V1Fixture.StartAsync();
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/v1/devices", DeviceRequest("batch-device"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsJsonAsync("/api/v1/devices/batch", new
        {
            deviceIds = new[] { "batch-device" },
            operation = "start"
        })).StatusCode);
        Assert.Equal("Running", await fixture.DesiredStateAsync("batch-device"));

        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsJsonAsync("/api/v1/devices/batch", new
        {
            deviceIds = new[] { "batch-device" },
            operation = "stop"
        })).StatusCode);
        Assert.Equal("Stopped", await fixture.DesiredStateAsync("batch-device"));
    }

    [Fact]
    public async Task Device_details_and_stopped_only_definition_replacement_are_available()
    {
        await using var fixture = await V1Fixture.StartAsync();
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/v1/devices", DeviceRequest("editable"))).StatusCode);
        using var details = JsonDocument.Parse(await fixture.Client.GetStringAsync("/api/v1/devices/editable"));
        Assert.Equal(1, details.RootElement.GetProperty("definition").GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Array, details.RootElement.GetProperty("definition").GetProperty("commands").ValueKind);
        Assert.Equal(JsonValueKind.Array, details.RootElement.GetProperty("definition").GetProperty("events").ValueKind);

        await fixture.Client.PostAsync("/api/v1/devices/editable/start", null);
        var runningEdit = await fixture.Client.PutAsJsonAsync("/api/v1/devices/editable", new
        {
            id = "editable", type = "custom", deterministic = true, seed = 2, version = 1,
            dataPoints = new[] { new { name = "temperature", dataType = "Double", access = "ReadWrite", initial = 21.5 } }
        });
        Assert.Equal(HttpStatusCode.Conflict, runningEdit.StatusCode);
        using (var problem = JsonDocument.Parse(await runningEdit.Content.ReadAsStringAsync()))
            Assert.Equal("deviceMustBeStopped", problem.RootElement.GetProperty("errorCode").GetString());

        await fixture.Client.PostAsync("/api/v1/devices/editable/stop", null);
        var update = await fixture.Client.PutAsJsonAsync("/api/v1/devices/editable", new
        {
            id = "editable", type = "sensor", deterministic = true, seed = 2, version = 1,
            dataPoints = new[] { new { name = "temperature", dataType = "Double", access = "ReadWrite", initial = 21.5 } }
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updated = JsonDocument.Parse(await fixture.Client.GetStringAsync("/api/v1/devices/editable"));
        Assert.Equal("sensor", updated.RootElement.GetProperty("definition").GetProperty("type").GetString());
        Assert.Equal(2, updated.RootElement.GetProperty("definition").GetProperty("version").GetInt64());
        Assert.Equal(21.5, updated.RootElement.GetProperty("state").GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task Built_in_profiles_are_discoverable_validated_and_drive_runtime_behavior()
    {
        await using var fixture = await V1Fixture.StartAsync();
        var profiles = await fixture.Client.GetStringAsync("/api/v1/device-profiles");
        Assert.Contains("ratedSpeed", profiles, StringComparison.Ordinal);
        Assert.Contains("sensor", profiles, StringComparison.OrdinalIgnoreCase);

        var create = await fixture.Client.PostAsJsonAsync("/api/v1/devices", new
        {
            id = "configured-pump",
            type = "pump",
            deterministic = true,
            seed = 4,
            dataPoints = new object[]
            {
                new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 },
                new { name = "temperature", dataType = "Double", access = "Read", initial = 25d },
                new { name = "pressure", dataType = "Double", access = "Read", initial = 0d },
                new { name = "running", dataType = "Boolean", access = "Read", initial = false },
                new { name = "alarm", dataType = "Boolean", access = "Read", initial = false }
            },
            commands = new[] { "start", "stop" },
            events = new[] { "PumpStarted", "PumpStopped", "Overheated" },
            behavior = new { profile = "pump", parameters = new { ratedSpeed = 1000, accelerationSeconds = 2, maxPressure = 4, heatingRatePerSecond = 2, coolingRatePerSecond = 0.2, overheatTemperature = 90 } }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        await fixture.Client.PostAsync("/api/v1/devices/configured-pump/start", null);
        await fixture.Client.PutAsJsonAsync("/api/v1/scenarios/start-pump", new
        {
            name = "Start pump",
            yaml = "scenario:\n  name: start-pump\n  target:\n    type: pump\n  steps:\n    - at: 0s\n      command:\n        name: start"
        });
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/configured-pump/scenarios/start-pump/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/configured-pump/tick/1", null)).StatusCode);
        var state = await fixture.Client.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/v1/devices/configured-pump/state");
        Assert.Equal(500, state!["speed"].GetInt32());
        Assert.Equal(27d, state["temperature"].GetDouble());

        var invalid = await fixture.Client.PostAsJsonAsync("/api/v1/devices", new
        {
            id = "invalid-pump",
            type = "pump",
            dataPoints = new[] { new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 } },
            commands = new[] { "start", "stop" },
            behavior = new { profile = "pump", parameters = new { } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var problem = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.Equal("invalidBehaviorProfile", problem.RootElement.GetProperty("errorCode").GetString());
    }

    private static object DeviceRequest(string id) => new
    {
        id,
        type = "custom",
        deterministic = true,
        seed = 1,
        dataPoints = new[] { new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 } }
    };

    private static object DeviceRequest(string id, int port) => new
    {
        id,
        type = "custom",
        deterministic = true,
        seed = 1,
        dataPoints = new[] { new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 } },
        protocols = new
        {
            modbus = new
            {
                enabled = true,
                port,
                mappings = new[] { new { dataPoint = "speed", kind = "holding", address = 100, dataType = "int32", access = "readwrite" } }
            }
        }
    };

    private static object PumpRequest(string id) => new
    {
        id,
        type = "pump",
        deterministic = true,
        seed = 1,
        dataPoints = new object[]
        {
            new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = (object)0 },
            new { name = "temperature", dataType = "Double", access = "Read", initial = (object)25d },
            new { name = "pressure", dataType = "Double", access = "Read", initial = (object)0d },
            new { name = "running", dataType = "Boolean", access = "Read", initial = (object)false },
            new { name = "alarm", dataType = "Boolean", access = "Read", initial = (object)false }
        },
        commands = new[] { "start", "stop" },
        behavior = new { profile = "pump", parameters = new { } }
    };

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class V1Fixture(WebApplication app, HttpClient client, string databasePath, SimulationRegistry registry) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async Task<string?> DesiredStateAsync(string deviceId)
        {
            await using var scope = app.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IDeviceCatalogRepository>().FindAsync(deviceId))?.DesiredState;
        }

        public static async Task<V1Fixture> StartAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"industrial-sim-web-{Guid.NewGuid():N}.db");
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var registry = new SimulationRegistry();
            var legacy = SimulationHost.Create(new DeviceDefinition(
                new DeviceId("legacy"), "custom",
                [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)], [], []),
                new SimulationHostOptions(true, 1));
            await registry.AddAsync(legacy);
            builder.Services.AddIndustrialSimControlPlane(registry, $"Data Source={databasePath};Pooling=False");
            var app = builder.Build();
            app.UseIndustrialSimProblemDetails();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapIndustrialSimApi(legacy);
            app.MapIndustrialSimV1Api();
            app.MapRuntimeHub();
            app.MapOpenApi("/openapi/v1.json");
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new V1Fixture(app, new HttpClient { BaseAddress = new Uri(address) }, databasePath, registry);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            await registry.DisposeAsync();
            File.Delete(databasePath);
        }
    }
}
