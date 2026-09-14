using System.Collections.Concurrent;
using System.Diagnostics;
using IndustrialSim.Observability.Security;
using IndustrialSim.Observability.Tracing;

namespace IndustrialSim.Observability.Tests;

public sealed class TraceRedactionTests
{
    [Fact]
    public void Custom_operation_tags_are_allowlisted_and_secret_redacted()
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == IndustrialSimActivitySource.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue
        };
        ActivitySource.AddActivityListener(listener);
        var redactor = new SecretRedactor();

        using (var operation = IndustrialSimOperation.Start(
            "industrial.state.write",
            redactor,
            deviceId: "pump Bearer raw-token",
            action: "write Password=hunter2"))
        {
            operation.SetResult("failed", "Pwd=database-secret");
        }

        var activity = Assert.Single(stopped);
        var tags = activity.Tags.ToDictionary(tag => tag.Key, tag => tag.Value ?? string.Empty, StringComparer.Ordinal);
        Assert.Equal(
            ["industrial.device.id", "industrial.error.code", "industrial.lifecycle.action", "industrial.operation", "industrial.result"],
            tags.Keys.Order(StringComparer.Ordinal).ToArray());
        var serialized = string.Join(';', tags.Select(tag => $"{tag.Key}={tag.Value}"));
        Assert.DoesNotContain("raw-token", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("database-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("request.body", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("datapoint.value", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SecretRedactor.RedactedValue, serialized, StringComparison.Ordinal);
    }
}
