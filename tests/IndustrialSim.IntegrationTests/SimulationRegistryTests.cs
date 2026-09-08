using IndustrialSim.Core.Domain;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;

namespace IndustrialSim.IntegrationTests;

public sealed class SimulationRegistryTests
{
    [Fact]
    public async Task Creates_lists_starts_stops_and_removes_devices_in_batches()
    {
        await using var registry = new SimulationRegistry();
        await registry.CreateAsync(Launch("pump-a", 15020));
        await registry.CreateAsync(Launch("pump-b", 15021));

        Assert.Equal(["pump-a", "pump-b"], registry.List().Select(item => item.DeviceId).Order().ToArray());
        var started = await registry.StartManyAsync(["pump-a", "pump-b"]);
        Assert.True(started.Succeeded);
        Assert.All(registry.List(), item => Assert.True(item.IsRunning));

        var stopped = await registry.StopManyAsync(["pump-a", "pump-b"]);
        Assert.True(stopped.Succeeded);
        Assert.All(registry.List(), item => Assert.False(item.IsRunning));

        var removed = await registry.RemoveManyAsync(["pump-a", "pump-b"]);
        Assert.True(removed.Succeeded);
        Assert.Empty(registry.List());
    }

    [Fact]
    public async Task Hosts_keep_state_clock_and_faults_isolated()
    {
        await using var registry = new SimulationRegistry();
        var left = await registry.CreateAsync(Launch("left", 15030, seed: 7));
        var right = await registry.CreateAsync(Launch("right", 15031, seed: 11));
        await registry.StartAsync("left");
        await registry.StartAsync("right");

        left.Host.State.SetInternal(new DataPointId("speed"), 900);
        left.Host.Tick(TimeSpan.FromSeconds(3));
        left.Host.ActivateFault(new FaultSpec("left-fault", FaultCategory.Device, "left", null, TimeSpan.Zero, Type: "Overheat"));

        Assert.Equal(900, left.Host.State.GetInternal(new DataPointId("speed"))!.Value);
        Assert.Equal(0, right.Host.State.GetInternal(new DataPointId("speed"))!.Value);
        Assert.Equal(TimeSpan.FromSeconds(3), left.Host.Engine.CurrentTime.Elapsed);
        Assert.Equal(TimeSpan.Zero, right.Host.Engine.CurrentTime.Elapsed);
        Assert.Single(left.Host.FaultManager.ActiveFaults);
        Assert.Empty(right.Host.FaultManager.ActiveFaults);
        Assert.Equal(7, left.Host.Seed);
        Assert.Equal(11, right.Host.Seed);
    }

    [Fact]
    public async Task Rejects_duplicate_ids_and_port_conflicts()
    {
        await using var registry = new SimulationRegistry();
        await registry.CreateAsync(Launch("one", 15040));

        await Assert.ThrowsAsync<SimulationConflictException>(() => registry.CreateAsync(Launch("one", 15041)));
        var conflict = await Assert.ThrowsAsync<SimulationConflictException>(() => registry.CreateAsync(Launch("two", 15040)));
        Assert.Equal("portConflict", conflict.ErrorCode);
    }

    [Fact]
    public async Task Serializes_concurrent_lifecycle_operations()
    {
        await using var registry = new SimulationRegistry();
        var handle = await registry.CreateAsync(Launch("concurrent", 15050));

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => registry.StartAsync("concurrent")));
        Assert.True(handle.Host.IsRunning);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => registry.StopAsync("concurrent")));
        Assert.False(handle.Host.IsRunning);
    }

    [Fact]
    public async Task Replaces_a_stopped_host_only_after_control_plane_commit()
    {
        await using var registry = new SimulationRegistry();
        var original = await registry.CreateAsync(Launch("replaceable", 15060));
        var committed = false;
        var replacement = await registry.ReplaceAsync(
            "replaceable",
            new DeviceLaunchDefinition(Definition("replaceable", includeTemperature: true), new SimulationHostOptions(true, 22), [new ProtocolPortBinding("modbus", 15061)]),
            _ => { committed = true; return Task.CompletedTask; });

        Assert.True(committed);
        Assert.NotSame(original.Host, replacement.Host);
        Assert.Equal(22, replacement.Host.Seed);
        Assert.Contains(replacement.Host.Runtime.Definition.DataPoints, point => point.Name == "temperature");
        Assert.Equal(15061, replacement.PortBindings.Single().Port);
    }

    [Fact]
    public async Task Replacement_rejects_running_hosts_and_rolls_back_failed_commits()
    {
        await using var registry = new SimulationRegistry();
        var original = await registry.CreateAsync(Launch("stable", 15070));
        await registry.StartAsync("stable");
        var running = await Assert.ThrowsAsync<SimulationConflictException>(() => registry.ReplaceAsync("stable", Launch("stable", 15071), _ => Task.CompletedTask));
        Assert.Equal("deviceMustBeStopped", running.ErrorCode);
        await registry.StopAsync("stable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ReplaceAsync(
            "stable", Launch("stable", 15071), _ => throw new InvalidOperationException("database unavailable")));

        Assert.Same(original.Host, registry.Get("stable").Host);
        var other = await registry.CreateAsync(Launch("other", 15071));
        Assert.Equal("other", other.DeviceId);
    }

    private static DeviceLaunchDefinition Launch(string id, int port, int seed = 1) => new(
        Definition(id),
        new SimulationHostOptions(Deterministic: true, Seed: seed),
        [new ProtocolPortBinding("modbus", port)]);

    private static DeviceDefinition Definition(string id, bool includeTemperature = false) => new(
        new DeviceId(id),
        "pump",
        Points(includeTemperature),
        [new CommandDefinition("start"), new CommandDefinition("stop")],
        []);

    private static IReadOnlyList<DataPointDefinition> Points(bool includeTemperature) => includeTemperature
        ? [
            new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0),
            new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, false),
            new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false),
            new DataPointDefinition("temperature", DataType.Double, DataPointAccess.Read, 20d)
        ]
        : [
            new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0),
            new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, false),
            new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false)
        ];
}
