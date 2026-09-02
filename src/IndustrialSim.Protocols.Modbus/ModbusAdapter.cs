using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Protocols.Abstractions;

namespace IndustrialSim.Protocols.Modbus;

public sealed class ModbusAdapter : IProtocolAdapter
{
    private IDeviceRuntime? _runtime;
    private IReadOnlyDictionary<string, ValidatedModbusMapping> _mappings = new Dictionary<string, ValidatedModbusMapping>(StringComparer.OrdinalIgnoreCase);
    private TcpListener? _listener;
    private CancellationTokenSource? _serverCts;
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private readonly object _faultGate = new();
    private TransportFaultMode _faultMode;
    private TimeSpan _faultDuration;

    public string Name => "modbus";
    public bool IsRunning { get; private set; }
    public bool IsDisconnected { get; private set; }
    public TimeSpan Latency { get; private set; }
    public int Port { get; private set; }

    public void ApplyTransportFault(string fault, TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        var mode = fault.ToLowerInvariant() switch
        {
            "disconnect" => TransportFaultMode.Disconnect,
            "timeout" => TransportFaultMode.Timeout,
            "latency" => TransportFaultMode.Latency,
            _ => throw new ArgumentException($"Unsupported Modbus transport fault '{fault}'.", nameof(fault))
        };
        lock (_faultGate)
        {
            _faultMode = mode;
            _faultDuration = duration;
            IsDisconnected = mode is TransportFaultMode.Disconnect or TransportFaultMode.Timeout;
            Latency = mode == TransportFaultMode.Latency ? duration : TimeSpan.Zero;
        }
        if (mode == TransportFaultMode.Disconnect) CloseClients();
    }

    public void RecoverTransportFault()
    {
        lock (_faultGate)
        {
            _faultMode = TransportFaultMode.None;
            _faultDuration = TimeSpan.Zero;
            IsDisconnected = false;
            Latency = TimeSpan.Zero;
        }
    }

    public async Task StartAsync(IDeviceRuntime runtime, ProtocolOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        IsRunning = true;
        if (options.Port > 0) await StartServerAsync(options.Port, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _serverCts?.Cancel();
        _listener?.Stop();
        CloseClients();
        _listener = null;
        _serverCts?.Dispose();
        _serverCts = null;
        IsRunning = false;
        _runtime = null;
        Port = 0;
        await Task.CompletedTask;
    }

    public Task StartServerAsync(int port, CancellationToken cancellationToken = default)
    {
        EnsureRunning();
        cancellationToken.ThrowIfCancellationRequested();
        if (_listener is not null) throw new InvalidOperationException("Modbus server is already started.");
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = AcceptLoopAsync(_serverCts.Token);
        return Task.CompletedTask;
    }

    public void Configure(IEnumerable<ValidatedModbusMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        _mappings = mappings.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    }

    public object? Read(string datapoint)
    {
        EnsureTransport();
        EnsureRunning();
        return _runtime!.Read(datapoint)?.Value;
    }

    public void Write(string datapoint, object? value)
    {
        EnsureRunning();
        if (_mappings.TryGetValue(datapoint, out var mapping) && !CanWrite(mapping))
            throw new InvalidOperationException($"Mapping '{datapoint}' is not writable.");
        var result = _runtime!.Write(datapoint, value);
        if (!result.Succeeded) throw new InvalidOperationException(result.Error);
    }

    public byte[] ReadRegisters(int address, int count, string kind = "register")
    {
        EnsureRunning();
        if (count < 1 || address < 0 || address + count > 65536) throw new ArgumentException("Invalid Modbus register range.");
        var bytes = new byte[count * 2];
        var encodedMappings = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (var offset = 0; offset < count; offset++)
        {
            var mapping = FindMapping(kind, address + offset);
            if (!CanRead(mapping)) throw new InvalidOperationException($"Mapping '{mapping.Name}' is not readable.");
            if (!encodedMappings.TryGetValue(mapping.Name, out var encoded))
            {
                encoded = Encode(mapping, _runtime!.Read(mapping.Name)?.Value);
                encodedMappings.Add(mapping.Name, encoded);
            }
            var sourceOffset = (address + offset - mapping.Address) * 2;
            bytes[offset * 2] = encoded[sourceOffset];
            bytes[offset * 2 + 1] = encoded[sourceOffset + 1];
        }
        return bytes;
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(token); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            if (CurrentFault().Mode == TransportFaultMode.Disconnect) { client.Dispose(); continue; }
            _ = HandleClientAsync(client, token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        _clients.TryAdd(client, 0);
        using (client)
        {
            var stream = client.GetStream();
            var header = new byte[7];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await ReadExactlyAsync(stream, header, token);
                    var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
                    if (length < 2 || length > 254) throw new ModbusRequestException(3);
                    var pdu = new byte[length - 1];
                    await ReadExactlyAsync(stream, pdu, token);
                    var fault = CurrentFault();
                    if (fault.Mode == TransportFaultMode.Disconnect) return;
                    if (fault.Mode == TransportFaultMode.Timeout)
                    {
                        if (fault.Duration > TimeSpan.Zero) await Task.Delay(fault.Duration, token);
                        continue;
                    }
                    if (fault.Mode == TransportFaultMode.Latency && fault.Duration > TimeSpan.Zero) await Task.Delay(fault.Duration, token);
                    await WriteResponseAsync(stream, header, ProcessRequest(pdu), token);
                }
            }
            catch (EndOfStreamException) { }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally { _clients.TryRemove(client, out _); }
        }
    }

    private byte[] ProcessRequest(byte[] pdu)
    {
        if (pdu.Length == 0) return ExceptionResponse(0, 3);
        var function = pdu[0];
        try
        {
            EnsureTransport();
            return function switch
            {
                1 or 2 => ReadBits(function, pdu),
                3 or 4 => ReadRegistersWire(function, pdu),
                5 => WriteSingleCoil(pdu),
                6 => WriteSingleRegister(pdu),
                16 => WriteMultipleRegisters(pdu),
                _ => throw new ModbusRequestException(1)
            };
        }
        catch (ModbusRequestException exception) { return ExceptionResponse(function, exception.Code); }
        catch (InvalidOperationException) { return ExceptionResponse(function, 2); }
        catch (ArgumentException) { return ExceptionResponse(function, 2); }
    }

    private byte[] ReadBits(byte function, byte[] pdu)
    {
        RequireLength(pdu, 5);
        var address = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        if (quantity is < 1 or > 2000) throw new ModbusRequestException(3);
        var result = new byte[(quantity + 7) / 8];
        var kind = function == 1 ? "coil" : "discrete";
        for (var i = 0; i < quantity; i++)
        {
            var mapping = FindMapping(kind, address + i);
            if (!CanRead(mapping)) throw new ModbusRequestException(2);
            if (Convert.ToBoolean(_runtime!.Read(mapping.Name)?.Value)) result[i / 8] |= (byte)(1 << (i % 8));
        }
        return new[] { function, (byte)result.Length }.Concat(result).ToArray();
    }

    private byte[] ReadRegistersWire(byte function, byte[] pdu)
    {
        RequireLength(pdu, 5);
        var address = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        if (quantity is < 1 or > 125) throw new ModbusRequestException(3);
        var payload = ReadRegisters(address, quantity, function == 3 ? "register" : "input");
        return new[] { function, (byte)payload.Length }.Concat(payload).ToArray();
    }

    private byte[] WriteSingleCoil(byte[] pdu)
    {
        RequireLength(pdu, 5);
        var address = ReadUInt16(pdu, 1);
        var value = ReadUInt16(pdu, 3);
        if (value is not (0x0000 or 0xFF00)) throw new ModbusRequestException(3);
        var mapping = FindMapping("coil", address);
        if (!CanWrite(mapping)) throw new ModbusRequestException(2);
        Write(mapping.Name, value == 0xFF00);
        return pdu;
    }

    private byte[] WriteSingleRegister(byte[] pdu)
    {
        RequireLength(pdu, 5);
        var mapping = FindMapping("register", ReadUInt16(pdu, 1));
        if (mapping.Width != 1 || !CanWrite(mapping)) throw new ModbusRequestException(2);
        Write(mapping.Name, Decode(mapping, pdu.AsSpan(3, 2).ToArray()));
        return pdu;
    }

    private byte[] WriteMultipleRegisters(byte[] pdu)
    {
        RequireLength(pdu, 6);
        var address = ReadUInt16(pdu, 1);
        var quantity = ReadUInt16(pdu, 3);
        var byteCount = pdu[5];
        if (quantity is < 1 or > 123 || byteCount != quantity * 2 || pdu.Length != 6 + byteCount) throw new ModbusRequestException(3);
        var end = address + quantity;
        var cursor = (int)address;
        var writes = new List<(ValidatedModbusMapping Mapping, object Value)>();
        while (cursor < end)
        {
            var mapping = FindMapping("register", cursor);
            if (mapping.Address != cursor || !CanWrite(mapping) || mapping.Address + mapping.Width > end) throw new ModbusRequestException(2);
            var offset = (mapping.Address - address) * 2;
            writes.Add((mapping, Decode(mapping, pdu.AsSpan(6 + offset, mapping.Width * 2).ToArray())));
            cursor = mapping.Address + mapping.Width;
        }
        var result = _runtime!.WriteBatch(writes.Select(write => (write.Mapping.Name, (object?)write.Value)));
        if (!result.Succeeded) throw new ModbusRequestException(2);
        return new[] { (byte)16, pdu[1], pdu[2], pdu[3], pdu[4] };
    }

    private ValidatedModbusMapping FindMapping(string kind, int address) => _mappings.Values.FirstOrDefault(m => string.Equals(m.Kind, kind, StringComparison.OrdinalIgnoreCase) && m.Address <= address && address < m.Address + m.Width) ?? throw new ModbusRequestException(2);

    private static byte[] Encode(ValidatedModbusMapping mapping, object? value)
    {
        var canonical = new byte[mapping.Width * 2];
        switch (mapping.DataType.ToLowerInvariant())
        {
            case "int8": canonical[1] = unchecked((byte)Convert.ToSByte(value)); break;
            case "uint8": canonical[1] = Convert.ToByte(value); break;
            case "int16": BinaryPrimitives.WriteInt16BigEndian(canonical, Convert.ToInt16(value)); break;
            case "uint16": BinaryPrimitives.WriteUInt16BigEndian(canonical, Convert.ToUInt16(value)); break;
            case "int32": BinaryPrimitives.WriteInt32BigEndian(canonical, Convert.ToInt32(value)); break;
            case "uint32": BinaryPrimitives.WriteUInt32BigEndian(canonical, Convert.ToUInt32(value)); break;
            case "int64": BinaryPrimitives.WriteInt64BigEndian(canonical, Convert.ToInt64(value)); break;
            case "uint64": BinaryPrimitives.WriteUInt64BigEndian(canonical, Convert.ToUInt64(value)); break;
            case "float":
            case "float32": BinaryPrimitives.WriteSingleBigEndian(canonical, Convert.ToSingle(value)); break;
            case "double": BinaryPrimitives.WriteDoubleBigEndian(canonical, Convert.ToDouble(value)); break;
            default: throw new ArgumentException($"Unsupported Modbus data type '{mapping.DataType}'.");
        }
        ApplyOrder(canonical, mapping.ByteOrder, mapping.WordOrder);
        return canonical;
    }

    private static object Decode(ValidatedModbusMapping mapping, byte[] wire)
    {
        var canonical = wire.ToArray();
        ApplyOrder(canonical, mapping.ByteOrder, mapping.WordOrder, true);
        return mapping.DataType.ToLowerInvariant() switch
        {
            "int8" => (object)unchecked((sbyte)canonical[^1]),
            "uint8" => canonical[^1],
            "int16" => BinaryPrimitives.ReadInt16BigEndian(canonical),
            "uint16" => BinaryPrimitives.ReadUInt16BigEndian(canonical),
            "int32" => BinaryPrimitives.ReadInt32BigEndian(canonical),
            "uint32" => BinaryPrimitives.ReadUInt32BigEndian(canonical),
            "int64" => BinaryPrimitives.ReadInt64BigEndian(canonical),
            "uint64" => BinaryPrimitives.ReadUInt64BigEndian(canonical),
            "float" or "float32" => BinaryPrimitives.ReadSingleBigEndian(canonical),
            "double" => BinaryPrimitives.ReadDoubleBigEndian(canonical),
            _ => throw new ArgumentException($"Unsupported Modbus data type '{mapping.DataType}'.")
        };
    }

    private static void ApplyOrder(byte[] bytes, string? byteOrder, string? wordOrder, bool reverse = false)
    {
        var littleBytes = string.Equals(byteOrder, "little", StringComparison.OrdinalIgnoreCase);
        var littleWords = string.Equals(wordOrder, "little", StringComparison.OrdinalIgnoreCase);
        if (reverse)
        {
            if (littleWords) ReverseWords(bytes);
            if (littleBytes) SwapBytesInWords(bytes);
        }
        else
        {
            if (littleBytes) SwapBytesInWords(bytes);
            if (littleWords) ReverseWords(bytes);
        }
    }

    private static void SwapBytesInWords(byte[] bytes) { for (var i = 0; i < bytes.Length; i += 2) (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]); }
    private static void ReverseWords(byte[] bytes) { for (var left = 0; left < bytes.Length / 2; left += 2) { var right = bytes.Length - 2 - left; (bytes[left], bytes[right]) = (bytes[right], bytes[left]); (bytes[left + 1], bytes[right + 1]) = (bytes[right + 1], bytes[left + 1]); } }

    private static async Task WriteResponseAsync(NetworkStream stream, byte[] requestHeader, byte[] pdu, CancellationToken token)
    {
        var response = new byte[7 + pdu.Length];
        requestHeader.AsSpan(0, 4).CopyTo(response);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), (ushort)(pdu.Length + 1));
        response[6] = requestHeader[6];
        pdu.CopyTo(response, 7);
        await stream.WriteAsync(response, token);
    }

    private static byte[] ExceptionResponse(byte function, byte code) => new[] { (byte)(function | 0x80), code };
    private static ushort ReadUInt16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
    private static void RequireLength(byte[] pdu, int length) { if (pdu.Length < length) throw new ModbusRequestException(3); }
    private void EnsureRunning() { if (!IsRunning || _runtime is null) throw new InvalidOperationException("Modbus adapter is not running."); }
    private void EnsureTransport() { if (CurrentFault().Mode is TransportFaultMode.Disconnect or TransportFaultMode.Timeout) throw new ModbusRequestException(6); }
    private static bool CanRead(ValidatedModbusMapping mapping) => string.IsNullOrWhiteSpace(mapping.Access) || !mapping.Access.Equals("write", StringComparison.OrdinalIgnoreCase);
    private static bool CanWrite(ValidatedModbusMapping mapping) => mapping.Kind is "coil" or "register" && (string.IsNullOrWhiteSpace(mapping.Access) || mapping.Access.Equals("write", StringComparison.OrdinalIgnoreCase) || mapping.Access.Equals("readwrite", StringComparison.OrdinalIgnoreCase));

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(offset), token);
            if (count == 0) throw new EndOfStreamException();
            offset += count;
        }
    }

    private sealed class ModbusRequestException(byte code) : Exception { public byte Code { get; } = code; }
    private enum TransportFaultMode { None, Disconnect, Timeout, Latency }

    private (TransportFaultMode Mode, TimeSpan Duration) CurrentFault()
    {
        lock (_faultGate) return (_faultMode, _faultDuration);
    }

    private void CloseClients()
    {
        foreach (var client in _clients.Keys)
        {
            try { client.Close(); }
            catch (SocketException) { }
        }
    }
}
