namespace IndustrialSim.Core.Domain;

public readonly record struct DeviceId
{
    public DeviceId(string value) => Value = Validate(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;

    private static string Validate(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Identifier cannot be blank.", parameterName)
            : value.Trim();
}

public readonly record struct DataPointId
{
    public DataPointId(string value) => Value = Validate(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;

    private static string Validate(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Identifier cannot be blank.", parameterName)
            : value.Trim();
}

public readonly record struct CommandId
{
    public CommandId(string value) => Value = Validate(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;

    private static string Validate(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Identifier cannot be blank.", parameterName)
            : value.Trim();
}

public readonly record struct EventId
{
    public EventId(string value) => Value = Validate(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;

    private static string Validate(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Identifier cannot be blank.", parameterName)
            : value.Trim();
}
