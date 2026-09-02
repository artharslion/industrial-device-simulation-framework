using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;
using IndustrialSim.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace IndustrialSim.IntegrationTests;

public sealed class CrossProtocolRuntimeFlowTests
{
    [Fact]
    public async Task Scenario_protocol_writes_and_faults_share_one_runtime_across_real_clients_and_http()
    {
        var opcPort = FreePort();
        var modbusPort = FreePort();
        var path = WriteConfiguration(opcPort, modbusPort);
        await using var host = await SimulationHost.LoadAsync(path, new SimulationHostOptions(Deterministic: true));
        await host.StartAsync();
        var web = await StartWebAsync(host);
        await using var webApp = web.App;
        using var http = new HttpClient { BaseAddress = new Uri(web.Address) };
        using var opc = await ConnectOpcAsync(opcPort);
        using var modbus = new TcpClient();
        await modbus.ConnectAsync(IPAddress.Loopback, modbusPort);

        host.RunScenario("""
            scenario:
              name: cross-protocol
              steps:
                - at: 0s
                  set: { device: cross, datapoint: speed, value: 100 }
            """);
        host.Tick(TimeSpan.Zero);
        Assert.Equal(100, await ReadOpcInt32Async(opc, "cross/speed"));
        Assert.Equal(100, await ReadModbusInt32Async(modbus, 1, 100));
        Assert.Equal(100, await ReadHttpInt32Async(http, "speed"));

        var opcWrite = await opc.WriteAsync(null, new WriteValueCollection
        {
            new() { NodeId = new NodeId("cross/speed", 2), AttributeId = Attributes.Value, Value = new DataValue(new Variant(200)) }
        }, CancellationToken.None);
        Assert.True(StatusCode.IsGood(opcWrite.Results[0]));
        Assert.Equal(200, await ReadModbusInt32Async(modbus, 2, 100));
        Assert.Equal(200, await ReadHttpInt32Async(http, "speed"));

        await WriteModbusInt32Async(modbus, 3, 100, 300);
        Assert.Equal(300, await ReadOpcInt32Async(opc, "cross/speed"));
        Assert.Equal(300, await ReadHttpInt32Async(http, "speed"));

        var dataFault = new FaultSpec("data-spike", FaultCategory.Data, "cross", "speed", host.Engine.CurrentTime.Elapsed, Type: "spike", Metadata: new Dictionary<string, string> { ["parameter"] = "25" });
        host.ScheduleFault(dataFault);
        host.Tick(TimeSpan.Zero);
        Assert.Equal(325, await ReadOpcInt32Async(opc, "cross/speed"));
        Assert.True(host.RecoverFault(dataFault.Id));
        Assert.Equal(300, await ReadModbusInt32Async(modbus, 4, 100));

        var deviceFault = new FaultSpec("device-overheat", FaultCategory.Device, "cross", "alarm", host.Engine.CurrentTime.Elapsed, Type: "overheat");
        host.ScheduleFault(deviceFault);
        host.Tick(TimeSpan.Zero);
        Assert.True(Convert.ToBoolean((await opc.ReadValueAsync(new NodeId("cross/alarm", 2), CancellationToken.None)).Value));
        Assert.True(host.RecoverFault(deviceFault.Id));
        Assert.False(Convert.ToBoolean((await opc.ReadValueAsync(new NodeId("cross/alarm", 2), CancellationToken.None)).Value));

        var networkFault = new FaultSpec("network-disconnect", FaultCategory.Network, "cross", "modbus", host.Engine.CurrentTime.Elapsed, Type: "disconnect");
        host.ScheduleFault(networkFault);
        host.Tick(TimeSpan.Zero);
        await Assert.ThrowsAnyAsync<IOException>(() => ModbusRoundTripAsync(modbus, 5, 3, 100, 2));
        host.Tick(TimeSpan.FromSeconds(1));
        Assert.True(host.RecoverFault(networkFault.Id));
        using var recoveredModbus = new TcpClient();
        await recoveredModbus.ConnectAsync(IPAddress.Loopback, modbusPort);
        Assert.Equal(300, await ReadModbusInt32Async(recoveredModbus, 6, 100));

        var events = await http.GetStringAsync("/api/events");
        Assert.Contains("data-spike", events, StringComparison.Ordinal);
        Assert.Contains("device-overheat", events, StringComparison.Ordinal);
        Assert.Contains("network-disconnect", events, StringComparison.Ordinal);
    }

    private static async Task<(WebApplication App, string Address)> StartWebAsync(SimulationHost host)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapIndustrialSimApi(host);
        await app.StartAsync();
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return (app, address);
    }

    private static async Task<ISession> ConnectOpcAsync(int port)
    {
        var trust = Path.Combine(Path.GetTempPath(), "industrial-sim-opcua", "integration-client-trust");
        Directory.CreateDirectory(trust);
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "IndustrialSim Integration Client",
            ApplicationUri = "urn:industrial-sim:integration-client",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier(),
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trust },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trust },
                RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = trust },
                AutoAcceptUntrustedCertificates = true
            },
            TransportQuotas = new TransportQuotas(),
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
        };
        await configuration.ValidateAsync(ApplicationType.Client);
        var endpoint = await CoreClientUtils.SelectEndpointAsync(configuration, $"opc.tcp://127.0.0.1:{port}", false, null!, CancellationToken.None);
        return await new DefaultSessionFactory(null!).CreateAsync(configuration, new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration)), false, "integration", 60000, new UserIdentity(new AnonymousIdentityToken()), null, CancellationToken.None);
    }

    private static async Task<int> ReadOpcInt32Async(ISession session, string node) => Convert.ToInt32((await session.ReadValueAsync(new NodeId(node, 2), CancellationToken.None)).Value);

    private static async Task<int> ReadHttpInt32Async(HttpClient client, string name)
    {
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/state"));
        return document.RootElement.GetProperty(name).GetInt32();
    }

    private static async Task<int> ReadModbusInt32Async(TcpClient client, ushort transaction, ushort address)
    {
        var response = await ModbusRoundTripAsync(client, transaction, 3, address, 2);
        return BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(9, 4));
    }

    private static async Task WriteModbusInt32Async(TcpClient client, ushort transaction, ushort address, int value)
    {
        var payload = new byte[5];
        payload[0] = 4;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1), value);
        var response = await ModbusRoundTripAsync(client, transaction, 16, address, 2, payload);
        Assert.Equal((byte)16, response[7]);
    }

    private static async Task<byte[]> ModbusRoundTripAsync(TcpClient client, ushort transaction, byte function, ushort address, ushort quantity, byte[]? payload = null)
    {
        var pdu = new List<byte> { function, (byte)(address >> 8), (byte)address, (byte)(quantity >> 8), (byte)quantity };
        if (payload is not null) pdu.AddRange(payload);
        var request = new byte[7 + pdu.Count];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transaction);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), (ushort)(pdu.Count + 1));
        request[6] = 1;
        pdu.CopyTo(request, 7);
        await client.GetStream().WriteAsync(request);
        var header = new byte[7];
        await ReadExactlyAsync(client.GetStream(), header);
        var body = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2)) - 1];
        await ReadExactlyAsync(client.GetStream(), body);
        return header.Concat(body).ToArray();
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(offset));
            if (count == 0) throw new EndOfStreamException();
            offset += count;
        }
    }

    private static string WriteConfiguration(int opcPort, int modbusPort)
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-sim-cross-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, $$"""
            device:
              id: cross
              type: pump
              datapoints:
                speed: { type: int32, initial: 0, access: readwrite }
                alarm: { type: boolean, initial: false, access: read }
              commands:
                start:
            protocols:
              opcua:
                enabled: true
                endpoint: "opc.tcp://127.0.0.1:{{opcPort}}"
              modbus:
                enabled: true
                port: {{modbusPort}}
                mappings:
                  speed: { holdingRegister: 100, type: int32, access: readwrite }
                  alarm: { coil: 10, type: boolean, access: read }
            """);
        return path;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
