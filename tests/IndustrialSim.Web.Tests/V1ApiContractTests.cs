using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
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
            portBindings = new[] { new { protocol = "modbus", port = FreePort() } }
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
            yaml = "scenario:\n  name: startup\n  steps:\n    - at: 0s\n      set:\n        device: api-pump\n        datapoint: speed\n        value: 900"
        });
        Assert.Equal(HttpStatusCode.OK, scenario.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/api-pump/scenarios/startup/start", null)).StatusCode);
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
    public async Task Device_details_and_stopped_only_definition_replacement_are_available()
    {
        await using var fixture = await V1Fixture.StartAsync();
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/v1/devices", DeviceRequest("editable"))).StatusCode);
        using var details = JsonDocument.Parse(await fixture.Client.GetStringAsync("/api/v1/devices/editable"));
        Assert.Equal(1, details.RootElement.GetProperty("definition").GetProperty("version").GetInt64());

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
        portBindings = new[] { new { protocol = "modbus", port } }
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
