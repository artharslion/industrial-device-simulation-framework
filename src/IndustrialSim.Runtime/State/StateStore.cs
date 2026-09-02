using IndustrialSim.Core.Domain;

namespace IndustrialSim.Runtime.State;

public sealed class StateStore
{
    private readonly DeviceDefinition _definition;
    private readonly Dictionary<string, ScalarValue?> _values;
    private readonly object _gate = new();

    public StateStore(DeviceDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _values = definition.DataPoints.ToDictionary(
            point => point.Name,
            point => point.InitialValue,
            StringComparer.OrdinalIgnoreCase);
    }

    public event Action<DataPointChanged>? DataPointChanged;

    public DeviceDefinition Definition => _definition;

    public ScalarValue? Get(DataPointId dataPointId)
    {
        lock (_gate)
            return _values.TryGetValue(dataPointId.Value, out var value) ? value : null;
    }

    public IReadOnlyDictionary<string, ScalarValue?> Snapshot()
    {
        lock (_gate)
            return new Dictionary<string, ScalarValue?>(_values, StringComparer.OrdinalIgnoreCase);
    }

    public StateTransitionResult Set(DataPointId dataPointId, object? value, SimulationTime? timestamp = null)
        => SetCore(dataPointId, value, timestamp, enforceAccess: true);

    public StateTransitionResult SetInternal(DataPointId dataPointId, object? value, SimulationTime? timestamp = null)
        => SetCore(dataPointId, value, timestamp, enforceAccess: false);

    private StateTransitionResult SetCore(DataPointId dataPointId, object? value, SimulationTime? timestamp, bool enforceAccess)
    {
        DataPointChanged? changedEvent = null;
        StateTransitionResult result;

        lock (_gate)
        {
            var point = _definition.DataPoints.FirstOrDefault(item =>
                string.Equals(item.Name, dataPointId.Value, StringComparison.OrdinalIgnoreCase));
            if (point is null)
                return StateTransitionResult.Rejected($"Data point '{dataPointId.Value}' does not exist.");
            if (enforceAccess && point.Access is DataPointAccess.Read)
                return StateTransitionResult.Rejected($"Data point '{point.Name}' is read-only.");
            if (!ScalarValue.TryCreate(point.DataType, value, out var normalized))
                return StateTransitionResult.Rejected($"Value is invalid for data point '{point.Name}' of type {point.DataType}.");

            var previous = _values[point.Name];
            if (Equals(previous, normalized))
                return StateTransitionResult.Unchanged(normalized!);

            _values[point.Name] = normalized;
            changedEvent = new DataPointChanged(
                timestamp ?? SimulationTime.Zero,
                _definition.Id,
                new DataPointId(point.Name),
                previous,
                normalized!);
            result = StateTransitionResult.ChangedResult(changedEvent);
        }

        DataPointChanged?.Invoke(changedEvent!);
        return result;
    }
}
