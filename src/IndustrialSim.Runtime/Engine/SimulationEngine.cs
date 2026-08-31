using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.Time;

namespace IndustrialSim.Runtime.Engine;

public enum EngineState { Stopped, Running, Paused }

public sealed class SimulationEngine
{
    private readonly ISimulationClock _clock;
    private readonly List<ScheduledCallback> _callbacks = [];
    private long _sequence;

    public SimulationEngine(ISimulationClock clock) => _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    public EngineState State { get; private set; } = EngineState.Stopped;
    public SimulationTime CurrentTime => new(_clock.Elapsed);
    public event EventHandler<Exception>? CallbackFailed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State = EngineState.Running;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State = EngineState.Stopped;
        return Task.CompletedTask;
    }

    public void Pause() => State = State == EngineState.Running ? EngineState.Paused : State;
    public void Reset()
    {
        State = EngineState.Stopped;
        if (_clock is DeterministicClock deterministic) deterministic.Reset();
        _callbacks.Clear();
    }

    public void Schedule(SimulationTime dueTime, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callbacks.Add(new ScheduledCallback(dueTime, _sequence++, callback));
        _callbacks.Sort((left, right) =>
        {
            var time = left.DueTime.CompareTo(right.DueTime);
            return time != 0 ? time : left.Sequence.CompareTo(right.Sequence);
        });
    }

    public void Tick(TimeSpan amount)
    {
        if (State != EngineState.Running) return;
        if (_clock is not DeterministicClock deterministic)
            throw new InvalidOperationException("Explicit ticks require a deterministic clock.");
        deterministic.Advance(amount);
        while (_callbacks.Count > 0 && _callbacks[0].DueTime <= CurrentTime)
        {
            var callback = _callbacks[0].Callback;
            _callbacks.RemoveAt(0);
            try { callback(); }
            catch (Exception exception) { CallbackFailed?.Invoke(this, exception); }
        }
    }

    private sealed record ScheduledCallback(SimulationTime DueTime, long Sequence, Action Callback);
}
