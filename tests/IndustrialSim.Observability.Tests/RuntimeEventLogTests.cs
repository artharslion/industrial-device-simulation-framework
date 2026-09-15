using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Observability.Events;
using IndustrialSim.Observability.Security;

namespace IndustrialSim.Observability.Tests;

public sealed class RuntimeEventLogTests
{
    [Fact]
    public async Task Retention_filters_and_live_subscribers_share_one_ordered_sequence()
    {
        await using var registry = new SimulationRegistry();
        await using var log = CreateLog(new RuntimeEventLogOptions(32, 3, 8));
        log.Attach(registry);
        await log.StartAsync();
        var handle = await registry.CreateAsync(Launch("pump-a"));
        await using var subscription = log.Subscribe(new RuntimeEventQuery(DeviceId: "pump-a", EventTypes: ["DataPointChanged"]));

        for (var value = 1; value <= 4; value++)
            handle.Host.State.SetInternal(new DataPointId("speed"), value, handle.Host.Engine.CurrentTime);

        await WaitUntilAsync(() => log.Query(new RuntimeEventQuery(DeviceId: "pump-a")).Count == 3);
        var retained = log.Query(new RuntimeEventQuery(DeviceId: "pump-a", EventTypes: ["DataPointChanged"], Limit: 10));
        var firstLive = await subscription.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        var secondLive = await subscription.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, retained.Count);
        Assert.Equal([2, 3, 4], retained.Select(item => item.Data.GetProperty("newValue").GetInt32()));
        Assert.True(retained[0].Sequence < retained[1].Sequence && retained[1].Sequence < retained[2].Sequence);
        Assert.True(firstLive.Sequence < secondLive.Sequence);
        Assert.Empty(log.Query(new RuntimeEventQuery(DeviceId: "another-device")));
        Assert.Equal(2, log.Query(new RuntimeEventQuery(AfterSequence: retained[0].Sequence)).Count);
    }

    [Fact]
    public async Task Full_ingress_and_subscriber_channels_count_dropped_events_without_waiting()
    {
        await using var registry = new SimulationRegistry();
        await using var log = CreateLog(new RuntimeEventLogOptions(1, 100, 1));
        log.Attach(registry);
        var handle = await registry.CreateAsync(Launch("overload"));

        for (var value = 1; value <= 20; value++)
            handle.Host.State.SetInternal(new DataPointId("speed"), value);
        Assert.True(log.IngressDropped > 0);

        await log.StartAsync();
        await WaitUntilAsync(() => log.Query().Count > 0);
        await using var subscription = log.Subscribe(capacity: 1);
        handle.Host.State.SetInternal(new DataPointId("speed"), 21);
        await WaitUntilAsync(() => subscription.Reader.TryPeek(out _));
        handle.Host.State.SetInternal(new DataPointId("speed"), 22);

        await WaitUntilAsync(() => log.SubscriberDropped > 0);
        Assert.True(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Existing_hosts_and_new_hosts_are_attached_once()
    {
        await using var registry = new SimulationRegistry();
        var existing = await registry.CreateAsync(Launch("existing"));
        await using var log = CreateLog(new RuntimeEventLogOptions(16, 16, 4));

        log.Attach(registry);
        log.Attach(registry);
        await log.StartAsync();
        existing.Host.State.SetInternal(new DataPointId("speed"), 1);
        var added = await registry.CreateAsync(Launch("added"));
        added.Host.State.SetInternal(new DataPointId("speed"), 2);

        await WaitUntilAsync(() => log.Query().Count == 2);
        Assert.Single(log.Query(new RuntimeEventQuery(DeviceId: "existing")));
        Assert.Single(log.Query(new RuntimeEventQuery(DeviceId: "added")));
    }

    [Fact]
    public async Task Retained_events_can_be_searched_before_the_result_limit_is_applied()
    {
        await using var registry = new SimulationRegistry();
        await using var log = CreateLog(new RuntimeEventLogOptions(32, 16, 8));
        log.Attach(registry);
        await log.StartAsync();
        var handle = await registry.CreateAsync(Launch("pump-search"));

        handle.Host.State.SetInternal(new DataPointId("speed"), 1);
        handle.Host.State.SetInternal(new DataPointId("speed"), 2);
        handle.Host.State.SetInternal(new DataPointId("speed"), 3);

        await WaitUntilAsync(() => log.Query().Count == 3);

        var matches = log.Query(new RuntimeEventQuery(Search: "SPEED", Limit: 1));

        var match = Assert.Single(matches);
        Assert.Equal(3, match.Data.GetProperty("newValue").GetInt32());
        Assert.Equal(3, log.Query(new RuntimeEventQuery(Search: "pump-search")).Count);
        Assert.Empty(log.Query(new RuntimeEventQuery(Search: "alarm")));
    }

    private static RuntimeEventLog CreateLog(RuntimeEventLogOptions options) =>
        new(options, new RuntimeEventEnvelopeFactory(new SecretRedactor()), TimeProvider.System);

    private static DeviceLaunchDefinition Launch(string id) => new(
        new DeviceDefinition(
            new DeviceId(id),
            "custom",
            [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)],
            [],
            []),
        new SimulationHostOptions(true, 1));

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
