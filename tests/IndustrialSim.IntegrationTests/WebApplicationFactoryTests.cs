using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using IndustrialSim.Web;

namespace IndustrialSim.IntegrationTests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class WebApplicationFactoryEnvironmentCollection
{
    public const string CollectionName = "WebApplicationFactory environment";
}

[Collection(WebApplicationFactoryEnvironmentCollection.CollectionName)]
public sealed class WebApplicationFactoryTests
{
    [Fact]
    public async Task Production_composition_root_serves_runtime_openapi_headers_and_problem_details()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"industrial-sim-factory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var yamlPath = Path.Combine(directory, "device.yaml");
        var databasePath = Path.Combine(directory, "industrial-sim.db");
        await File.WriteAllTextAsync(yamlPath, """
            device:
              id: factory-device
              type: sensor
              datapoints:
                value: { type: double, initial: 1, access: readwrite }
            protocols:
              opcua:
                enabled: false
              modbus:
                enabled: false
            web:
              enabled: true
              port: 8080
            """);

        try
        {
            using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
            {
                ["IndustrialSim__DeviceConfig"] = yamlPath,
                ["ConnectionStrings__IndustrialSim"] = $"Data Source={databasePath};Pooling=False",
                ["Auth__Mode"] = "Disabled",
                ["IndustrialSim__Restore__AutoStartDesiredRunning"] = "false"
            });
            await using var factory = new IndustrialSimWebFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var devices = await client.GetAsync("/api/v1/devices");
            Assert.Equal(HttpStatusCode.OK, devices.StatusCode);
            Assert.Contains("factory-device", await devices.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal("nosniff", Assert.Single(devices.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("DENY", Assert.Single(devices.Headers.GetValues("X-Frame-Options")));

            using (var state = JsonDocument.Parse(await client.GetStringAsync("/api/v1/devices/factory-device/state")))
                Assert.Equal(1d, state.RootElement.GetProperty("value").GetDouble());

            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/devices/factory-device/state/value", 2d)).StatusCode);
            JsonElement events = default;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                do
                {
                    events = await client.GetFromJsonAsync<JsonElement>(
                        "/api/v1/devices/factory-device/events?eventType=DataPointChanged&limit=1",
                        timeout.Token);
                    if (events.GetArrayLength() == 0) await Task.Delay(10, timeout.Token);
                } while (events.GetArrayLength() == 0);
            }
            Assert.Equal(2d, events[0].GetProperty("data").GetProperty("newValue").GetDouble());

            var openApi = await client.GetAsync("/openapi/v1.json");
            Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
            Assert.Equal("application/json", openApi.Content.Headers.ContentType?.MediaType);
            Assert.Contains("/api/v1/devices", await openApi.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var invalidOperation = await client.PostAsync("/api/v1/devices/factory-device/not-supported", null);
            Assert.Equal(HttpStatusCode.BadRequest, invalidOperation.StatusCode);
            Assert.Equal("application/problem+json", invalidOperation.Content.Headers.ContentType?.MediaType);
            var problem = await invalidOperation.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("invalidLifecycleOperation", problem.GetProperty("errorCode").GetString());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class IndustrialSimWebFactory : WebApplicationFactory<WebApplicationMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var (name, value) in values)
            {
                _previous[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
