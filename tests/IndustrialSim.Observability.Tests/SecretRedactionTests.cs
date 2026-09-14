using System.Diagnostics;
using IndustrialSim.Core.Domain;
using IndustrialSim.Faults;
using IndustrialSim.Observability.Events;
using IndustrialSim.Observability.Security;

namespace IndustrialSim.Observability.Tests;

public sealed class SecretRedactionTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("client-secret")]
    [InlineData("access_token")]
    [InlineData("ConnectionStrings:IndustrialSim")]
    [InlineData("Authorization")]
    public void Secret_like_keys_are_redacted(string key)
    {
        var redactor = new SecretRedactor();

        Assert.Equal(SecretRedactor.RedactedValue, redactor.Redact(key, "sensitive-value"));
    }

    [Fact]
    public void Credential_text_patterns_are_redacted_without_hiding_normal_text()
    {
        var redactor = new SecretRedactor();
        var input = "Bearer abc.def.ghi; Password=hunter2; endpoint=https://user:pass@example.test/path; status=ready";

        var result = redactor.RedactText(input);

        Assert.DoesNotContain("abc.def.ghi", result, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pass", result, StringComparison.Ordinal);
        Assert.Contains("status=ready", result, StringComparison.Ordinal);
        Assert.Contains(SecretRedactor.RedactedValue, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_event_envelope_is_structured_immutable_redacted_and_correlated()
    {
        var metadata = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer should-not-escape",
            ["source"] = "api"
        };
        var runtimeEvent = new DataPointChanged(
            new SimulationTime(TimeSpan.FromSeconds(3)),
            new DeviceId("pump-001"),
            new DataPointId("api-key"),
            ScalarValue.Create(DataType.String, "old-secret"),
            ScalarValue.Create(DataType.String, "new-secret"),
            metadata);
        var activityContext = new ActivityContext(
            ActivityTraceId.CreateRandom(),
            ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);

        var envelope = new RuntimeEventEnvelopeFactory(new SecretRedactor()).Create(
            runtimeEvent,
            7,
            new DateTimeOffset(2026, 9, 14, 6, 0, 0, TimeSpan.Zero),
            activityContext);
        metadata["source"] = "mutated";

        Assert.Equal(7, envelope.Sequence);
        Assert.Equal("pump-001", envelope.DeviceId);
        Assert.Equal("DataPointChanged", envelope.EventType);
        Assert.Equal(TimeSpan.FromSeconds(3), envelope.SimulationTime);
        Assert.Equal("api", envelope.Metadata["source"]);
        Assert.Equal(SecretRedactor.RedactedValue, envelope.Metadata["Authorization"]);
        Assert.Equal(SecretRedactor.RedactedValue, envelope.Data.GetProperty("previousValue").GetString());
        Assert.Equal(SecretRedactor.RedactedValue, envelope.Data.GetProperty("newValue").GetString());
        Assert.Equal(activityContext.TraceId.ToHexString(), envelope.TraceId);
        Assert.Equal(activityContext.SpanId.ToHexString(), envelope.SpanId);
    }

    [Fact]
    public void Fault_envelope_redacts_metadata_but_preserves_operational_fields()
    {
        var fault = new FaultEvent(
            new FaultSpec(
                "fault-1",
                FaultCategory.Network,
                "pump-001",
                "modbus",
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                "timeout",
                new Dictionary<string, string> { ["api_key"] = "raw-key", ["reason"] = "test" }),
            FaultLifecycle.Active,
            new SimulationTime(TimeSpan.FromSeconds(2)));

        var envelope = new RuntimeEventEnvelopeFactory(new SecretRedactor()).Create(
            fault,
            8,
            DateTimeOffset.UnixEpoch);

        Assert.Equal("FaultActive", envelope.EventType);
        Assert.Equal("network", envelope.Data.GetProperty("category").GetString());
        Assert.Equal("modbus", envelope.Data.GetProperty("target").GetString());
        Assert.Equal(SecretRedactor.RedactedValue, envelope.Metadata["api_key"]);
        Assert.Equal("test", envelope.Metadata["reason"]);
        Assert.DoesNotContain("raw-key", envelope.Data.GetRawText(), StringComparison.Ordinal);
    }
}
