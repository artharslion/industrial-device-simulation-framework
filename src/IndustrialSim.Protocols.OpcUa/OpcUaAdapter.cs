using IndustrialSim.Protocols.Abstractions;

namespace IndustrialSim.Protocols.OpcUa;

public sealed class OpcUaAdapter : IProtocolAdapter
{
    private IDeviceRuntime? _runtime;
    public string Name => "opcua";
    public bool IsRunning { get; private set; }
    public Task StartAsync(IDeviceRuntime runtime, ProtocolOptions options, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime)); IsRunning = true; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); IsRunning = false; _runtime = null; return Task.CompletedTask; }
    public object? Read(string node) { Ensure(); return _runtime!.Read(NodeName(node))?.Value; }
    public void Write(string node, object? value) { Ensure(); var result = _runtime!.Write(NodeName(node), value); if (!result.Succeeded) throw new InvalidOperationException(result.Error); }
    public Task InvokeMethodAsync(string method, CancellationToken cancellationToken = default) { Ensure(); return _runtime!.InvokeCommandAsync(method[(method.LastIndexOf('/') + 1)..], cancellationToken); }
    private string NodeName(string node) => node[(node.LastIndexOf('/') + 1)..];
    private void Ensure() { if (!IsRunning || _runtime is null) throw new InvalidOperationException("OPC UA adapter is not running."); }
}
