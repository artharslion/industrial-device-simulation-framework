namespace IndustrialSim.Observability.Events;

public sealed record RuntimeEventQuery(
    string? DeviceId = null,
    IReadOnlyList<string>? EventTypes = null,
    long? AfterSequence = null,
    int Limit = 100)
{
    internal bool Matches(RuntimeEventEnvelope envelope) =>
        (string.IsNullOrWhiteSpace(DeviceId) || envelope.DeviceId.Equals(DeviceId, StringComparison.OrdinalIgnoreCase)) &&
        (EventTypes is null || EventTypes.Count == 0 || EventTypes.Any(type => type.Equals(envelope.EventType, StringComparison.OrdinalIgnoreCase))) &&
        (!AfterSequence.HasValue || envelope.Sequence > AfterSequence.Value);
}
