using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Runtime.State;
using IndustrialSim.Core.Domain;
using System.Net;
using System.Net.Sockets;

namespace IndustrialSim.Protocols.OpcUa;

public sealed class OpcUaAdapter : IProtocolAdapter
{
    private IDeviceRuntime? _runtime;
    private TcpListener? _listener;
    private CancellationTokenSource? _serverCts;
    public string Name => "opcua";
    public bool IsRunning { get; private set; }
    public bool IsDisconnected { get; private set; }
    public TimeSpan Latency { get; private set; }
    public string Endpoint { get; private set; } = "opc.tcp://0.0.0.0:4840";
    public int Port { get; private set; }
    public event Action<DataPointChanged>? DataPointChanged;
    public IReadOnlyCollection<string> Nodes => _runtime is null ? Array.Empty<string>() : _runtime.Definition.DataPoints.Select(p => $"{_runtime.Definition.Id.Value}/{p.Name}").Concat(_runtime.Definition.Commands.Select(c => $"{_runtime.Definition.Id.Value}/{c.Name}")).ToArray();
    public void ApplyTransportFault(string fault, TimeSpan duration) { IsDisconnected = fault.Equals("disconnect", StringComparison.OrdinalIgnoreCase) || fault.Equals("timeout", StringComparison.OrdinalIgnoreCase); Latency = fault.Equals("latency", StringComparison.OrdinalIgnoreCase) ? duration : TimeSpan.Zero; }
    public void RecoverTransportFault() { IsDisconnected = false; Latency = TimeSpan.Zero; }
    public Task StartAsync(IDeviceRuntime runtime, ProtocolOptions options, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime)); Endpoint = options.Endpoint ?? "opc.tcp://0.0.0.0:4840"; IsRunning = true; _runtime.State.DataPointChanged += OnDataPointChanged; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _serverCts?.Cancel(); _listener?.Stop(); if (_runtime is not null) _runtime.State.DataPointChanged -= OnDataPointChanged; IsRunning = false; _runtime = null; return Task.CompletedTask; }
    public Task StartServerAsync(int port = 4840, CancellationToken cancellationToken = default)
    {
        Ensure(); _listener = new TcpListener(IPAddress.Any, port); _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _ = AcceptLoopAsync(_serverCts.Token); return Task.CompletedTask;
    }
    public object? Read(string node) { EnsureTransport(); Ensure(); return _runtime!.Read(NodeName(node))?.Value; }
    public void Write(string node, object? value) { Ensure(); var result = _runtime!.Write(NodeName(node), value); if (!result.Succeeded) throw new InvalidOperationException(result.Error); }
    public Task InvokeMethodAsync(string method, CancellationToken cancellationToken = default) { Ensure(); return _runtime!.InvokeCommandAsync(method[(method.LastIndexOf('/') + 1)..], cancellationToken); }
    private string NodeName(string node) => node[(node.LastIndexOf('/') + 1)..];
    private void Ensure() { if (!IsRunning || _runtime is null) throw new InvalidOperationException("OPC UA adapter is not running."); }
    private void EnsureTransport() { if (IsDisconnected) throw new IOException("OPC UA transport disconnected."); }
    private void OnDataPointChanged(DataPointChanged change) => DataPointChanged?.Invoke(change);
    private async Task AcceptLoopAsync(CancellationToken token) { while (!token.IsCancellationRequested) { try { using var c = await _listener!.AcceptTcpClientAsync(token); } catch { break; } } }
}
