using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialSim.Observability.Tracing;
using IndustrialSim.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace IndustrialSim.IntegrationTests;

[Collection(WebApplicationFactoryEnvironmentCollection.CollectionName)]
public sealed class TraceCorrelationTests
{
    [Fact]
    public async Task Http_state_write_has_a_child_operation_span_and_correlated_runtime_event()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == IndustrialSimActivitySource.Name || source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Enqueue
        };
        ActivitySource.AddActivityListener(listener);

        var directory = Path.Combine(Path.GetTempPath(), $"industrial-sim-trace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var yamlPath = Path.Combine(directory, "device.yaml");
        var databasePath = Path.Combine(directory, "industrial-sim.db");
        await File.WriteAllTextAsync(yamlPath, """
            device:
              id: trace-device
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
                ["IndustrialSim__Restore__AutoStartDesiredRunning"] = "false",
                ["OpenTelemetry__Otlp__Endpoint"] = null
            });
            await using var factory = new IndustrialSimWebFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.NotNull(factory.Services.GetService<TracerProvider>());

            Assert.Equal(
                HttpStatusCode.OK,
                (await client.PutAsJsonAsync("/api/v1/devices/trace-device/state/value", 2d)).StatusCode);
            var envelope = await WaitForEventAsync(client);
            await WaitUntilAsync(() => activities.Any(activity => activity.DisplayName == "industrial.state.write"));

            var operation = Assert.Single(activities, activity => activity.DisplayName == "industrial.state.write");
            var server = Assert.Single(activities, activity =>
                activity.Kind == ActivityKind.Server &&
                activity.TraceId == operation.TraceId &&
                activity.SpanId == operation.ParentSpanId);
            Assert.Equal(operation.TraceId.ToHexString(), envelope.GetProperty("traceId").GetString());
            Assert.Equal(operation.SpanId.ToHexString(), envelope.GetProperty("spanId").GetString());
            Assert.Equal(server.TraceId, operation.TraceId);
            Assert.DoesNotContain(operation.Tags, tag => tag.Key.Contains("value", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<JsonElement> WaitForEventAsync(HttpClient client)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var events = await client.GetFromJsonAsync<JsonElement>(
                "/api/v1/devices/trace-device/events?eventType=DataPointChanged&limit=1",
                timeout.Token);
            if (events.GetArrayLength() > 0) return events[0].Clone();
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
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
