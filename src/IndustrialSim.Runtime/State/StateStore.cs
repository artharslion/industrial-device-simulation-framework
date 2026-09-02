using IndustrialSim.Core.Domain;

namespace IndustrialSim.Runtime.State;

public sealed record StateBatchTransitionResult(bool Succeeded, IReadOnlyList<DataPointChanged> Events, string? Error)
{
    public static StateBatchTransitionResult Committed(IReadOnlyList<DataPointChanged> events) => new(true, events, null);
    public static StateBatchTransitionResult Rejected(string error) => new(false, [], error);
}

public sealed class StateStore
{
    private readonly DeviceDefinition _definition;
    private readonly Dictionary<string, ScalarValue?> _baseValues;
    private readonly Dictionary<string, ScalarValue?> _values;
    private readonly Dictionary<string, ValueProjection> _valueProjections = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public StateStore(DeviceDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _baseValues = definition.DataPoints.ToDictionary(
            point => point.Name,
            point => point.InitialValue,
            StringComparer.OrdinalIgnoreCase);
        _values = new Dictionary<string, ScalarValue?>(_baseValues, StringComparer.OrdinalIgnoreCase);
    }

    public event Action<DataPointChanged>? DataPointChanged;

    public DeviceDefinition Definition => _definition;

    public ScalarValue? Get(DataPointId dataPointId)
    {
        lock (_gate)
            return _values.TryGetValue(dataPointId.Value, out var value) ? value : null;
    }

    public ScalarValue? GetInternal(DataPointId dataPointId)
    {
        lock (_gate)
            return _baseValues.TryGetValue(dataPointId.Value, out var value) ? value : null;
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

    public StateBatchTransitionResult SetBatch(IEnumerable<(DataPointId DataPointId, object? Value)> updates, SimulationTime? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(updates);
        var requested = updates.ToArray();
        if (requested.Length == 0) return StateBatchTransitionResult.Committed([]);
        var duplicate = requested.GroupBy(update => update.DataPointId.Value, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) return StateBatchTransitionResult.Rejected($"Data point '{duplicate.Key}' appears more than once in a batch.");

        List<(DataPointDefinition Point, ScalarValue Normalized, ScalarValue Exposed, ScalarValue? Previous)> prepared = [];
        List<DataPointChanged> events = [];
        lock (_gate)
        {
            foreach (var update in requested)
            {
                var point = _definition.DataPoints.FirstOrDefault(item => item.Name.Equals(update.DataPointId.Value, StringComparison.OrdinalIgnoreCase));
                if (point is null) return StateBatchTransitionResult.Rejected($"Data point '{update.DataPointId.Value}' does not exist.");
                if (point.Access == DataPointAccess.Read) return StateBatchTransitionResult.Rejected($"Data point '{point.Name}' is read-only.");
                if (!ScalarValue.TryCreate(point.DataType, update.Value, out var normalized))
                    return StateBatchTransitionResult.Rejected($"Value is invalid for data point '{point.Name}' of type {point.DataType}.");
                prepared.Add((point, normalized!, ApplyValueProjections(point, normalized!), _values[point.Name]));
            }

            foreach (var update in prepared)
            {
                _baseValues[update.Point.Name] = update.Normalized;
                _values[update.Point.Name] = update.Exposed;
                if (!Equals(update.Previous, update.Exposed)) events.Add(Change(update.Point, update.Previous, update.Exposed, timestamp));
            }
        }
        foreach (var changed in events) DataPointChanged?.Invoke(changed);
        return StateBatchTransitionResult.Committed(events);
    }

    public void AddValueProjection(string id, DataPointId dataPointId, Func<ScalarValue, ScalarValue> projection, SimulationTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Projection id cannot be blank.", nameof(id));
        ArgumentNullException.ThrowIfNull(projection);
        DataPointChanged? changedEvent = null;
        lock (_gate)
        {
            var point = FindPoint(dataPointId);
            if (_valueProjections.ContainsKey(id)) throw new InvalidOperationException($"Projection '{id}' is already active.");
            var previous = _values[point.Name];
            _valueProjections[id] = new ValueProjection(dataPointId.Value, projection);
            if (_baseValues[point.Name] is { } baseValue)
            {
                var exposed = ApplyValueProjections(point, baseValue);
                _values[point.Name] = exposed;
                if (!Equals(previous, exposed)) changedEvent = Change(point, previous, exposed, timestamp);
            }
        }
        if (changedEvent is not null) DataPointChanged?.Invoke(changedEvent);
    }

    public bool RemoveValueProjection(string id, SimulationTime? timestamp = null)
    {
        DataPointChanged? changedEvent = null;
        lock (_gate)
        {
            if (!_valueProjections.Remove(id, out var removed)) return false;
            var point = FindPoint(new DataPointId(removed.DataPoint));
            var previous = _values[point.Name];
            if (_baseValues[point.Name] is { } baseValue)
            {
                var exposed = ApplyValueProjections(point, baseValue);
                _values[point.Name] = exposed;
                if (!Equals(previous, exposed)) changedEvent = Change(point, previous, exposed, timestamp);
            }
        }
        if (changedEvent is not null) DataPointChanged?.Invoke(changedEvent);
        return true;
    }

    private StateTransitionResult SetCore(DataPointId dataPointId, object? value, SimulationTime? timestamp, bool enforceAccess)
    {
        DataPointChanged? changedEvent = null;
        StateTransitionResult result;

        lock (_gate)
        {
            var point = _definition.DataPoints.FirstOrDefault(item => string.Equals(item.Name, dataPointId.Value, StringComparison.OrdinalIgnoreCase));
            if (point is null) return StateTransitionResult.Rejected($"Data point '{dataPointId.Value}' does not exist.");
            if (enforceAccess && point.Access is DataPointAccess.Read)
                return StateTransitionResult.Rejected($"Data point '{point.Name}' is read-only.");
            if (!ScalarValue.TryCreate(point.DataType, value, out var normalized))
                return StateTransitionResult.Rejected($"Value is invalid for data point '{point.Name}' of type {point.DataType}.");

            var previous = _values[point.Name];
            _baseValues[point.Name] = normalized;
            var exposed = ApplyValueProjections(point, normalized!);
            _values[point.Name] = exposed;
            if (Equals(previous, exposed)) return StateTransitionResult.Unchanged(exposed);

            changedEvent = Change(point, previous, exposed, timestamp);
            result = StateTransitionResult.ChangedResult(changedEvent);
        }

        DataPointChanged?.Invoke(changedEvent!);
        return result;
    }

    private ScalarValue ApplyValueProjections(DataPointDefinition point, ScalarValue value)
    {
        foreach (var projection in _valueProjections.Values.Where(item => item.DataPoint.Equals(point.Name, StringComparison.OrdinalIgnoreCase)))
        {
            value = projection.Apply(value) ?? throw new InvalidOperationException("A value projection returned null.");
            if (value.DataType != point.DataType) throw new InvalidOperationException($"A value projection changed data point '{point.Name}' to an incompatible type.");
        }
        return value;
    }

    private DataPointDefinition FindPoint(DataPointId dataPointId) =>
        _definition.DataPoints.FirstOrDefault(item => item.Name.Equals(dataPointId.Value, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Data point '{dataPointId.Value}' does not exist.", nameof(dataPointId));

    private DataPointChanged Change(DataPointDefinition point, ScalarValue? previous, ScalarValue value, SimulationTime? timestamp) =>
        new(timestamp ?? SimulationTime.Zero, _definition.Id, new DataPointId(point.Name), previous, value);

    private sealed record ValueProjection(string DataPoint, Func<ScalarValue, ScalarValue> Apply);
}
