using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.Engine;

namespace IndustrialSim.Hosting.Snapshots;

public sealed record RuntimeSnapshotValue(DataType DataType, JsonElement Value);

public sealed record RuntimeSnapshot(
    string Id,
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    string DeviceId,
    string DefinitionFingerprint,
    bool Deterministic,
    int Seed,
    TimeSpan SimulationTime,
    EngineState EngineState,
    IReadOnlyDictionary<string, RuntimeSnapshotValue> State);

public sealed class SnapshotCompatibilityException(string message, string errorCode) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class RuntimeSnapshotService
{
    public const int CurrentSchemaVersion = 1;

    public RuntimeSnapshot Capture(SimulationHost host, string id, DateTimeOffset? createdUtc = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Snapshot id cannot be blank.", nameof(id));
        var state = host.Runtime.Definition.DataPoints.ToDictionary(
            point => point.Name,
            point => new RuntimeSnapshotValue(
                point.DataType,
                JsonSerializer.SerializeToElement(host.State.GetExposedInternal(new DataPointId(point.Name))?.Value, ValueType(point.DataType))),
            StringComparer.OrdinalIgnoreCase);
        return new RuntimeSnapshot(
            id,
            CurrentSchemaVersion,
            createdUtc ?? DateTimeOffset.UtcNow,
            host.Runtime.Definition.Id.Value,
            Fingerprint(host.Runtime.Definition),
            host.IsDeterministic,
            host.Seed,
            host.Engine.CurrentTime.Elapsed,
            host.Engine.State,
            state);
    }

    public void Restore(SimulationHost host, RuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateCompatibility(host, snapshot);

        var values = host.Runtime.Definition.DataPoints.Select(point =>
        {
            if (!snapshot.State.TryGetValue(point.Name, out var stored))
                throw new SnapshotCompatibilityException($"Snapshot is missing data point '{point.Name}'.", "snapshotStateMismatch");
            if (stored.DataType != point.DataType)
                throw new SnapshotCompatibilityException($"Snapshot data point '{point.Name}' has an incompatible type.", "snapshotStateMismatch");
            var value = JsonSerializer.Deserialize(stored.Value.GetRawText(), ValueType(point.DataType));
            if (!ScalarValue.TryCreate(point.DataType, value, out _))
                throw new SnapshotCompatibilityException($"Snapshot value for '{point.Name}' is invalid.", "snapshotStateMismatch");
            return (Point: point, Value: value);
        }).ToArray();

        host.StopScenario();
        host.Engine.Reset();
        foreach (var item in values)
            host.State.SetInternal(new DataPointId(item.Point.Name), item.Value, new SimulationTime(snapshot.SimulationTime));
        host.Engine.RestoreTime(snapshot.SimulationTime);
        if (snapshot.EngineState == EngineState.Running)
            host.Engine.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        else if (snapshot.EngineState == EngineState.Paused)
        {
            host.Engine.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            host.Engine.Pause();
        }
    }

    public static string Fingerprint(DeviceDefinition definition)
    {
        var canonical = new StringBuilder(definition.Id.Value).Append('|').Append(definition.Type);
        foreach (var point in definition.DataPoints.OrderBy(point => point.Name, StringComparer.OrdinalIgnoreCase))
            canonical.Append('|').Append(point.Name).Append(':').Append(point.DataType).Append(':').Append(point.Access);
        foreach (var command in definition.Commands.OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase))
            canonical.Append("|cmd:").Append(command.Name);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void ValidateCompatibility(SimulationHost host, RuntimeSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
            throw new SnapshotCompatibilityException($"Snapshot schema {snapshot.SchemaVersion} is not supported.", "snapshotVersionUnsupported");
        if (!snapshot.DeviceId.Equals(host.Runtime.Definition.Id.Value, StringComparison.OrdinalIgnoreCase))
            throw new SnapshotCompatibilityException("Snapshot belongs to another device.", "snapshotDeviceMismatch");
        if (snapshot.DefinitionFingerprint != Fingerprint(host.Runtime.Definition))
            throw new SnapshotCompatibilityException("Snapshot device definition is incompatible.", "snapshotDefinitionMismatch");
        if (snapshot.Deterministic != host.IsDeterministic)
            throw new SnapshotCompatibilityException("Snapshot clock mode is incompatible.", "snapshotClockMismatch");
        if (snapshot.Seed != host.Seed)
            throw new SnapshotCompatibilityException("Snapshot seed is incompatible.", "snapshotSeedMismatch");
    }

    private static Type ValueType(DataType type) => type switch
    {
        DataType.Boolean => typeof(bool),
        DataType.Int8 => typeof(sbyte),
        DataType.Int16 => typeof(short),
        DataType.Int32 => typeof(int),
        DataType.Int64 => typeof(long),
        DataType.UInt8 => typeof(byte),
        DataType.UInt16 => typeof(ushort),
        DataType.UInt32 => typeof(uint),
        DataType.UInt64 => typeof(ulong),
        DataType.Float => typeof(float),
        DataType.Double => typeof(double),
        DataType.String => typeof(string),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported snapshot data type.")
    };
}
