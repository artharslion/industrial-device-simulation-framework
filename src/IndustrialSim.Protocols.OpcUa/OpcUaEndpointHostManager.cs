using IndustrialSim.Protocols.Abstractions;
using Opc.Ua;
using Opc.Ua.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace IndustrialSim.Protocols.OpcUa;

public sealed class OpcUaEndpointHostManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<OpcUaEndpointDescriptor, EndpointHost> _hosts = [];
    private readonly Dictionary<int, OpcUaEndpointDescriptor> _ports = [];
    private bool _disposed;

    public int HostCount
    {
        get
        {
            _gate.Wait();
            try { return _hosts.Count; }
            finally { _gate.Release(); }
        }
    }

    public int MemberCount(string endpoint)
    {
        var descriptor = OpcUaEndpointDescriptor.Parse(endpoint);
        _gate.Wait();
        try { return _hosts.TryGetValue(descriptor, out var host) ? host.MemberCount : 0; }
        finally { _gate.Release(); }
    }

    internal async Task<OpcUaDeviceRegistration> RegisterAsync(
        string endpoint,
        string simulationKey,
        IDeviceRuntime runtime,
        IReadOnlyDictionary<string, string> dataPointNodeIds,
        OpcUaTransportFaultController fault,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var descriptor = OpcUaEndpointDescriptor.Parse(endpoint);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_ports.TryGetValue(descriptor.Port, out var owner) && owner != descriptor)
                throw new InvalidOperationException(
                    $"OPC UA port {descriptor.Port} already hosts endpoint '{owner.Endpoint}' and cannot also host '{descriptor.Endpoint}'.");

            var projection = new OpcUaDeviceProjection(simulationKey, runtime, dataPointNodeIds, fault);
            if (_hosts.TryGetValue(descriptor, out var existing))
            {
                existing.Add(projection);
                return new OpcUaDeviceRegistration(this, descriptor, simulationKey);
            }

            var host = new EndpointHost(descriptor);
            try
            {
                await host.StartAsync(projection, cancellationToken);
                _hosts.Add(descriptor, host);
                _ports.Add(descriptor.Port, descriptor);
                return new OpcUaDeviceRegistration(this, descriptor, simulationKey);
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal bool IsRunning(OpcUaEndpointDescriptor descriptor)
    {
        _gate.Wait();
        try { return _hosts.TryGetValue(descriptor, out var host) && host.IsRunning; }
        finally { _gate.Release(); }
    }

    internal async Task UnregisterAsync(OpcUaEndpointDescriptor descriptor, string simulationKey)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_hosts.TryGetValue(descriptor, out var host) || !host.Remove(simulationKey)) return;
            if (host.MemberCount > 0) return;

            await host.DisposeAsync();
            _hosts.Remove(descriptor);
            _ports.Remove(descriptor.Port);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal void Refresh(OpcUaEndpointDescriptor descriptor, string simulationKey)
    {
        _gate.Wait();
        try
        {
            if (_hosts.TryGetValue(descriptor, out var host)) host.Refresh(simulationKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync();
        try
        {
            foreach (var host in _hosts.Values) await host.DisposeAsync();
            _hosts.Clear();
            _ports.Clear();
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private sealed class EndpointHost(OpcUaEndpointDescriptor descriptor) : IAsyncDisposable
    {
        private ApplicationInstance? _application;
        private IndustrialOpcUaServer? _server;
        private readonly HashSet<string> _members = new(StringComparer.OrdinalIgnoreCase);

        public int MemberCount => _members.Count;
        public bool IsRunning => _application is not null && _server is not null;

        public async Task StartAsync(OpcUaDeviceProjection projection, CancellationToken cancellationToken)
        {
            if (!_members.Add(projection.SimulationKey))
                throw new InvalidOperationException($"OPC UA device '{projection.SimulationKey}' is already registered on endpoint '{descriptor.Endpoint}'.");

            var configuration = await CreateConfigurationAsync(descriptor, cancellationToken);
            _server = new IndustrialOpcUaServer([projection]);
            _application = new ApplicationInstance(configuration, null!);
            try
            {
                await _application.CheckApplicationInstanceCertificatesAsync(true, null, cancellationToken);
                await _application.StartAsync(_server);
            }
            catch
            {
                _members.Remove(projection.SimulationKey);
                _application = null;
                _server = null;
                throw;
            }
        }

        public void Add(OpcUaDeviceProjection projection)
        {
            if (!_members.Add(projection.SimulationKey))
                throw new InvalidOperationException($"OPC UA device '{projection.SimulationKey}' is already registered on endpoint '{descriptor.Endpoint}'.");
            try { _server!.AddDevice(projection); }
            catch
            {
                _members.Remove(projection.SimulationKey);
                throw;
            }
        }

        public bool Remove(string simulationKey)
        {
            if (!_members.Remove(simulationKey)) return false;
            _server!.RemoveDevice(simulationKey);
            return true;
        }

        public void Refresh(string simulationKey) => _server?.RefreshDevice(simulationKey);

        public async ValueTask DisposeAsync()
        {
            var application = _application;
            _application = null;
            _server = null;
            _members.Clear();
            if (application is not null) await application.StopAsync();
        }
    }

    private static async Task<ApplicationConfiguration> CreateConfigurationAsync(
        OpcUaEndpointDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var certificateKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(descriptor.Endpoint)))[..16];
        var certificatePath = Path.Combine(Path.GetTempPath(), "industrial-sim-opcua", "certs", certificateKey);
        var trustPath = Path.Combine(Path.GetTempPath(), "industrial-sim-opcua", "trust");
        Directory.CreateDirectory(certificatePath);
        Directory.CreateDirectory(trustPath);
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "IndustrialSim",
            ApplicationUri = "urn:industrial-sim:server",
            ApplicationType = ApplicationType.Server,
            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = new StringCollection { descriptor.Endpoint },
                SecurityPolicies = new ServerSecurityPolicyCollection
                {
                    new ServerSecurityPolicy
                    {
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                }
            },
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = "Directory",
                    StorePath = certificatePath,
                    SubjectName = "CN=IndustrialSim, O=OPC Foundation"
                },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trustPath },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trustPath },
                RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = trustPath },
                AutoAcceptUntrustedCertificates = true
            },
            TransportQuotas = new TransportQuotas(),
            TraceConfiguration = new TraceConfiguration()
        };
        await configuration.ValidateAsync(ApplicationType.Server, cancellationToken);
        return configuration;
    }
}

public sealed class OpcUaDeviceRegistration : IAsyncDisposable
{
    private OpcUaEndpointHostManager? _manager;
    private readonly string _simulationKey;

    internal OpcUaDeviceRegistration(
        OpcUaEndpointHostManager manager,
        OpcUaEndpointDescriptor descriptor,
        string simulationKey)
    {
        _manager = manager;
        Descriptor = descriptor;
        _simulationKey = simulationKey;
    }

    public OpcUaEndpointDescriptor Descriptor { get; }
    public bool IsServerRunning => _manager?.IsRunning(Descriptor) == true;

    internal void Refresh() => _manager?.Refresh(Descriptor, _simulationKey);

    public async ValueTask DisposeAsync()
    {
        var manager = Interlocked.Exchange(ref _manager, null);
        if (manager is not null) await manager.UnregisterAsync(Descriptor, _simulationKey);
    }
}
