using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using IndustrialSim.Application.Catalogs;
using IndustrialSim.Application.Devices;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Persistence;
using IndustrialSim.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.IntegrationTests;

public sealed class DeviceCatalogRestoreTests
{
    [Fact]
    public async Task Restores_multiple_devices_and_isolates_an_invalid_row()
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-sim-restore-{Guid.NewGuid():N}.db");
        try
        {
            await using (var setup = Context(path))
            {
                await setup.Database.MigrateAsync();
                var devices = new DeviceCatalogRepository(setup);
                await devices.UpsertAsync(new DeviceCatalogItem("a", DeviceLaunchDocumentSerializer.Serialize(Launch("a", 15031)), "Stopped", 0));
                await devices.UpsertAsync(new DeviceCatalogItem("b", DeviceLaunchDocumentSerializer.Serialize(Launch("b", 15032)), "Running", 0));
                await devices.UpsertAsync(new DeviceCatalogItem("broken", "{", "Running", 0));
                await setup.CommitAsync();
            }

            await using var db = Context(path);
            await using var registry = new SimulationRegistry();
            var service = new DeviceCatalogRestoreService(new DeviceCatalogRepository(db), new TemplateCatalogRepository(db), registry);

            var results = await service.RestoreAsync(autoStartDesiredRunning: false);

            Assert.Equal(["a", "b"], registry.List().Select(item => item.DeviceId).Order().ToArray());
            Assert.All(registry.List(), item => Assert.False(item.IsRunning));
            Assert.Equal("deviceLaunchDocumentInvalid", results.Single(item => item.DeviceId == "broken").ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Explicit_auto_start_honors_running_desired_state()
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-sim-restore-{Guid.NewGuid():N}.db");
        try
        {
            await using (var setup = Context(path))
            {
                await setup.Database.MigrateAsync();
                await new DeviceCatalogRepository(setup).UpsertAsync(new DeviceCatalogItem("running", DeviceLaunchDocumentSerializer.Serialize(Launch("running", 15041)), "Running", 0));
                await setup.CommitAsync();
            }
            await using var db = Context(path);
            await using var registry = new SimulationRegistry();
            var results = await new DeviceCatalogRestoreService(new DeviceCatalogRepository(db), new TemplateCatalogRepository(db), registry).RestoreAsync(true);

            Assert.True(registry.Get("running").Host.IsRunning);
            Assert.True(results.Single().Started);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Restored_running_protocol_device_is_available_to_a_real_modbus_client()
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-sim-restore-{Guid.NewGuid():N}.db");
        var port = FreePort();
        try
        {
            await using (var setup = Context(path))
            {
                await setup.Database.MigrateAsync();
                await new DeviceCatalogRepository(setup).UpsertAsync(new DeviceCatalogItem("restored-modbus", DeviceLaunchDocumentSerializer.Serialize(Launch("restored-modbus", port)), "Running", 0));
                await setup.CommitAsync();
            }

            await using var db = Context(path);
            await using var registry = new SimulationRegistry();
            var results = await new DeviceCatalogRestoreService(new DeviceCatalogRepository(db), new TemplateCatalogRepository(db), registry).RestoreAsync(true);

            Assert.True(results.Single().Started);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await WriteModbusInt32Async(client, 1, 100, 732);
            Assert.Equal(732, await ReadModbusInt32Async(client, 2, 100));
            Assert.Equal(732, registry.Get("restored-modbus").Host.State.Snapshot()["speed"]!.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Port_conflict_during_restore_is_isolated_to_the_conflicting_device()
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-sim-restore-{Guid.NewGuid():N}.db");
        var sharedPort = FreePort();
        try
        {
            await using (var setup = Context(path))
            {
                await setup.Database.MigrateAsync();
                var devices = new DeviceCatalogRepository(setup);
                await devices.UpsertAsync(new DeviceCatalogItem("a-owner", DeviceLaunchDocumentSerializer.Serialize(Launch("a-owner", sharedPort)), "Stopped", 0));
                await devices.UpsertAsync(new DeviceCatalogItem("b-conflict", DeviceLaunchDocumentSerializer.Serialize(Launch("b-conflict", sharedPort)), "Stopped", 0));
                await devices.UpsertAsync(new DeviceCatalogItem("c-independent", DeviceLaunchDocumentSerializer.Serialize(Launch("c-independent", FreePort())), "Stopped", 0));
                await setup.CommitAsync();
            }

            await using var db = Context(path);
            await using var registry = new SimulationRegistry();
            var results = await new DeviceCatalogRestoreService(new DeviceCatalogRepository(db), new TemplateCatalogRepository(db), registry).RestoreAsync(false);

            Assert.Equal(["a-owner", "c-independent"], registry.List().Select(item => item.DeviceId).ToArray());
            Assert.Equal("portConflict", results.Single(item => item.DeviceId == "b-conflict").ErrorCode);
            Assert.True(results.Single(item => item.DeviceId == "c-independent").Restored);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static DeviceLaunchDefinition Launch(string id, int port) => new(
        new DeviceDefinition(new DeviceId(id), "custom", [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)]),
        new SimulationHostOptions(true, 1),
        Modbus: new ModbusLaunchDefinition(port, [new ValidatedModbusMapping("speed", 100, 2, "register", "int32", "readwrite", null, null)]));

    private static IndustrialSimDbContext Context(string path) => new(
        new DbContextOptionsBuilder<IndustrialSimDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);

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
        await ModbusRoundTripAsync(client, transaction, 16, address, 2, payload);
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

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
