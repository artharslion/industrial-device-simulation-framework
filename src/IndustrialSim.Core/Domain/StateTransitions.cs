namespace IndustrialSim.Core.Domain;

public abstract record RuntimeEvent(
    SimulationTime Timestamp,
    DeviceId DeviceId,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DataPointChanged : RuntimeEvent
{
    public DataPointChanged(
        SimulationTime timestamp,
        DeviceId deviceId,
        DataPointId dataPointId,
        ScalarValue? previousValue,
        ScalarValue newValue,
        IReadOnlyDictionary<string, string>? metadata = null)
        : base(timestamp, deviceId, CopyMetadata(metadata))
    {
        DataPointId = dataPointId;
        PreviousValue = previousValue;
        NewValue = newValue ?? throw new ArgumentNullException(nameof(newValue));
    }

    public DataPointId DataPointId { get; }
    public ScalarValue? PreviousValue { get; }
    public ScalarValue NewValue { get; }

    private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string>? metadata) =>
        new Dictionary<string, string>(metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal);
}

public sealed record CommandExecuted(
    SimulationTime Timestamp,
    DeviceId DeviceId,
    string CommandName,
    IReadOnlyDictionary<string, string>? EventMetadata = null)
    : RuntimeEvent(Timestamp, DeviceId, new Dictionary<string, string>(EventMetadata ?? new Dictionary<string, string>(), StringComparer.Ordinal));

public sealed record DeviceStarted(
    SimulationTime Timestamp,
    DeviceId DeviceId,
    IReadOnlyDictionary<string, string>? EventMetadata = null)
    : RuntimeEvent(Timestamp, DeviceId, new Dictionary<string, string>(EventMetadata ?? new Dictionary<string, string>(), StringComparer.Ordinal));

public sealed record DeviceStopped(
    SimulationTime Timestamp,
    DeviceId DeviceId,
    IReadOnlyDictionary<string, string>? EventMetadata = null)
    : RuntimeEvent(Timestamp, DeviceId, new Dictionary<string, string>(EventMetadata ?? new Dictionary<string, string>(), StringComparer.Ordinal));

public sealed record StateTransitionResult
{
    private StateTransitionResult(bool succeeded, bool changed, ScalarValue? currentValue, DataPointChanged? @event, string? error)
    {
        Succeeded = succeeded;
        Changed = changed;
        CurrentValue = currentValue;
        Event = @event;
        Error = error;
    }

    public bool Succeeded { get; }
    public bool Changed { get; }
    public ScalarValue? CurrentValue { get; }
    public DataPointChanged? Event { get; }
    public string? Error { get; }

    public static StateTransitionResult ChangedResult(DataPointChanged @event) =>
        new(true, true, @event.NewValue, @event, null);

    public static StateTransitionResult Unchanged(ScalarValue currentValue) =>
        new(true, false, currentValue ?? throw new ArgumentNullException(nameof(currentValue)), null, null);

    public static StateTransitionResult Rejected(string error) =>
        new(false, false, null, null, string.IsNullOrWhiteSpace(error)
            ? throw new ArgumentException("An error message is required.", nameof(error))
            : error);
}
