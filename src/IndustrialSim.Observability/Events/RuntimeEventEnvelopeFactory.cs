using System.Diagnostics;
using System.Text.Json;
using IndustrialSim.Core.Domain;
using IndustrialSim.Faults;
using IndustrialSim.Observability.Security;

namespace IndustrialSim.Observability.Events;

public sealed class RuntimeEventEnvelopeFactory(SecretRedactor redactor)
{
    private readonly SecretRedactor _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));

    public RuntimeEventEnvelope Create(
        RuntimeEvent runtimeEvent,
        long sequence,
        DateTimeOffset observedAtUtc,
        ActivityContext? activityContext = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        var data = runtimeEvent switch
        {
            DataPointChanged change => JsonSerializer.SerializeToElement(new
            {
                dataPoint = change.DataPointId.Value,
                previousValue = _redactor.RedactValue(change.DataPointId.Value, change.PreviousValue?.Value),
                newValue = _redactor.RedactValue(change.DataPointId.Value, change.NewValue.Value)
            }),
            CommandExecuted command => JsonSerializer.SerializeToElement(new
            {
                command = _redactor.RedactText(command.CommandName)
            }),
            DeviceStarted => JsonSerializer.SerializeToElement(new { lifecycle = "started" }),
            DeviceStopped => JsonSerializer.SerializeToElement(new { lifecycle = "stopped" }),
            _ => JsonSerializer.SerializeToElement(new { })
        };

        return CreateEnvelope(
            sequence,
            observedAtUtc,
            runtimeEvent.Timestamp.Elapsed,
            runtimeEvent.DeviceId.Value,
            runtimeEvent.GetType().Name,
            data,
            runtimeEvent.Metadata,
            activityContext);
    }

    public RuntimeEventEnvelope Create(
        FaultEvent faultEvent,
        long sequence,
        DateTimeOffset observedAtUtc,
        ActivityContext? activityContext = null)
    {
        ArgumentNullException.ThrowIfNull(faultEvent);
        var fault = faultEvent.Fault;
        var data = JsonSerializer.SerializeToElement(new
        {
            faultId = _redactor.RedactText(fault.Id),
            category = fault.Category.ToString().ToLowerInvariant(),
            lifecycle = faultEvent.Lifecycle.ToString().ToLowerInvariant(),
            target = _redactor.RedactText(fault.Target),
            type = _redactor.RedactText(fault.Type),
            durationMs = fault.Duration?.TotalMilliseconds
        });
        return CreateEnvelope(
            sequence,
            observedAtUtc,
            faultEvent.Timestamp.Elapsed,
            fault.Device,
            $"Fault{faultEvent.Lifecycle}",
            data,
            fault.Metadata,
            activityContext);
    }

    public RuntimeEventEnvelope CreateObservation(
        long sequence,
        DateTimeOffset observedAtUtc,
        TimeSpan simulationTime,
        string deviceId,
        string eventType,
        object data,
        IReadOnlyDictionary<string, string>? metadata = null,
        ActivityContext? activityContext = null) =>
        CreateEnvelope(
            sequence,
            observedAtUtc,
            simulationTime,
            deviceId,
            eventType,
            JsonSerializer.SerializeToElement(data),
            metadata,
            activityContext);

    private RuntimeEventEnvelope CreateEnvelope(
        long sequence,
        DateTimeOffset observedAtUtc,
        TimeSpan simulationTime,
        string deviceId,
        string eventType,
        JsonElement data,
        IReadOnlyDictionary<string, string>? metadata,
        ActivityContext? activityContext)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Device id cannot be blank.", nameof(deviceId));
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("Event type cannot be blank.", nameof(eventType));
        ActivityContext? context = activityContext is { } supplied && supplied != default ? supplied : null;
        return new RuntimeEventEnvelope(
            sequence,
            observedAtUtc.ToUniversalTime(),
            simulationTime,
            _redactor.RedactText(deviceId),
            eventType,
            data.Clone(),
            _redactor.RedactMetadata(metadata),
            context?.TraceId.ToHexString(),
            context?.SpanId.ToHexString());
    }
}
