using System.Diagnostics;
using IndustrialSim.Observability.Security;

namespace IndustrialSim.Observability.Tracing;

public sealed class IndustrialSimOperation : IDisposable
{
    private static readonly HashSet<string> AllowedNames =
    [
        "industrial.device.create",
        "industrial.device.update",
        "industrial.device.remove",
        "industrial.device.lifecycle",
        "industrial.command.invoke",
        "industrial.state.write",
        "industrial.scenario.start",
        "industrial.scenario.stop",
        "industrial.fault.activate",
        "industrial.fault.recover",
        "industrial.protocol.start",
        "industrial.protocol.stop"
    ];

    private readonly Activity? _activity;
    private readonly SecretRedactor _redactor;
    private bool _hasResult;
    private bool _disposed;

    private IndustrialSimOperation(
        string name,
        SecretRedactor redactor,
        string? deviceId,
        string? action,
        string? protocol)
    {
        if (!AllowedNames.Contains(name)) throw new ArgumentException($"Unsupported trace operation '{name}'.", nameof(name));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _activity = IndustrialSimActivitySource.Source.StartActivity(name, ActivityKind.Internal);
        _activity?.SetTag("industrial.operation", name);
        SetTag("industrial.device.id", deviceId);
        SetTag("industrial.lifecycle.action", action);
        SetTag("industrial.protocol", protocol);
    }

    public static IndustrialSimOperation Start(
        string name,
        SecretRedactor redactor,
        string? deviceId = null,
        string? action = null,
        string? protocol = null) =>
        new(name, redactor, deviceId, action, protocol);

    public void SetResult(string result, string? errorCode = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = result.ToLowerInvariant() switch
        {
            "success" => "success",
            "rejected" => "rejected",
            "failed" => "failed",
            _ => "failed"
        };
        _activity?.SetTag("industrial.result", normalized);
        SetTag("industrial.error.code", errorCode);
        if (normalized != "success") _activity?.SetStatus(ActivityStatusCode.Error);
        _hasResult = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!_hasResult) SetResult("failed", "unhandled");
        _disposed = true;
        _activity?.Dispose();
    }

    private void SetTag(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _activity?.SetTag(key, _redactor.RedactText(value));
    }
}
