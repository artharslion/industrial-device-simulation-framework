using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Protocols.OpcUa;
using Opc.Ua;
using Opc.Ua.Client;
using System.Net;
using System.Net.Sockets;

namespace IndustrialSim.Protocols.OpcUa.Tests;

public class ProtocolContractTests
{
    [Fact]
    public async Task Runtime_contract_supports_state_reads_writes_commands_and_events()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("pump-001"), "pump", new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) }, new[] { new CommandDefinition("start") }));
        var changes = 0; runtime.State.DataPointChanged += _ => changes++;
        Assert.Equal(0, runtime.Read("speed")!.Value);
        Assert.True(runtime.Write("speed", 12).Succeeded);
        await runtime.InvokeCommandAsync("start");
        Assert.Equal(1, runtime.CommandsInvoked);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task OpcUa_maps_nodes_and_methods_to_runtime()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("pump-001"), "pump", new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) }, new[] { new CommandDefinition("start") }));
        var adapter = new OpcUaAdapter(); await adapter.StartAsync(runtime, new ProtocolOptions()); adapter.Write("pump-001/speed", 8); Assert.Equal(8, adapter.Read("pump-001/speed")); await adapter.InvokeMethodAsync("pump-001/start"); Assert.Equal(1, runtime.CommandsInvoked);
    }

    [Fact]
    public async Task Transport_disconnect_does_not_stop_runtime()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("p"), "pump", new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.Read, 1) }));
        var adapter = new OpcUaAdapter(); await adapter.StartAsync(runtime, new ProtocolOptions()); adapter.ApplyTransportFault("disconnect", TimeSpan.Zero);
        Assert.Throws<IOException>(() => adapter.Read("speed")); Assert.True(adapter.IsRunning); adapter.RecoverTransportFault(); Assert.Equal(1, adapter.Read("speed"));
    }

    [Fact]
    public async Task Standard_server_accepts_client_and_exposes_runtime_nodes()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("pump-001"), "pump",
            new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) },
            new[] { new CommandDefinition("start") }));
        var adapter = new OpcUaAdapter();
        var port = GetFreePort();
        await adapter.StartAsync(runtime, new ProtocolOptions($"opc.tcp://127.0.0.1:{port}", port));
        Assert.True(adapter.IsStandardOpcUaServer);

        var trust = Path.Combine(Path.GetTempPath(), "industrial-sim-opcua", "client-trust"); Directory.CreateDirectory(trust);
        var config = new ApplicationConfiguration { ApplicationName = "IndustrialSimTest", ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration { ApplicationCertificate = new CertificateIdentifier(), TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trust }, TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trust }, RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = trust }, AutoAcceptUntrustedCertificates = true }, TransportQuotas = new TransportQuotas(), ClientConfiguration = new ClientConfiguration() };
        await config.ValidateAsync(ApplicationType.Client);
        var ep = await CoreClientUtils.SelectEndpointAsync(config, $"opc.tcp://127.0.0.1:{port}", false, null!, CancellationToken.None);
        using var session = await new DefaultSessionFactory(null!).CreateAsync(config, new ConfiguredEndpoint(null, ep, EndpointConfiguration.Create(config)), false, "test", 60000, new UserIdentity(new AnonymousIdentityToken()), null, CancellationToken.None);
        var node = new NodeId("pump-001/speed", 2);
        Assert.Equal(0, Convert.ToInt32((await session.ReadValueAsync(node)).Value));
        var write = new WriteValue { NodeId = node, AttributeId = Attributes.Value, Value = new DataValue(new Variant(7)) };
        var writeResults = await session.WriteAsync(null, new WriteValueCollection { write }, CancellationToken.None);
        Assert.True(StatusCode.IsGood(writeResults.Results[0]));
        Assert.Equal(7, runtime.Read("speed")!.Value);
        var methods = await session.CallAsync(new NodeId("pump-001", 2), new NodeId("pump-001/start", 2), CancellationToken.None);
        Assert.NotNull(methods);
        await adapter.StopAsync();
        using var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start();
        probe.Stop();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
