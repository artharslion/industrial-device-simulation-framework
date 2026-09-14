using System.Text;
using IndustrialSim.Core.Domain;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;
using IndustrialSim.Observability.Events;
using IndustrialSim.Observability.Metrics;
using IndustrialSim.Observability.Security;
using Prometheus;

namespace IndustrialSim.Observability.Tests;

public sealed class IndustrialSimMetricsTests
{
    [Fact]
    public async Task Collector_exposes_exact_names_and_bounded_labels()
    {
        var registry = Prometheus.Metrics.NewCustomRegistry();
        var metrics = new IndustrialSimMetrics(registry);

        metrics.Observe(new DataPointChanged(
            SimulationTime.Zero,
            new DeviceId("pump-1"),
            new DataPointId("speed"),
            null,
            ScalarValue.Create(DataType.Int32, 1)));
        metrics.Observe(new ScenarioActionObservation("pump-1", "startup", "set", SimulationTime.Zero));
        metrics.Observe(new FaultEvent(new FaultSpec("fault-1", FaultCategory.Data, "pump-1", "speed", TimeSpan.Zero), FaultLifecycle.Active, SimulationTime.Zero));
        metrics.Observe(new ProtocolLifecycleObservation("pump-1", "OPC UA", "start", true, null, SimulationTime.Zero));
        metrics.Observe(new ProtocolLifecycleObservation("pump-1", "future-protocol", "explode", false, "secret-error", SimulationTime.Zero));
        metrics.RecordDroppedEvent("runtime-log", "ingress");
        metrics.RecordDroppedEvent("signalr", "subscriber");

        var scrape = await ScrapeAsync(registry);
        string[] names =
        [
            "industrial_simulation_ticks_total",
            "industrial_device_state_changes_total",
            "industrial_scenario_actions_total",
            "industrial_faults_active",
            "industrial_protocol_connections",
            "industrial_protocol_errors_total",
            "industrial_stream_events_dropped_total"
        ];
        Assert.All(names, name => Assert.Contains($"# TYPE {name}", scrape, StringComparison.Ordinal));
        Assert.Contains("mode=\"deterministic\"", scrape, StringComparison.Ordinal);
        Assert.Contains("action=\"set\"", scrape, StringComparison.Ordinal);
        Assert.Contains("protocol=\"opcua\"", scrape, StringComparison.Ordinal);
        Assert.Contains("protocol=\"other\",operation=\"other\"", scrape, StringComparison.Ordinal);
        Assert.DoesNotContain("pump-1", scrape, StringComparison.Ordinal);
        Assert.DoesNotContain("startup", scrape, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-error", scrape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scrape_collects_tick_deltas_without_a_callback_on_the_simulation_tick_path()
    {
        var collectorRegistry = Prometheus.Metrics.NewCustomRegistry();
        var metrics = new IndustrialSimMetrics(collectorRegistry);
        await using var simulations = new SimulationRegistry();
        var handle = await simulations.CreateAsync(new DeviceLaunchDefinition(
            new DeviceDefinition(
                new DeviceId("tick-device"),
                "custom",
                [new DataPointDefinition("value", DataType.Int32, DataPointAccess.ReadWrite, 0)],
                [],
                []),
            new SimulationHostOptions(Deterministic: true)));
        metrics.Attach(simulations);
        await handle.Host.StartAsync();

        handle.Host.Tick(TimeSpan.FromSeconds(1));
        handle.Host.Tick(TimeSpan.FromSeconds(1));

        var first = await ScrapeAsync(collectorRegistry);
        var second = await ScrapeAsync(collectorRegistry);
        Assert.Contains("industrial_simulation_ticks_total{mode=\"deterministic\"} 2", first, StringComparison.Ordinal);
        Assert.Contains("industrial_simulation_ticks_total{mode=\"deterministic\"} 2", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_log_drop_properties_and_prometheus_counters_agree()
    {
        var collectorRegistry = Prometheus.Metrics.NewCustomRegistry();
        var metrics = new IndustrialSimMetrics(collectorRegistry);
        await using var simulations = new SimulationRegistry();
        await using var log = new RuntimeEventLog(
            new RuntimeEventLogOptions(1, 100, 1),
            new RuntimeEventEnvelopeFactory(new SecretRedactor()),
            TimeProvider.System,
            metrics);
        log.Attach(simulations);
        var handle = await simulations.CreateAsync(new DeviceLaunchDefinition(
            new DeviceDefinition(
                new DeviceId("drop-device"),
                "custom",
                [new DataPointDefinition("value", DataType.Int32, DataPointAccess.ReadWrite, 0)],
                [],
                []),
            new SimulationHostOptions(Deterministic: true)));

        for (var value = 1; value <= 20; value++)
            handle.Host.State.SetInternal(new DataPointId("value"), value);
        Assert.True(log.IngressDropped > 0);

        await log.StartAsync();
        await RuntimeEventLogTests.WaitUntilAsync(() => log.Query().Count > 0);
        await using var subscription = log.Subscribe(capacity: 1);
        for (var value = 21; value <= 100; value++)
            handle.Host.State.SetInternal(new DataPointId("value"), value);
        await RuntimeEventLogTests.WaitUntilAsync(() => log.SubscriberDropped > 0);

        var scrape = await ScrapeAsync(collectorRegistry);
        Assert.Contains(
            $"industrial_stream_events_dropped_total{{stream=\"runtime-log\",stage=\"ingress\"}} {log.IngressDropped}",
            scrape,
            StringComparison.Ordinal);
        Assert.Contains(
            $"industrial_stream_events_dropped_total{{stream=\"runtime-log\",stage=\"subscriber\"}} {log.SubscriberDropped}",
            scrape,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OPC UA", "opcua")]
    [InlineData("modbus-tcp", "modbus")]
    [InlineData("mqtt", "other")]
    public void Protocol_labels_are_normalized_to_a_fixed_vocabulary(string input, string expected) =>
        Assert.Equal(expected, MetricLabels.Protocol(input));

    [Theory]
    [InlineData("SET", "set")]
    [InlineData("ramp", "ramp")]
    [InlineData("custom", "unknown")]
    public void Scenario_labels_are_normalized_to_a_fixed_vocabulary(string input, string expected) =>
        Assert.Equal(expected, MetricLabels.ScenarioAction(input));

    private static async Task<string> ScrapeAsync(CollectorRegistry registry)
    {
        await using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
