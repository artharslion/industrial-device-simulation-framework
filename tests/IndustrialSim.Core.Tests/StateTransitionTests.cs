using IndustrialSim.Core.Domain;

namespace IndustrialSim.Core.Tests;

public class StateTransitionTests
{
    [Fact]
    public void Data_point_changed_contains_transition_context()
    {
        var changed = new DataPointChanged(
            SimulationTime.FromSeconds(2),
            new DeviceId("pump-001"),
            new DataPointId("speed"),
            ScalarValue.Create(DataType.Int32, 0),
            ScalarValue.Create(DataType.Int32, 1450),
            new Dictionary<string, string> { ["reason"] = "scenario" });

        Assert.Equal(SimulationTime.FromSeconds(2), changed.Timestamp);
        Assert.Equal("pump-001", changed.DeviceId.Value);
        Assert.Equal("speed", changed.DataPointId.Value);
        Assert.Equal(0, changed.PreviousValue!.Value);
        Assert.Equal(1450, changed.NewValue.Value);
        Assert.Equal("scenario", changed.Metadata["reason"]);
    }

    [Fact]
    public void Transition_result_distinguishes_changed_and_unchanged_values()
    {
        var changed = StateTransitionResult.ChangedResult(new DataPointChanged(
            SimulationTime.Zero,
            new DeviceId("pump"),
            new DataPointId("running"),
            ScalarValue.Create(DataType.Boolean, false),
            ScalarValue.Create(DataType.Boolean, true)));
        var unchanged = StateTransitionResult.Unchanged(ScalarValue.Create(DataType.Boolean, true));

        Assert.True(changed.Succeeded);
        Assert.True(changed.Changed);
        Assert.NotNull(changed.Event);
        Assert.True(unchanged.Succeeded);
        Assert.False(unchanged.Changed);
        Assert.Null(unchanged.Event);
        Assert.Equal(true, unchanged.CurrentValue!.Value);
    }

    [Fact]
    public void Rejected_transition_contains_actionable_error()
    {
        var result = StateTransitionResult.Rejected("Data point 'speed' is read-only.");

        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Contains("read-only", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
