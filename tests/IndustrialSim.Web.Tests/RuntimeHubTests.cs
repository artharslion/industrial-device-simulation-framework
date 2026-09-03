using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Web.Hubs;

namespace IndustrialSim.Web.Tests;

public sealed class RuntimeHubTests
{
    [Fact]
    public async Task State_events_are_ordered_for_each_subscriber()
    {
        await using var registry = new SimulationRegistry();
        var broker = new RuntimeStreamBroker(capacity: 16);
        broker.Attach(registry);
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
        var broker = new RuntimeStreamBroker(capacity: 2);
        broker.Attach(registry);
        var handle = await registry.CreateAsync(Launch("slow"));
        await using var subscription = broker.Subscribe();

        for (var value = 1; value <= 20; value++)
            handle.Host.State.SetInternal(new DataPointId("speed"), value);

        Assert.True(broker.DroppedEvents > 0);
        Assert.True(subscription.Reader.TryRead(out _));
    }

    private static DeviceLaunchDefinition Launch(string id) => new(
        new DeviceDefinition(new DeviceId(id), "custom",
            [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)], [], []),
        new SimulationHostOptions(true, 1));
}
