using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace IndustrialSim.Protocols.OpcUa;

public sealed class OpcUaAdapter : IProtocolAdapter
{
    private IDeviceRuntime? _runtime;
    private ApplicationInstance? _application;
    private readonly OpcUaTransportFaultController _transportFault = new();
    public string Name => "opcua";
    public bool IsRunning { get; private set; }
    public bool IsDisconnected { get; private set; }
    public TimeSpan Latency { get; private set; }
    public string Endpoint { get; private set; } = "opc.tcp://0.0.0.0:4840";
    public int Port { get; private set; }
    public bool IsStandardOpcUaServer => _application?.Server is IndustrialOpcUaServer && IsRunning;
    public event Action<DataPointChanged>? DataPointChanged;
    public IReadOnlyCollection<string> Nodes => _runtime is null ? [] : _runtime.Definition.DataPoints.Select(p => $"{_runtime.Definition.Id.Value}/{p.Name}").Concat(_runtime.Definition.Commands.Select(c => $"{_runtime.Definition.Id.Value}/{c.Name}")).ToArray();
    public void ApplyTransportFault(string fault, TimeSpan duration)
    {
        _transportFault.Apply(fault, duration);
        IsDisconnected = _transportFault.Mode is OpcUaTransportFaultMode.Disconnect or OpcUaTransportFaultMode.Timeout;
        Latency = _transportFault.Mode == OpcUaTransportFaultMode.Latency ? duration : TimeSpan.Zero;
        if (_transportFault.Mode == OpcUaTransportFaultMode.Disconnect) StopWireServerAsync().GetAwaiter().GetResult();
    }

    public void RecoverTransportFault()
    {
        var restart = _transportFault.Mode == OpcUaTransportFaultMode.Disconnect && IsRunning && _application is null && Port > 0;
        _transportFault.Recover();
        IsDisconnected = false;
        Latency = TimeSpan.Zero;
        if (restart) StartServerAsync(Port).GetAwaiter().GetResult();
    }
    public async Task StartAsync(IDeviceRuntime runtime, ProtocolOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime)); Endpoint = options.Endpoint ?? Endpoint;
        _runtime.State.DataPointChanged += OnDataPointChanged; IsRunning = true;
        if (options.Port > 0) await StartServerAsync(options.Port, cancellationToken);
    }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); await StopWireServerAsync();
        if (_runtime is not null) _runtime.State.DataPointChanged -= OnDataPointChanged;
        _application = null; _runtime = null; IsRunning = false; Port = 0;
    }
    public async Task StartServerAsync(int port = 4840, CancellationToken cancellationToken = default)
    {
        Ensure(); if (_application is not null) throw new InvalidOperationException("OPC UA server is already running.");
        Port = port; Endpoint = $"opc.tcp://0.0.0.0:{port}";
        var certificatePath = Path.Combine(Path.GetTempPath(), "industrial-sim-opcua", "certs");
        var trustPath = Path.Combine(Path.GetTempPath(), "industrial-sim-opcua", "trust");
        Directory.CreateDirectory(certificatePath); Directory.CreateDirectory(trustPath);
        var config = new ApplicationConfiguration { ApplicationName = "IndustrialSim", ApplicationUri = "urn:industrial-sim:server", ApplicationType = ApplicationType.Server,
            ServerConfiguration = new ServerConfiguration { BaseAddresses = new StringCollection { Endpoint }, SecurityPolicies = new ServerSecurityPolicyCollection { new ServerSecurityPolicy { SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None } } },
            SecurityConfiguration = new SecurityConfiguration { ApplicationCertificate = new CertificateIdentifier { StoreType = "Directory", StorePath = certificatePath, SubjectName = "CN=IndustrialSim" }, TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trustPath }, TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trustPath }, RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = trustPath }, AutoAcceptUntrustedCertificates = true }, TransportQuotas = new TransportQuotas(), TraceConfiguration = new TraceConfiguration() };
        await config.ValidateAsync(ApplicationType.Server, cancellationToken); _application = new ApplicationInstance(config, null!);
        await _application.CheckApplicationInstanceCertificatesAsync(true, 2048, cancellationToken); await _application.StartAsync(new IndustrialOpcUaServer(_runtime!, _transportFault));
    }
    public object? Read(string node) { EnsureTransport(); Ensure(); if (Latency > TimeSpan.Zero) Thread.Sleep(Latency); return _runtime!.Read(NodeName(node))?.Value; }
    public void Write(string node, object? value) { EnsureTransport(); Ensure(); var result = _runtime!.Write(NodeName(node), value); if (!result.Succeeded) throw new InvalidOperationException(result.Error); }
    public Task InvokeMethodAsync(string method, CancellationToken cancellationToken = default) { EnsureTransport(); Ensure(); return _runtime!.InvokeCommandAsync(NodeName(method), cancellationToken); }
    private static string NodeName(string node) => node[(node.LastIndexOf('/') + 1)..];
    private void Ensure() { if (!IsRunning || _runtime is null) throw new InvalidOperationException("OPC UA adapter is not running."); }
    private void EnsureTransport() { if (IsDisconnected) throw new IOException("OPC UA transport disconnected."); }
    private void OnDataPointChanged(DataPointChanged change) => DataPointChanged?.Invoke(change);

    private async Task StopWireServerAsync()
    {
        var application = _application;
        _application = null;
        if (application is not null) await application.StopAsync();
    }
}
