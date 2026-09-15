using IndustrialSim.Core.Domain;

namespace IndustrialSim.Devices;

public sealed record BehaviorParameterDefinition(
    string Name,
    double DefaultValue,
    double Minimum,
    string? Unit,
    string Description);

public sealed record BuiltInDeviceProfile(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<DataPointDefinition> DataPoints,
    IReadOnlyList<CommandDefinition> Commands,
    IReadOnlyList<EventDefinition> Events,
    IReadOnlyList<BehaviorParameterDefinition> Parameters)
{
    public DeviceDefinition CreateDefinition(DeviceId id, IReadOnlyDictionary<string, double>? parameters = null) =>
        new(id, Name, DataPoints, Commands, Events, new DeviceBehaviorDefinition(Name, parameters));
}

public static class BuiltInDeviceProfiles
{
    private static readonly IReadOnlyList<BuiltInDeviceProfile> Profiles =
    [
        new(
            "pump",
            "Pump",
            "Accelerates to a rated speed, heats to a stable normal operating temperature, cools while stopped, derives pressure from speed, and raises an overheat alarm.",
            [
                new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0, "rpm"),
                new DataPointDefinition("temperature", DataType.Double, DataPointAccess.Read, 25d, "°C"),
                new DataPointDefinition("pressure", DataType.Double, DataPointAccess.Read, 0d, "bar"),
                new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, false),
                new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false)
            ],
            [new CommandDefinition("start"), new CommandDefinition("stop")],
            [new EventDefinition("PumpStarted"), new EventDefinition("PumpStopped"), new EventDefinition("Overheated")],
            [
                new("ratedSpeed", 1450, 0, "rpm", "Target running speed."),
                new("accelerationSeconds", 10, 0.001, "s", "Time required to reach rated speed."),
                new("maxPressure", 3.2, 0, "bar", "Pressure produced at rated speed."),
                new("heatingRatePerSecond", 0.5, 0, "°C/s", "Temperature increase while running."),
                new("coolingRatePerSecond", 0.2, 0, "°C/s", "Temperature decrease while stopped."),
                new("normalOperatingTemperature", 70, 25, "°C", "Stable temperature during normal operation."),
                new("overheatTemperature", 90, 0, "°C", "Alarm threshold.")
            ]),
        new(
            "motor",
            "Motor",
            "Accelerates to a rated speed, derives current from load speed, heats while running, cools while stopped, and raises an overheat alarm.",
            [
                new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0, "rpm"),
                new DataPointDefinition("temperature", DataType.Double, DataPointAccess.Read, 25d, "°C"),
                new DataPointDefinition("current", DataType.Double, DataPointAccess.Read, 0d, "A"),
                new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, false),
                new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false)
            ],
            [new CommandDefinition("start"), new CommandDefinition("stop")],
            [],
            [
                new("ratedSpeed", 1800, 0, "rpm", "Target running speed."),
                new("accelerationSeconds", 10, 0.001, "s", "Time required to reach rated speed."),
                new("ratedCurrent", 12, 0, "A", "Current at rated speed."),
                new("heatingRatePerSecond", 0.4, 0, "°C/s", "Temperature increase while running."),
                new("coolingRatePerSecond", 0.2, 0, "°C/s", "Temperature decrease while stopped."),
                new("overheatTemperature", 90, 0, "°C", "Alarm threshold.")
            ]),
        new(
            "sensor",
            "Sensor",
            "Increases its value deterministically while quality is Good; reset restores value and quality.",
            [
                new DataPointDefinition("value", DataType.Double, DataPointAccess.ReadWrite, 0d),
                new DataPointDefinition("quality", DataType.String, DataPointAccess.Read, "Good")
            ],
            [new CommandDefinition("reset")],
            [],
            [new("ratePerSecond", 1, 0, null, "Value increase per simulated second.")])
    ];

    public static IReadOnlyList<BuiltInDeviceProfile> All => Profiles;

    public static BuiltInDeviceProfile? Find(string? profile) =>
        Profiles.FirstOrDefault(item => item.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));

    public static BuiltInDeviceProfile Get(string profile) =>
        Find(profile) ?? throw new ArgumentException($"Unknown built-in behavior profile '{profile}'.", nameof(profile));

    public static void Validate(DeviceDefinition definition, DeviceBehaviorDefinition behavior)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(behavior);
        if (behavior.Profile.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (behavior.Parameters.Count > 0) throw new ArgumentException("Behavior profile 'none' does not accept parameters.");
            return;
        }

        var profile = Get(behavior.Profile);
        if (!definition.Type.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Behavior profile '{profile.Name}' requires device type '{profile.Name}', but the definition uses '{definition.Type}'.");

        var points = definition.DataPoints.ToDictionary(point => point.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var required in profile.DataPoints)
        {
            if (!points.TryGetValue(required.Name, out var actual))
                throw new ArgumentException($"Behavior profile '{profile.Name}' requires data point '{required.Name}'.");
            if (actual.DataType != required.DataType || actual.Access != required.Access)
                throw new ArgumentException($"Behavior profile '{profile.Name}' requires data point '{required.Name}' to be {required.DataType}/{required.Access}.");
        }

        var commands = definition.Commands.Select(command => command.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var required in profile.Commands)
            if (!commands.Contains(required.Name))
                throw new ArgumentException($"Behavior profile '{profile.Name}' requires command '{required.Name}'.");

        var parameterDefinitions = profile.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in behavior.Parameters)
        {
            if (!parameterDefinitions.TryGetValue(pair.Key, out var parameter))
                throw new ArgumentException($"Behavior profile '{profile.Name}' does not define parameter '{pair.Key}'.");
            if (!double.IsFinite(pair.Value) || pair.Value < parameter.Minimum)
                throw new ArgumentException($"Behavior parameter '{pair.Key}' must be at least {parameter.Minimum}.");
        }

        if (profile.Name.Equals("pump", StringComparison.OrdinalIgnoreCase))
        {
            var normalOperatingTemperature = Parameter(behavior, "normalOperatingTemperature");
            var overheatTemperature = Parameter(behavior, "overheatTemperature");
            if (normalOperatingTemperature >= overheatTemperature)
                throw new ArgumentException("Behavior parameter 'normalOperatingTemperature' must be lower than 'overheatTemperature'.");
        }
    }

    public static double Parameter(DeviceBehaviorDefinition behavior, string name)
    {
        var profile = Get(behavior.Profile);
        var definition = profile.Parameters.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Behavior profile '{profile.Name}' does not define parameter '{name}'.", nameof(name));
        return behavior.Parameters.TryGetValue(name, out var value) ? value : definition.DefaultValue;
    }
}
