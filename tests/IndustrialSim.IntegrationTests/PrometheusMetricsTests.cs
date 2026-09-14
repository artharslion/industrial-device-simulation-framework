using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using IndustrialSim.Web;
using IndustrialSim.Core.Domain;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;
using IndustrialSim.Web.Hubs;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialSim.IntegrationTests;

[Collection(WebApplicationFactoryEnvironmentCollection.CollectionName)]
public sealed class PrometheusMetricsTests
{
    [Fact]
    public async Task Metrics_endpoint_is_anonymous_and_exposes_the_bounded_runtime_contract()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"industrial-sim-metrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var yamlPath = Path.Combine(directory, "device.yaml");
        var databasePath = Path.Combine(directory, "industrial-sim.db");
        await File.WriteAllTextAsync(yamlPath, """
            device:
              id: metrics-device
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
                ["Auth__Mode"] = "LocalIdentity",
                ["IndustrialSim__Restore__AutoStartDesiredRunning"] = "false"
            });
            await using var factory = new IndustrialSimWebFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var registry = factory.Services.GetRequiredService<ISimulationRegistry>();
            var host = registry.Get("metrics-device").Host;
            var broker = factory.Services.GetRequiredService<RuntimeStreamBroker>();
            await using var unreadSubscriber = broker.Subscribe();

            host.State.SetInternal(new DataPointId("value"), 2d, host.Engine.CurrentTime);
            host.RunScenario("""
                scenario:
                  name: metrics-scenario
                  steps:
                    - at: 0s
                      set:
                        device: metrics-device
                        datapoint: value
                        value: 3
                """);
            await WaitUntilAsync(() => Convert.ToDouble(host.State.Get(new DataPointId("value"))!.Value) == 3d);
            for (var value = 4; value <= 400; value++)
                host.State.SetInternal(new DataPointId("value"), (double)value, host.Engine.CurrentTime);
            await WaitUntilAsync(() => broker.DroppedEvents > 0);
            var fault = new FaultSpec("metrics-fault", FaultCategory.Data, "metrics-device", "value", host.Engine.CurrentTime.Elapsed, Type: "freeze");
            host.ActivateFault(fault);

            var response = await client.GetAsync("/metrics");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
            Assert.Contains("industrial_simulation_ticks_total", body, StringComparison.Ordinal);
            Assert.Contains("industrial_device_state_changes_total", body, StringComparison.Ordinal);
            Assert.Contains("industrial_scenario_actions_total", body, StringComparison.Ordinal);
            Assert.Contains("industrial_faults_active", body, StringComparison.Ordinal);
            Assert.Contains("industrial_protocol_connections", body, StringComparison.Ordinal);
            Assert.Contains("industrial_protocol_errors_total", body, StringComparison.Ordinal);
            Assert.Contains("industrial_stream_events_dropped_total", body, StringComparison.Ordinal);
            Assert.Contains("industrial_scenario_actions_total{action=\"set\"} 1", body, StringComparison.Ordinal);
            Assert.Contains("industrial_faults_active{category=\"data\"} 1", body, StringComparison.Ordinal);
            Assert.Matches("industrial_device_state_changes_total [1-9][0-9]*", body);
            Assert.Matches("industrial_simulation_ticks_total\\{mode=\"realtime\"\\} [1-9][0-9]*", body);
            Assert.Matches("industrial_stream_events_dropped_total\\{stream=\"signalr\",stage=\"subscriber\"\\} [1-9][0-9]*", body);
            Assert.DoesNotContain("metrics-device", body, StringComparison.Ordinal);

            Assert.True(host.RecoverFault(fault.Id));
            await WaitUntilAsync(async () => (await client.GetStringAsync("/metrics"))
                .Contains("industrial_faults_active{category=\"data\"} 0", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition()) await Task.Delay(10, timeout.Token);
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
