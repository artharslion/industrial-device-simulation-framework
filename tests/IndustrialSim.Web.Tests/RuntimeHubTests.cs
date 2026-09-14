using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Observability.Events;
using IndustrialSim.Observability.Security;
using IndustrialSim.Web.Hubs;

namespace IndustrialSim.Web.Tests;

public sealed class RuntimeHubTests
{
    [Fact]
    public async Task State_events_are_ordered_for_each_subscriber()
    {
        await using var registry = new SimulationRegistry();
        await using var log = EventLog(registry);
        await log.StartAsync();
        await using var broker = new RuntimeStreamBroker(log, capacity: 16);
        var handle = await registry.CreateAsync(Launch("stream"));
        await using var subscription = broker.Subscribe();

        handle.Host.State.SetInternal(new DataPointId("speed"), 1);
        handle.Host.State.SetInternal(new DataPointId("speed"), 2);
        var first = await subscription.Reader.ReadAsync();
        var second = await subscription.Reader.ReadAsync();

        Assert.True(first.Sequence < second.Sequence);
        Assert.Equal(1, first.Value.GetInt32());
        Assert.Equal(2, second.Value.GetInt32());
    }

    [Fact]
    public async Task Slow_consumers_are_bounded_and_count_dropped_events()
    {
        await using var registry = new SimulationRegistry();
        await using var log = EventLog(registry);
        await log.StartAsync();
        await using var broker = new RuntimeStreamBroker(log, capacity: 2);
        var handle = await registry.CreateAsync(Launch("slow"));
        await using var subscription = broker.Subscribe();

        for (var value = 1; value <= 20; value++)
            handle.Host.State.SetInternal(new DataPointId("speed"), value);

        await WaitUntilAsync(() => broker.DroppedEvents > 0);
        Assert.True(subscription.Reader.TryRead(out _));
    }

    private static RuntimeEventLog EventLog(ISimulationRegistry registry)
    {
        var log = new RuntimeEventLog(
            new RuntimeEventLogOptions(128, 128, 128),
            new RuntimeEventEnvelopeFactory(new SecretRedactor()),
            TimeProvider.System);
        log.Attach(registry);
        return log;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private static DeviceLaunchDefinition Launch(string id) => new(
        new DeviceDefinition(new DeviceId(id), "custom",
            [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)], [], []),
        new SimulationHostOptions(true, 1));
}
