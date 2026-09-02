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
    private readonly Action<FaultAction>? _faultAction;
    private bool _started;
    private bool _stopped;
    public event Action<ScenarioAction, SimulationTime>? ActionExecuted;
    public bool IsRunning => _started && !_stopped;

    public ScenarioRunner(ScenarioDefinition scenario, SimulationEngine engine, StateStore state, Action<string, string>? command = null, Action<string, string>? fault = null, Action<FaultAction>? faultAction = null)
    { _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario)); _engine = engine ?? throw new ArgumentNullException(nameof(engine)); _state = state ?? throw new ArgumentNullException(nameof(state)); _command = command; _fault = fault; _faultAction = faultAction; }

    public void Start()
    {
        if (_started) return;
        ValidateScenario();
        _started = true;
        var relativeCursor = TimeSpan.Zero;
        foreach (var step in _scenario.Steps)
        {
            switch (step.Trigger)
            {
                case AtTrigger at:
                    relativeCursor = at.Offset;
                    ScheduleSequentialAction(step.Action, relativeCursor, ref relativeCursor);
                    break;
                case AfterTrigger after:
                    relativeCursor += after.Delay;
                    ScheduleSequentialAction(step.Action, relativeCursor, ref relativeCursor);
                    break;
                case EveryTrigger every:
                    ScheduleEvery(step.Action, every.Interval, new SimulationTime(every.Interval)); break;
                case WhenTrigger condition: ScheduleWhen(step.Action, condition); break;
            }
        }
    }

    public void Stop() => _stopped = true;

    private void ScheduleSequentialAction(ScenarioAction action, TimeSpan due, ref TimeSpan relativeCursor)
    {
        ScheduleAction(action, new SimulationTime(due));
        if (action is WaitAction wait) relativeCursor += wait.Duration;
    }

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
            case SetAction set:
                EnsureSucceeded(_state.SetInternal(new DataPointId(set.DataPoint), set.Value, _engine.CurrentTime), set.DataPoint);
                break;
            case CommandAction command: _command?.Invoke(command.Device, command.Name); break;
            case FaultAction fault: _faultAction?.Invoke(fault); _fault?.Invoke(fault.Device, fault.Type); break;
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
            var index = i; _engine.Schedule(new SimulationTime(_engine.CurrentTime.Elapsed + duration * index / steps), () =>
            {
                if (!_stopped) EnsureSucceeded(_state.SetInternal(new DataPointId(ramp.DataPoint), ramp.From + (ramp.To - ramp.From) * index / steps, _engine.CurrentTime), ramp.DataPoint);
            });
        }
    }

    private void ValidateScenario()
    {
        foreach (var step in _scenario.Steps)
        {
            switch (step.Trigger)
            {
                case AtTrigger at when at.Offset < TimeSpan.Zero:
                    throw new ArgumentException("at offset cannot be negative.");
                case AfterTrigger after when after.Delay < TimeSpan.Zero:
                    throw new ArgumentException("after delay cannot be negative.");
                case EveryTrigger every when every.Interval <= TimeSpan.Zero:
                    throw new ArgumentException("every interval must be positive.");
                case WhenTrigger conditionTrigger:
                    ValidateDevice(conditionTrigger.Device);
                    ValidateDataPoint(ConditionEvaluator.DataPointName(conditionTrigger.Condition));
                    break;
            }

            switch (step.Action)
            {
                case SetAction set:
                    ValidateDevice(set.Device);
                    var setPoint = ValidateDataPoint(set.DataPoint);
                    if (!ScalarValue.TryCreate(setPoint.DataType, set.Value, out _)) throw new ArgumentException($"Scenario value is invalid for data point '{set.DataPoint}'.");
                    break;
                case RampAction ramp:
                    ValidateDevice(ramp.Device);
                    var rampPoint = ValidateDataPoint(ramp.DataPoint);
                    if (ramp.Duration <= TimeSpan.Zero) throw new ArgumentException("ramp duration must be positive.");
                    if (!ScalarValue.TryCreate(rampPoint.DataType, ramp.From, out _) || !ScalarValue.TryCreate(rampPoint.DataType, ramp.To, out _)) throw new ArgumentException($"Scenario ramp is invalid for data point '{ramp.DataPoint}'.");
                    break;
                case CommandAction command:
                    ValidateDevice(command.Device);
                    if (!_state.Definition.Commands.Any(item => item.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"Scenario command '{command.Name}' does not exist on device '{command.Device}'.");
                    break;
                case WaitAction wait when wait.Duration < TimeSpan.Zero:
                    throw new ArgumentException("wait duration cannot be negative.");
                case WaitAction when step.Trigger is EveryTrigger or WhenTrigger:
                    throw new ArgumentException("wait actions require an at or after trigger.");
                case FaultAction fault:
                    if (!string.IsNullOrWhiteSpace(fault.Device)) ValidateDevice(fault.Device);
                    if (!string.IsNullOrWhiteSpace(fault.DataPoint)) ValidateDataPoint(fault.DataPoint);
                    if (fault.Duration < TimeSpan.Zero) throw new ArgumentException("fault duration cannot be negative.");
                    break;
            }
        }
    }

    private void ValidateDevice(string device)
    {
        if (!device.Equals(_state.Definition.Id.Value, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Scenario device '{device}' does not match runtime device '{_state.Definition.Id.Value}'.");
    }

    private DataPointDefinition ValidateDataPoint(string dataPoint) =>
        _state.Definition.DataPoints.FirstOrDefault(item => item.Name.Equals(dataPoint, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Scenario data point '{dataPoint}' does not exist on device '{_state.Definition.Id.Value}'.");

    private static void EnsureSucceeded(StateTransitionResult result, string dataPoint)
    {
        if (!result.Succeeded) throw new InvalidOperationException(result.Error ?? $"Scenario failed to update data point '{dataPoint}'.");
    }
}

public static class ConditionEvaluator
{
    private static readonly Regex Expression = new("^\\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<op>==|>|<)\\s*(?<value>true|false|-?[0-9]+(?:\\.[0-9]+)?)\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static bool Evaluate(string expression, StateStore state)
    {
        var match = Match(expression);
        var current = state.GetExposedInternal(new DataPointId(match.Groups["name"].Value))?.Value;
        if (current is null) return false;
        var literal = match.Groups["value"].Value;
        int comparison;
        if (current is bool boolean && bool.TryParse(literal, out var expected)) comparison = boolean.CompareTo(expected);
        else if (double.TryParse(Convert.ToString(current, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var target)) comparison = number.CompareTo(target);
        else return false;
        return match.Groups["op"].Value switch { "==" => comparison == 0, ">" => comparison > 0, "<" => comparison < 0, _ => false };
    }

    public static string DataPointName(string expression) => Match(expression).Groups["name"].Value;

    private static Match Match(string expression)
    {
        var match = Expression.Match(expression ?? string.Empty);
        return match.Success ? match : throw new ArgumentException("Condition must be '<datapoint> >|<|== <scalar>'.", nameof(expression));
    }
}
