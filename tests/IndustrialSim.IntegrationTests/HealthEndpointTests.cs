using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialSim.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IndustrialSim.IntegrationTests;

[Collection(WebApplicationFactoryEnvironmentCollection.CollectionName)]
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Liveness_and_readiness_have_distinct_dependency_semantics_and_redacted_responses()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"industrial-sim-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var yamlPath = Path.Combine(directory, "device.yaml");
        var databasePath = Path.Combine(directory, "industrial-sim.db");
        await File.WriteAllTextAsync(yamlPath, """
            device:
              id: health-device
              type: sensor
              datapoints:
                value: { type: double, initial: 1, access: readwrite }
            protocols:
              opcua: { enabled: false }
              modbus: { enabled: false }
            web: { enabled: true, port: 8080 }
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

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            var readyJson = await ready.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("healthy", readyJson.GetProperty("status").GetString());

            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/devices/health-device/stop", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

            File.Delete(databasePath);
            File.Delete(yamlPath);
            Directory.Delete(directory);

            var unavailable = await client.GetAsync("/health/ready");
            var unavailableBody = await unavailable.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            Assert.DoesNotContain(databasePath, unavailableBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Data Source", unavailableBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ConnectionStrings", unavailableBody, StringComparison.OrdinalIgnoreCase);
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
