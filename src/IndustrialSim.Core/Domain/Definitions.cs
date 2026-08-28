namespace IndustrialSim.Core.Domain;

public interface IDataPoint
{
    string Name { get; }
    DataType DataType { get; }
    object? Value { get; }
    string? Unit { get; }
    string? Description { get; }
    DataPointAccess Access { get; }
}

public interface ICommand
{
    string Name { get; }
}

public interface IEventDefinition
{
    string Name { get; }
}

public interface IDevice
{
    string Id { get; }
    string Type { get; }
    IReadOnlyCollection<IDataPoint> DataPoints { get; }
    IReadOnlyCollection<ICommand> Commands { get; }
    IReadOnlyCollection<IEventDefinition> Events { get; }
}

public sealed class DataPointDefinition : IDataPoint
{
    public DataPointDefinition(
        string name,
        DataType dataType,
        DataPointAccess access,
        object? initial = null,
        string? unit = null,
        string? description = null)
    {
        Name = ValidateName(name, nameof(name));
        if (!Enum.IsDefined(access))
            throw new ArgumentOutOfRangeException(nameof(access), access, "Unsupported access mode.");

        DataType = dataType;
        Access = access;
        InitialValue = initial is null ? null : ScalarValue.Create(dataType, initial);
        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public string Name { get; }
    public DataType DataType { get; }
    public DataPointAccess Access { get; }
    public ScalarValue? InitialValue { get; }
    public object? Value => InitialValue?.Value;
    public string? Unit { get; }
    public string? Description { get; }

    private static string ValidateName(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Name cannot be blank.", parameterName)
            : value.Trim();
}

public sealed class CommandDefinition : ICommand
{
    public CommandDefinition(string name) => Name = ValidateName(name, nameof(name));
    public string Name { get; }

    private static string ValidateName(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Name cannot be blank.", parameterName)
            : value.Trim();
}

public sealed class EventDefinition : IEventDefinition
{
    public EventDefinition(string name) => Name = ValidateName(name, nameof(name));
    public string Name { get; }

    private static string ValidateName(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Name cannot be blank.", parameterName)
            : value.Trim();
}

public sealed class DeviceDefinition : IDevice
{
    public DeviceDefinition(
        DeviceId id,
        string type,
        IEnumerable<DataPointDefinition>? dataPoints = null,
        IEnumerable<CommandDefinition>? commands = null,
        IEnumerable<EventDefinition>? events = null)
    {
        Id = id;
        Type = string.IsNullOrWhiteSpace(type)
            ? throw new ArgumentException("Device type cannot be blank.", nameof(type))
            : type.Trim();

        DataPoints = CopyAndValidate(dataPoints ?? [], nameof(dataPoints));
        Commands = CopyAndValidate(commands ?? [], nameof(commands));
        Events = CopyAndValidate(events ?? [], nameof(events));
    }

    public DeviceId Id { get; }
    public string Type { get; }
    public IReadOnlyList<DataPointDefinition> DataPoints { get; }
    public IReadOnlyList<CommandDefinition> Commands { get; }
    public IReadOnlyList<EventDefinition> Events { get; }

    string IDevice.Id => Id.Value;
    IReadOnlyCollection<IDataPoint> IDevice.DataPoints => DataPoints;
    IReadOnlyCollection<ICommand> IDevice.Commands => Commands;
    IReadOnlyCollection<IEventDefinition> IDevice.Events => Events;

    private static IReadOnlyList<T> CopyAndValidate<T>(IEnumerable<T> definitions, string parameterName)
        where T : class
    {
        var items = definitions.ToArray();
        if (items.Any(item => item is null))
            throw new ArgumentException("Definitions cannot contain null entries.", parameterName);

        var duplicate = items.GroupBy(item => item switch
        {
            DataPointDefinition point => point.Name,
            CommandDefinition command => command.Name,
            EventDefinition @event => @event.Name,
            _ => item.ToString() ?? string.Empty
        }, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException($"Duplicate definition name '{duplicate.Key}'.", parameterName);

        return Array.AsReadOnly(items);
    }
}
