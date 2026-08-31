using IndustrialSim.Runtime.Time;

namespace IndustrialSim.Runtime.Tests;

public class SimulationClockTests
{
    [Fact]
    public void Deterministic_clock_advances_only_when_ticked()
    {
        var clock = new DeterministicClock(DateTimeOffset.UtcNow);
        var initial = clock.Elapsed;

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(initial + TimeSpan.FromSeconds(2), clock.Elapsed);
        Assert.Equal(clock.StartTime + clock.Elapsed, clock.UtcNow);
        Assert.True(clock.IsDeterministic);
    }

    [Fact]
    public void Deterministic_clock_rejects_negative_advance()
    {
        var clock = new DeterministicClock();

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task Real_time_clock_is_monotonic()
    {
        var clock = new RealTimeClock();
        var before = clock.Elapsed;
        await Task.Delay(15);
        Assert.True(clock.Elapsed >= before);
        Assert.False(clock.IsDeterministic);
    }
}
