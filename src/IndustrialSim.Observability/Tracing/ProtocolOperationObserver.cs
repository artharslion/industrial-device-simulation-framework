using IndustrialSim.Hosting;
using IndustrialSim.Observability.Metrics;
using IndustrialSim.Observability.Security;

namespace IndustrialSim.Observability.Tracing;

public sealed class ProtocolOperationObserver(SecretRedactor redactor) : IProtocolOperationObserver
{
    private readonly SecretRedactor _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));

    public IProtocolOperationScope Start(string deviceId, string protocol, string operation)
    {
        var normalizedOperation = operation.Equals("start", StringComparison.OrdinalIgnoreCase) ? "start" : "stop";
        return new Scope(IndustrialSimOperation.Start(
            $"industrial.protocol.{normalizedOperation}",
            _redactor,
            deviceId,
            normalizedOperation,
            MetricLabels.Protocol(protocol)));
    }

    private sealed class Scope(IndustrialSimOperation operation) : IProtocolOperationScope
    {
        private readonly IndustrialSimOperation _operation = operation;

        public void SetResult(bool succeeded, string? errorCode) =>
            _operation.SetResult(succeeded ? "success" : "failed", errorCode);

        public void Dispose() => _operation.Dispose();
    }
}
