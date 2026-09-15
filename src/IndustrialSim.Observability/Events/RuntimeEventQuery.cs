namespace IndustrialSim.Observability.Events;

public sealed record RuntimeEventQuery(
    string? DeviceId = null,
    IReadOnlyList<string>? EventTypes = null,
    long? AfterSequence = null,
    int Limit = 100,
    string? Search = null)
{
    internal bool Matches(RuntimeEventEnvelope envelope) =>
        (string.IsNullOrWhiteSpace(DeviceId) || envelope.DeviceId.Equals(DeviceId, StringComparison.OrdinalIgnoreCase)) &&
        (EventTypes is null || EventTypes.Count == 0 || EventTypes.Any(type => type.Equals(envelope.EventType, StringComparison.OrdinalIgnoreCase))) &&
        (!AfterSequence.HasValue || envelope.Sequence > AfterSequence.Value) &&
        MatchesSearch(envelope);

    private bool MatchesSearch(RuntimeEventEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var search = Search.Trim();
        return envelope.DeviceId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || envelope.EventType.Contains(search, StringComparison.OrdinalIgnoreCase)
            || envelope.Data.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
            || envelope.Metadata.Any(pair => pair.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
                || pair.Value.Contains(search, StringComparison.OrdinalIgnoreCase));
    }
}
