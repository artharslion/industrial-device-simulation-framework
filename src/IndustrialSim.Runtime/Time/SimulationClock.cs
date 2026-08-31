namespace IndustrialSim.Runtime.Time;

public interface ISimulationClock
{
    TimeSpan Elapsed { get; }
    DateTimeOffset UtcNow { get; }
    bool IsDeterministic { get; }
}

public sealed class DeterministicClock : ISimulationClock
{
    private readonly object _gate = new();
    private TimeSpan _elapsed;

    public DeterministicClock(DateTimeOffset? startTime = null) => StartTime = startTime ?? DateTimeOffset.UtcNow;
    public DateTimeOffset StartTime { get; }
    public TimeSpan Elapsed { get { lock (_gate) return _elapsed; } }
    public DateTimeOffset UtcNow { get { lock (_gate) return StartTime + _elapsed; } }
    public bool IsDeterministic => true;

    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(amount), "Clock cannot move backwards.");
        lock (_gate) _elapsed += amount;
    }

    public void Reset()
    {
        lock (_gate) _elapsed = TimeSpan.Zero;
    }
}

public sealed class RealTimeClock : ISimulationClock
{
    private readonly DateTimeOffset _startUtc = DateTimeOffset.UtcNow;
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public DateTimeOffset UtcNow => _startUtc + _stopwatch.Elapsed;
    public bool IsDeterministic => false;
}
