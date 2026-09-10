using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace IndustrialSim.Scenarios;

public sealed record ScenarioTarget(string Type);
public sealed record ScenarioDefinition(string Name, IReadOnlyList<ScenarioStep> Steps, ScenarioTarget? Target = null);
public sealed record ScenarioStep(ScenarioTrigger Trigger, ScenarioAction Action);
public abstract record ScenarioTrigger;
public sealed record AtTrigger(TimeSpan Offset) : ScenarioTrigger;
public sealed record AfterTrigger(TimeSpan Delay) : ScenarioTrigger;
public sealed record EveryTrigger(TimeSpan Interval) : ScenarioTrigger;
public sealed record WhenTrigger(string Device, string Condition) : ScenarioTrigger;
public abstract record ScenarioAction;
public sealed record SetAction(string Device, string DataPoint, object? Value) : ScenarioAction;
public sealed record RampAction(string Device, string DataPoint, double From, double To, TimeSpan Duration) : ScenarioAction;
public sealed record CommandAction(string Device, string Name) : ScenarioAction;
public sealed record WaitAction(TimeSpan Duration) : ScenarioAction;
public sealed record FaultAction(
    string Device,
    string Type,
    string? DataPoint = null,
    string? Protocol = null,
    TimeSpan? Duration = null,
    IReadOnlyDictionary<string, string>? Metadata = null) : ScenarioAction;

public sealed class ScenarioParser
{
    public ScenarioDefinition Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) throw new ArgumentException("Scenario YAML cannot be empty.", nameof(yaml));
        var stream = new YamlStream();
        try { stream.Load(new StringReader(yaml)); } catch (YamlDotNet.Core.YamlException ex) { throw new ArgumentException($"Invalid scenario YAML: {ex.Message}", nameof(yaml), ex); }
        var root = Map(stream.Documents[0].RootNode, "root");
        var scenario = Map(root, "scenario");
        var name = Required(scenario, "name");
        var target = scenario.Children.TryGetValue(Key("target"), out var targetNode)
            ? new ScenarioTarget(Required(Map(targetNode, "target"), "type"))
            : null;
        if (!scenario.Children.TryGetValue(Key("steps"), out var node) || node is not YamlSequenceNode steps) throw new ArgumentException("Missing required 'scenario.steps'.");
        return new ScenarioDefinition(name, steps.Children.Select(step => ParseStep(step, target is not null)).ToArray(), target);
    }
    private static ScenarioStep ParseStep(YamlNode node, bool hasTarget)
    {
        var map = Map(node, "step");
        var triggers = new[] { "at", "after", "every", "when" }.Where(k => map.Children.ContainsKey(Key(k))).ToArray();
        var actions = new[] { "set", "ramp", "command", "wait", "fault" }.Where(k => map.Children.ContainsKey(Key(k))).ToArray();
        if (triggers.Length != 1) throw new ArgumentException("Each scenario step must have exactly one trigger.");
        if (actions.Length != 1) throw new ArgumentException("Each scenario step must have exactly one action.");
        ScenarioTrigger trigger = triggers[0] switch { "at" => new AtTrigger(ParseDuration(Required(map, "at"))), "after" => new AfterTrigger(ParseDuration(Required(map, "after"))), "every" => new EveryTrigger(ParseDuration(Required(map, "every"))), _ => ParseWhen(map[Key("when")], hasTarget) };
        ScenarioAction action = actions[0] switch { "set" => ParseSet(map[Key("set")], hasTarget), "ramp" => ParseRamp(map[Key("ramp")], hasTarget), "command" => ParseCommand(map[Key("command")], hasTarget), "wait" => ParseWait(map[Key("wait")]), _ => ParseFault(map[Key("fault")], hasTarget) };
        return new ScenarioStep(trigger, action);
    }
    private static WhenTrigger ParseWhen(YamlNode n, bool hasTarget) { var m = Map(n, "when"); return new(DeviceOrTarget(m, hasTarget, "when"), Required(m, "condition")); }
    private static SetAction ParseSet(YamlNode n, bool hasTarget) { var m = Map(n, "set"); return new(DeviceOrTarget(m, hasTarget, "set"), Required(m, "datapoint"), m.Children.TryGetValue(Key("value"), out var v) ? ConvertNode(v) : throw new ArgumentException("set requires value.")); }
    private static RampAction ParseRamp(YamlNode n, bool hasTarget) { var m = Map(n, "ramp"); return new(DeviceOrTarget(m, hasTarget, "ramp"), Required(m, "datapoint"), Number(m, "from"), Number(m, "to"), ParseDuration(Required(m, "duration"))); }
    private static CommandAction ParseCommand(YamlNode n, bool hasTarget) { var m = Map(n, "command"); return new(DeviceOrTarget(m, hasTarget, "command"), Required(m, "name")); }
    private static WaitAction ParseWait(YamlNode n) { var m = Map(n, "wait"); return new(ParseDuration(Required(m, "duration"))); }
    private static FaultAction ParseFault(YamlNode n, bool hasTarget)
    {
        var mapping = Map(n, "fault");
        var type = Required(mapping, "type");
        var device = Optional(mapping, "device");
        var dataPoint = Optional(mapping, "datapoint");
        if (mapping.Children.TryGetValue(Key("target"), out var targetNode))
        {
            var target = Map(targetNode, "target");
            device = Optional(target, "device") ?? device;
            dataPoint = Optional(target, "datapoint") ?? dataPoint;
        }
        var protocol = Optional(mapping, "protocol");
        if (string.IsNullOrWhiteSpace(device) && string.IsNullOrWhiteSpace(protocol) && !hasTarget) throw new ArgumentException("Fault action requires a scenario target, device/target, or protocol.");
        TimeSpan? duration = Optional(mapping, "duration") is { } durationText ? ParseDuration(durationText) : null;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "parameter", "seed" }) if (Optional(mapping, key) is { } value) metadata[key] = value;
        return new FaultAction(device ?? string.Empty, type, dataPoint, protocol, duration, metadata.Count == 0 ? null : metadata);
    }
    private static YamlMappingNode Map(YamlNode n, string context) => n as YamlMappingNode ?? throw new ArgumentException($"'{context}' must be a mapping.");
    private static YamlMappingNode Map(YamlMappingNode root, string key) => root.Children.TryGetValue(Key(key), out var n) ? Map(n, key) : throw new ArgumentException($"Missing required '{key}'.");
    private static YamlScalarNode Key(string key) => new(key);
    private static string Required(YamlMappingNode m, string key) => m.Children.TryGetValue(Key(key), out var n) && n is YamlScalarNode s && !string.IsNullOrWhiteSpace(s.Value) ? s.Value.Trim() : throw new ArgumentException($"Missing required '{key}'.");
    private static string? Optional(YamlMappingNode mapping, string key) => mapping.Children.TryGetValue(Key(key), out var node) && node is YamlScalarNode scalar && !string.IsNullOrWhiteSpace(scalar.Value) ? scalar.Value.Trim() : null;
    private static string DeviceOrTarget(YamlMappingNode mapping, bool hasTarget, string action) =>
        Optional(mapping, "device") ?? (hasTarget ? string.Empty : throw new ArgumentException($"{action} requires a device or scenario.target."));
    private static double Number(YamlMappingNode m, string key) => double.TryParse(Required(m, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : throw new ArgumentException($"'{key}' must be numeric.");
    public static TimeSpan ParseDuration(string text)
    {
        text = text.Trim();
        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var timeSpan)) return timeSpan;
        if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
            return TimeSpan.FromMilliseconds(milliseconds);
        if (text.Length < 2 || !double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) throw new ArgumentException($"Invalid duration '{text}'.");
        return char.ToLowerInvariant(text[^1]) switch
        {
            's' => TimeSpan.FromSeconds(value),
            'm' => TimeSpan.FromMinutes(value),
            'h' => TimeSpan.FromHours(value),
            _ => throw new ArgumentException($"Invalid duration '{text}'.")
        };
    }
    private static object? ConvertNode(YamlNode n) => n is YamlScalarNode s ? (s.Value is null ? null : bool.TryParse(s.Value, out var b) ? b : long.TryParse(s.Value, out var l) ? l : double.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : s.Value) : throw new ArgumentException("Scalar value required.");
}
