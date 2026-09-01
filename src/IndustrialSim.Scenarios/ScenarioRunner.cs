using System.Globalization;
using System.Text.RegularExpressions;
using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.Engine;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Scenarios;

public sealed class ScenarioRunner
{
    private readonly ScenarioDefinition _scenario;
    private readonly SimulationEngine _engine;
    private readonly StateStore _state;
    private readonly Action<string, string>? _command;
    private readonly Action<string, string>? _fault;
    private bool _started;
    private bool _stopped;
    public event Action<ScenarioAction, SimulationTime>? ActionExecuted;
    public bool IsRunning => _started && !_stopped;

    public ScenarioRunner(ScenarioDefinition scenario, SimulationEngine engine, StateStore state, Action<string, string>? command = null, Action<string, string>? fault = null)
    { _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario)); _engine = engine ?? throw new ArgumentNullException(nameof(engine)); _state = state ?? throw new ArgumentNullException(nameof(state)); _command = command; _fault = fault; }

    public void Start()
    {
        if (_started) return;
        _started = true;
        foreach (var step in _scenario.Steps)
        {
            switch (step.Trigger)
            {
                case AtTrigger at: ScheduleAction(step.Action, new SimulationTime(at.Offset)); break;
                case AfterTrigger after: ScheduleAction(step.Action, new SimulationTime(after.Delay)); break;
                case EveryTrigger every:
                    if (every.Interval <= TimeSpan.Zero) throw new ArgumentException("every interval must be positive.");
                    ScheduleEvery(step.Action, every.Interval, new SimulationTime(every.Interval)); break;
                case WhenTrigger condition: ScheduleWhen(step.Action, condition); break;
            }
        }
    }

    public void Stop() => _stopped = true;

    private void ScheduleEvery(ScenarioAction action, TimeSpan interval, SimulationTime due) => _engine.Schedule(due, () => { if (_stopped) return; Execute(action); ScheduleEvery(action, interval, due + interval); });
    private void ScheduleWhen(ScenarioAction action, WhenTrigger trigger) => _engine.Schedule(new SimulationTime(TimeSpan.Zero), () =>
    {
        if (_stopped) return;
        if (ConditionEvaluator.Evaluate(trigger.Condition, _state)) Execute(action);
        else _engine.Schedule(new SimulationTime(_engine.CurrentTime.Elapsed + TimeSpan.FromMilliseconds(100)), () => ScheduleWhen(action, trigger));
    });
    private void ScheduleAction(ScenarioAction action, SimulationTime due) => _engine.Schedule(due, () => Execute(action));

    private void Execute(ScenarioAction action)
    {
        if (_stopped) return;
        switch (action)
        {
            case SetAction set: _state.SetInternal(new DataPointId(set.DataPoint), set.Value, _engine.CurrentTime); break;
            case CommandAction command: _command?.Invoke(command.Device, command.Name); break;
            case FaultAction fault: _fault?.Invoke(fault.Device, fault.Type); break;
            case RampAction ramp: ExecuteRamp(ramp); break;
            case WaitAction _: break;
        }
        ActionExecuted?.Invoke(action, _engine.CurrentTime);
    }
    private void ExecuteRamp(RampAction ramp)
    {
        var duration = ramp.Duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : ramp.Duration;
        var steps = Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / 100d));
        for (var i = 0; i <= steps; i++)
        {
            var index = i; _engine.Schedule(new SimulationTime(_engine.CurrentTime.Elapsed + duration * index / steps), () => { if (!_stopped) _state.SetInternal(new DataPointId(ramp.DataPoint), ramp.From + (ramp.To - ramp.From) * index / steps, _engine.CurrentTime); });
        }
    }
}

public static class ConditionEvaluator
{
    private static readonly Regex Expression = new("^\\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<op>==|>|<)\\s*(?<value>true|false|-?[0-9]+(?:\\.[0-9]+)?)\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static bool Evaluate(string expression, StateStore state)
    {
        var match = Expression.Match(expression ?? string.Empty);
        if (!match.Success) throw new ArgumentException("Condition must be '<datapoint> >|<|== <scalar>'.", nameof(expression));
        var current = state.Get(new DataPointId(match.Groups["name"].Value))?.Value;
        if (current is null) return false;
        var literal = match.Groups["value"].Value;
        int comparison;
        if (current is bool boolean && bool.TryParse(literal, out var expected)) comparison = boolean.CompareTo(expected);
        else if (double.TryParse(Convert.ToString(current, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var target)) comparison = number.CompareTo(target);
        else return false;
        return match.Groups["op"].Value switch { "==" => comparison == 0, ">" => comparison > 0, "<" => comparison < 0, _ => false };
    }
}
