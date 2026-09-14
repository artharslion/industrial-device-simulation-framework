using System.Text.Json;

namespace IndustrialSim.Observability.Events;

public sealed record RuntimeEventEnvelope(
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    TimeSpan SimulationTime,
    string DeviceId,
    string EventType,
    JsonElement Data,
    IReadOnlyDictionary<string, string> Metadata,
    string? TraceId,
    string? SpanId);
