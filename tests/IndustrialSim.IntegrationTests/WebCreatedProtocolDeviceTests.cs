using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Web;
using IndustrialSim.Web.Api.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace IndustrialSim.IntegrationTests;

public sealed class WebCreatedProtocolDeviceTests
{
    [Fact]
    public async Task Api_created_opcua_device_is_browsable_readable_writable_and_callable_by_a_real_client()
    {
        await using var fixture = await Fixture.StartAsync();
        var port = FreePort();
        var created = await fixture.Client.PostAsJsonAsync("/api/v1/devices", PumpRequest("api-opc", new
        {
            opcua = new { enabled = true, port }
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/api-opc/start", null)).StatusCode);

        using var session = await ConnectOpcAsync(port);
        var references = await session.FetchReferencesAsync(new NodeId("api-opc", 2), CancellationToken.None);
        Assert.Contains(references, reference => reference.BrowseName.Name == "speed");
        var speed = new NodeId("api-opc/speed", 2);
        Assert.Equal(0, Convert.ToInt32((await session.ReadValueAsync(speed, CancellationToken.None)).Value));
        var write = await session.WriteAsync(null, new WriteValueCollection
        {
            new() { NodeId = speed, AttributeId = Attributes.Value, Value = new DataValue(new Variant(250)) }
        }, CancellationToken.None);
        Assert.True(StatusCode.IsGood(write.Results[0]));
        await session.CallAsync(new NodeId("api-opc", 2), new NodeId("api-opc/start", 2), CancellationToken.None);
        var state = await fixture.Client.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/v1/devices/api-opc/state");
        Assert.Equal(250, state!["speed"].GetInt32());
        Assert.True(state["running"].GetBoolean());
    }

    [Fact]
    public async Task Api_created_modbus_device_uses_explicit_mapping_over_a_real_tcp_client()
    {
        await using var fixture = await Fixture.StartAsync();
        var port = FreePort();
        var created = await fixture.Client.PostAsJsonAsync("/api/v1/devices", new
        {
            id = "api-modbus", type = "custom", deterministic = true, seed = 3,
            dataPoints = new[] { new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 } },
            behavior = new { profile = "none", parameters = new Dictionary<string, double>() },
            protocols = new
            {
                modbus = new
                {
                    enabled = true, port,
                    mappings = new[] { new { dataPoint = "speed", kind = "holding", address = 100, dataType = "int32", access = "readwrite" } }
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await fixture.Client.PostAsync("/api/v1/devices/api-modbus/start", null);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await WriteModbusInt32Async(client, 1, 100, 321);
        Assert.Equal(321, await ReadModbusInt32Async(client, 2, 100));
        var state = await fixture.Client.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/v1/devices/api-modbus/state");
        Assert.Equal(321, state!["speed"].GetInt32());
    }

    [Fact]
    public async Task Template_mapping_profile_is_applied_to_a_real_modbus_instance()
    {
        await using var fixture = await Fixture.StartAsync();
        var package = new
        {
            template = new
            {
                id = "mapped-device", version = "1.0.0", displayName = "Mapped device", deviceType = "custom", description = "test", tags = Array.Empty<string>(),
                dataPoints = new[] { new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 } },
                commands = Array.Empty<string>(), events = Array.Empty<string>(), behaviorJson = "{\"profile\":\"none\"}"
            },
            mappings = new[]
            {
                new { templateId = "mapped-device", templateVersion = "1.0.0", protocol = "modbus", name = "holding", entries = new[] { new { dataPoint = "speed", address = "40101", dataType = "int32", byteOrder = "BigEndian", wordOrder = "HighLow" } } }
            }
        };
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/v1/templates", package)).StatusCode);
        var port = FreePort();
        var instantiated = await fixture.Client.PostAsJsonAsync("/api/v1/templates/mapped-device/1.0.0/instantiate", new
        {
            deviceId = "from-template", deterministic = true, seed = 4,
            protocols = new { modbus = new { enabled = true, port, mappingProfile = "holding" } }
        });
        Assert.Equal(HttpStatusCode.Created, instantiated.StatusCode);
        await fixture.Client.PostAsync("/api/v1/devices/from-template/start", null);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await WriteModbusInt32Async(client, 3, 100, 654);
        Assert.Equal(654, await ReadModbusInt32Async(client, 4, 100));
    }

    private static object PumpRequest(string id, object protocols) => new
    {
        id, type = "pump", deterministic = true, seed = 2,
        dataPoints = new object[]
        {
            new { name = "speed", dataType = "Int32", access = "ReadWrite", initial = 0 },
            new { name = "temperature", dataType = "Double", access = "Read", initial = 25d },
            new { name = "pressure", dataType = "Double", access = "Read", initial = 0d },
            new { name = "running", dataType = "Boolean", access = "Read", initial = false },
            new { name = "alarm", dataType = "Boolean", access = "Read", initial = false }
        },
        commands = new[] { "start", "stop" },
        behavior = new { profile = "pump", parameters = new Dictionary<string, double>() },
        protocols
    };

    private static async Task<ISession> ConnectOpcAsync(int port)
    {
        var trust = Path.Combine(Path.GetTempPath(), "industrial-sim-opcua", "web-created-client-trust");
        Directory.CreateDirectory(trust);
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "IndustrialSim Web Created Client",
            ApplicationUri = "urn:industrial-sim:web-created-client",
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

    private static async Task<int> ReadModbusInt32Async(TcpClient client, ushort transaction, ushort address)
    {
        var response = await ModbusRoundTripAsync(client, transaction, 3, address, 2);
        return BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(9, 4));
    }

    private static async Task WriteModbusInt32Async(TcpClient client, ushort transaction, ushort address, int value)
    {
        var payload = new byte[5]; payload[0] = 4; BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1), value);
        var response = await ModbusRoundTripAsync(client, transaction, 16, address, 2, payload);
        Assert.Equal((byte)16, response[7]);
    }

    private static async Task<byte[]> ModbusRoundTripAsync(TcpClient client, ushort transaction, byte function, ushort address, ushort quantity, byte[]? payload = null)
    {
        var pdu = new List<byte> { function, (byte)(address >> 8), (byte)address, (byte)(quantity >> 8), (byte)quantity };
        if (payload is not null) pdu.AddRange(payload);
        var request = new byte[7 + pdu.Count]; BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transaction); BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), (ushort)(pdu.Count + 1)); request[6] = 1; pdu.CopyTo(request, 7);
        await client.GetStream().WriteAsync(request);
        var header = new byte[7]; await ReadExactlyAsync(client.GetStream(), header);
        var body = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2)) - 1]; await ReadExactlyAsync(client.GetStream(), body);
        return header.Concat(body).ToArray();
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length) { var count = await stream.ReadAsync(buffer.AsMemory(offset)); if (count == 0) throw new EndOfStreamException(); offset += count; }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class Fixture(WebApplication app, HttpClient client, string databasePath, SimulationRegistry registry) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<Fixture> StartAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"industrial-sim-web-protocol-{Guid.NewGuid():N}.db");
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var registry = new SimulationRegistry();
            await registry.AddAsync(SimulationHost.Create(new DeviceDefinition(new DeviceId("boot"), "custom", [new DataPointDefinition("value", DataType.Int32, DataPointAccess.ReadWrite, 0)]), new SimulationHostOptions(true, 1)));
            builder.Services.AddIndustrialSimControlPlane(registry, $"Data Source={path};Pooling=False");
            var app = builder.Build(); app.UseIndustrialSimProblemDetails(); app.UseAuthentication(); app.UseAuthorization(); app.MapIndustrialSimV1Api(); await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new Fixture(app, new HttpClient { BaseAddress = new Uri(address) }, path, registry);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); await app.StopAsync(); await app.DisposeAsync(); await registry.DisposeAsync(); File.Delete(databasePath);
        }
    }
}
