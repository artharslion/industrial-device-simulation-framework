using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Runtime.Tests;

public class StateStoreTests
{
    private static DeviceDefinition Definition() => new(
        new DeviceId("pump-001"),
        "pump",
        new[]
        {
            new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0),
            new DataPointDefinition("temperature", DataType.Double, DataPointAccess.Read, 25d)
        });

    [Fact]
    public void Initializes_from_definitions_and_reads_current_values()
    {
        var store = new StateStore(Definition());

        Assert.Equal(0, store.Get(new DataPointId("speed"))!.Value);
        Assert.Equal(25d, store.Get(new DataPointId("temperature"))!.Value);
    }

    [Fact]
    public void Write_validates_access_type_and_reference()
    {
        var store = new StateStore(Definition());

        Assert.False(store.Set(new DataPointId("temperature"), 30d).Succeeded);
        Assert.False(store.Set(new DataPointId("speed"), "fast").Succeeded);
        Assert.False(store.Set(new DataPointId("missing"), 1).Succeeded);
        Assert.Contains("read-only", store.Set(new DataPointId("temperature"), 30d).Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Publishes_only_changed_events_in_write_order()
    {
        var store = new StateStore(Definition());
        var changed = new List<DataPointChanged>();
        store.DataPointChanged += changed.Add;

        var first = store.Set(new DataPointId("speed"), 100);
        var same = store.Set(new DataPointId("speed"), 100);
        var second = store.Set(new DataPointId("speed"), 200);

        Assert.True(first.Changed);
        Assert.False(same.Changed);
        Assert.True(second.Changed);
        Assert.Equal(new[] { 100, 200 }, changed.Select(e => (int)e.NewValue.Value).ToArray());
        Assert.Equal(100, changed[1].PreviousValue!.Value);
    }

    [Fact]
    public async Task Concurrent_writes_are_serialized_and_reads_remain_safe()
    {
        var store = new StateStore(Definition());
        var writes = Enumerable.Range(1, 100).Select(value => Task.Run(() => store.Set(new DataPointId("speed"), value)));

        await Task.WhenAll(writes);

        var current = store.Get(new DataPointId("speed"));
        Assert.NotNull(current);
        Assert.InRange((int)current!.Value, 1, 100);
    }

    [Fact]
    public void Batch_write_validates_every_value_before_committing_any_change()
    {
        var store = new StateStore(Definition());

        var result = store.SetBatch(new[]
        {
            (new DataPointId("speed"), (object?)100),
            (new DataPointId("temperature"), (object?)30d)
        });

        Assert.False(result.Succeeded);
        Assert.Equal(0, store.Get(new DataPointId("speed"))!.Value);
        Assert.Equal(25d, store.Get(new DataPointId("temperature"))!.Value);
    }
}
