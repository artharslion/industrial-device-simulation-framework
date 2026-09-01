using System.Buffers.Binary;
using System.Net.Sockets;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Protocols.Modbus;

namespace IndustrialSim.Protocols.Modbus.Tests;

public sealed class ModbusWireBehaviorTests
{
    [Fact]
    public async Task Real_tcp_client_supports_reads_writes_and_multi_register_values()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("wire"), "pump", new[]
        {
            new DataPointDefinition("coil", DataType.Boolean, DataPointAccess.ReadWrite, false),
            new DataPointDefinition("discrete", DataType.Boolean, DataPointAccess.Read, true),
            new DataPointDefinition("input", DataType.UInt16, DataPointAccess.Read, (ushort)0x1234),
            new DataPointDefinition("holding", DataType.UInt16, DataPointAccess.ReadWrite, (ushort)0x5678),
            new DataPointDefinition("wide", DataType.UInt32, DataPointAccess.ReadWrite, 0x11223344u)
        }));
        var adapter = new ModbusAdapter();
        adapter.Configure(new[]
        {
            new ValidatedModbusMapping("coil", 10, 1, "coil", "boolean", "readwrite", null, null),
            new ValidatedModbusMapping("discrete", 20, 1, "discrete", "boolean", "read", null, null),
            new ValidatedModbusMapping("input", 30, 1, "input", "uint16", "read", null, null),
            new ValidatedModbusMapping("holding", 40, 1, "register", "uint16", "readwrite", null, null),
            new ValidatedModbusMapping("wide", 50, 2, "register", "uint32", "readwrite", null, null)
        });
        await adapter.StartAsync(runtime, new ProtocolOptions());
        await adapter.StartServerAsync(0);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", adapter.Port);
            var readCoil = await RoundTripAsync(client, 1, 1, 10, 1);
            Assert.Equal(new byte[] { 1, 1, 0 }, readCoil[7..]);
            var readDiscrete = await RoundTripAsync(client, 2, 2, 20, 1);
            Assert.Equal(new byte[] { 2, 1, 1 }, readDiscrete[7..]);
            var readInput = await RoundTripAsync(client, 3, 4, 30, 1);
            Assert.Equal(new byte[] { 4, 2, 0x12, 0x34 }, readInput[7..]);
            var readHolding = await RoundTripAsync(client, 4, 3, 40, 1);
            Assert.Equal(new byte[] { 3, 2, 0x56, 0x78 }, readHolding[7..]);
            var readWide = await RoundTripAsync(client, 5, 3, 50, 2);
            Assert.Equal(new byte[] { 3, 4, 0x11, 0x22, 0x33, 0x44 }, readWide[7..]);

            var writeCoil = await RoundTripAsync(client, 6, 5, 10, 0xFF00);
            Assert.Equal(new byte[] { 5, 0, 10, 0xFF, 0 }, writeCoil[7..]);
            Assert.True(runtime.Read("coil")!.Value is true);
            var writeHolding = await RoundTripAsync(client, 7, 6, 40, 0xCAFE);
            Assert.Equal(new byte[] { 6, 0, 40, 0xCA, 0xFE }, writeHolding[7..]);
            Assert.Equal((ushort)0xCAFE, runtime.Read("holding")!.Value);
            var writeWide = await RoundTripAsync(client, 8, 16, 50, 2, new byte[] { 4, 0xA1, 0xB2, 0xC3, 0xD4 });
            Assert.Equal(new byte[] { 16, 0, 50, 0, 2 }, writeWide[7..]);
            Assert.Equal(0xA1B2C3D4u, runtime.Read("wide")!.Value);
        }
        finally
        {
            await adapter.StopAsync();
        }
    }

    [Fact]
    public async Task Wire_enforces_access_and_returns_modbus_exceptions_for_invalid_requests()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("wire"), "pump", new[]
        {
            new DataPointDefinition("readOnly", DataType.UInt16, DataPointAccess.Read, (ushort)1),
            new DataPointDefinition("writeOnly", DataType.UInt16, DataPointAccess.Write, (ushort)2)
        }));
        var adapter = new ModbusAdapter();
        adapter.Configure(new[]
        {
            new ValidatedModbusMapping("readOnly", 60, 1, "register", "uint16", "read", null, null),
            new ValidatedModbusMapping("writeOnly", 61, 1, "register", "uint16", "write", null, null)
        });
        await adapter.StartAsync(runtime, new ProtocolOptions());
        await adapter.StartServerAsync(0);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", adapter.Port);
            var readWriteOnly = await RoundTripAsync(client, 1, 3, 61, 1);
            Assert.Equal((byte)0x83, readWriteOnly[7]);
            var writeReadOnly = await RoundTripAsync(client, 2, 6, 60, 2);
            Assert.Equal((byte)0x86, writeReadOnly[7]);
            var invalid = await RoundTripAsync(client, 3, 3, 999, 1);
            Assert.Equal((byte)0x83, invalid[7]);
        }
        finally
        {
            await adapter.StopAsync();
        }
    }

    [Fact]
    public async Task Rejected_multi_register_write_does_not_partially_mutate_state()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("wire"), "pump", new[]
        {
            new DataPointDefinition("first", DataType.UInt16, DataPointAccess.ReadWrite, (ushort)1),
            new DataPointDefinition("writeOnlyCoil", DataType.Boolean, DataPointAccess.ReadWrite, false)
        }));
        var adapter = new ModbusAdapter();
        adapter.Configure(new[]
        {
            new ValidatedModbusMapping("first", 80, 1, "register", "uint16", "readwrite", null, null),
            new ValidatedModbusMapping("writeOnlyCoil", 90, 1, "coil", "boolean", "write", null, null)
        });
        await adapter.StartAsync(runtime, new ProtocolOptions());
        await adapter.StartServerAsync(0);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", adapter.Port);
            var partial = await RoundTripAsync(client, 1, 16, 80, 2, new byte[] { 4, 0, 2, 0, 3 });
            Assert.Equal(new byte[] { 0x90, 2 }, partial[7..]);
            Assert.Equal((ushort)1, runtime.Read("first")!.Value);
            var readWriteOnlyCoil = await RoundTripAsync(client, 2, 1, 90, 1);
            Assert.Equal(new byte[] { 0x81, 2 }, readWriteOnlyCoil[7..]);
        }
        finally
        {
            await adapter.StopAsync();
        }
    }

    [Fact]
    public async Task Wire_honors_configured_byte_and_word_order()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("wire"), "pump", new[]
        {
            new DataPointDefinition("value", DataType.UInt32, DataPointAccess.ReadWrite, 0x11223344u)
        }));
        var adapter = new ModbusAdapter();
        adapter.Configure(new[] { new ValidatedModbusMapping("value", 70, 2, "register", "uint32", "readwrite", "little", "little") });
        await adapter.StartAsync(runtime, new ProtocolOptions());
        await adapter.StartServerAsync(0);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", adapter.Port);
            var response = await RoundTripAsync(client, 1, 3, 70, 2);
            Assert.Equal(new byte[] { 3, 4, 0x44, 0x33, 0x22, 0x11 }, response[7..]);
            await RoundTripAsync(client, 2, 16, 70, 2, new byte[] { 4, 0xDD, 0xCC, 0xBB, 0xAA });
            Assert.Equal(0xAABBCCDDu, runtime.Read("value")!.Value);
        }
        finally
        {
            await adapter.StopAsync();
        }
    }

    [Fact]
    public async Task Real_tcp_client_encodes_and_decodes_all_v01_numeric_widths()
    {
        var points = new[]
        {
            new DataPointDefinition("i8", DataType.Int8, DataPointAccess.ReadWrite, (sbyte)-2),
            new DataPointDefinition("u8", DataType.UInt8, DataPointAccess.ReadWrite, (byte)0xFE),
            new DataPointDefinition("i16", DataType.Int16, DataPointAccess.ReadWrite, (short)-2),
            new DataPointDefinition("u16", DataType.UInt16, DataPointAccess.ReadWrite, (ushort)0xFEDC),
            new DataPointDefinition("i32", DataType.Int32, DataPointAccess.ReadWrite, -2),
            new DataPointDefinition("u32", DataType.UInt32, DataPointAccess.ReadWrite, 0xFEDCBA98u),
            new DataPointDefinition("i64", DataType.Int64, DataPointAccess.ReadWrite, -2L),
            new DataPointDefinition("u64", DataType.UInt64, DataPointAccess.ReadWrite, 0xFEDCBA9876543210UL),
            new DataPointDefinition("f32", DataType.Float, DataPointAccess.ReadWrite, 1.5f),
            new DataPointDefinition("f64", DataType.Double, DataPointAccess.ReadWrite, 1.5d)
        };
        var mappings = new[]
        {
            new ValidatedModbusMapping("i8", 100, 1, "register", "int8", "readwrite", null, null),
            new ValidatedModbusMapping("u8", 101, 1, "register", "uint8", "readwrite", null, null),
            new ValidatedModbusMapping("i16", 102, 1, "register", "int16", "readwrite", null, null),
            new ValidatedModbusMapping("u16", 103, 1, "register", "uint16", "readwrite", null, null),
            new ValidatedModbusMapping("i32", 104, 2, "register", "int32", "readwrite", null, null),
            new ValidatedModbusMapping("u32", 106, 2, "register", "uint32", "readwrite", null, null),
            new ValidatedModbusMapping("i64", 108, 4, "register", "int64", "readwrite", null, null),
            new ValidatedModbusMapping("u64", 112, 4, "register", "uint64", "readwrite", null, null),
            new ValidatedModbusMapping("f32", 116, 2, "register", "float32", "readwrite", null, null),
            new ValidatedModbusMapping("f64", 118, 4, "register", "double", "readwrite", null, null)
        };
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("numeric"), "sensor", points));
        var adapter = new ModbusAdapter();
        adapter.Configure(mappings);
        await adapter.StartAsync(runtime, new ProtocolOptions());
        await adapter.StartServerAsync(0);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", adapter.Port);
            var read = await RoundTripAsync(client, 1, 3, 100, 22);
            Assert.Equal(Convert.FromHexString("032C00FE00FEFFFEFEDCFFFFFFFEFEDCBA98FFFFFFFFFFFFFFFEFEDCBA98765432103FC000003FF8000000000000"), read[7..]);

            await RoundTripAsync(client, 2, 16, 100, 22, Convert.FromHexString("2C007F00807FFF80010000000280000001000000000000000280000000000000013F0000004004000000000000"));
            Assert.Equal((sbyte)127, runtime.Read("i8")!.Value);
            Assert.Equal((byte)128, runtime.Read("u8")!.Value);
            Assert.Equal(short.MaxValue, runtime.Read("i16")!.Value);
            Assert.Equal((ushort)0x8001, runtime.Read("u16")!.Value);
            Assert.Equal(2, runtime.Read("i32")!.Value);
            Assert.Equal(0x80000001u, runtime.Read("u32")!.Value);
            Assert.Equal(2L, runtime.Read("i64")!.Value);
            Assert.Equal(0x8000000000000001UL, runtime.Read("u64")!.Value);
            Assert.Equal(0.5f, runtime.Read("f32")!.Value);
            Assert.Equal(2.5d, runtime.Read("f64")!.Value);
        }
        finally
        {
            await adapter.StopAsync();
        }
    }

    private static async Task<byte[]> RoundTripAsync(TcpClient client, ushort transaction, byte function, ushort address, ushort quantity, byte[]? writePayload = null)
    {
        var pdu = new List<byte> { function, (byte)(address >> 8), (byte)address, (byte)(quantity >> 8), (byte)quantity };
        if (writePayload is not null) pdu.AddRange(writePayload);
        var request = new byte[7 + pdu.Count];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transaction);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), (ushort)(pdu.Count + 1));
        request[6] = 1;
        pdu.CopyTo(request, 7);
        await client.GetStream().WriteAsync(request);
        var header = new byte[7];
        await ReadExactlyAsync(client.GetStream(), header);
        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        var body = new byte[length - 1];
        await ReadExactlyAsync(client.GetStream(), body);
        return header.Concat(body).ToArray();
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
