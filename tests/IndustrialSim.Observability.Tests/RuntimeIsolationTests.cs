using System.Diagnostics;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Observability.Events;
using IndustrialSim.Observability.Security;

namespace IndustrialSim.Observability.Tests;

public sealed class RuntimeIsolationTests
{
    [Fact]
    public async Task Blocked_formatter_and_unread_subscriber_do_not_block_simulation_ticks()
    {
        await using var registry = new SimulationRegistry();
        var blockingFactory = new BlockingEnvelopeFactory(new RuntimeEventEnvelopeFactory(new SecretRedactor()));
        await using var log = new RuntimeEventLog(
            new RuntimeEventLogOptions(512, 32, 1),
            blockingFactory,
            TimeProvider.System);
        log.Attach(registry);
        await log.StartAsync();
        await using var subscription = log.Subscribe(capacity: 1);
        var handle = await registry.CreateAsync(SensorLaunch());
        await handle.Host.StartAsync();
        await blockingFactory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var ticks = Task.Run(() =>
        {
            for (var index = 0; index < 100; index++)
                handle.Host.Tick(TimeSpan.FromMilliseconds(10));
        });

        await ticks.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(handle.Host.TotalTicks >= 100);
        Assert.Equal(1d, Convert.ToDouble(handle.Host.Runtime.Read("value")!.Value), 6);
        blockingFactory.Release.Set();
    }

    [Fact]
    public async Task Host_compatibility_event_history_is_bounded()
    {
        await using var host = SimulationHost.Create(new DeviceDefinition(
            new DeviceId("bounded-host"),
            "custom",
            [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)]));

        for (var value = 1; value <= SimulationHost.EventRetentionCapacity + 50; value++)
            host.State.SetInternal(new DataPointId("speed"), value);

        Assert.Equal(SimulationHost.EventRetentionCapacity, host.Events.Count);
        var first = Assert.IsType<DataPointChanged>(host.Events.First());
        Assert.Equal(51, first.NewValue.Value);
    }

    private static DeviceLaunchDefinition SensorLaunch() => new(
        new DeviceDefinition(
            new DeviceId("sensor-observed"),
            "sensor",
            [
                new DataPointDefinition("value", DataType.Double, DataPointAccess.ReadWrite, 0d),
                new DataPointDefinition("quality", DataType.String, DataPointAccess.Read, "Good")
            ],
            [new CommandDefinition("reset")],
            []),
        new SimulationHostOptions(true, 1));

    private sealed class BlockingEnvelopeFactory(IRuntimeEventEnvelopeFactory inner) : IRuntimeEventEnvelopeFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();

        public RuntimeEventEnvelope Create(object observation, long sequence, DateTimeOffset observedAtUtc, ActivityContext? activityContext = null)
        {
            Entered.TrySetResult();
            Release.Wait(TimeSpan.FromSeconds(5));
            return inner.Create(observation, sequence, observedAtUtc, activityContext);
        }
    }
}
