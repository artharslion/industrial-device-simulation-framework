using System.Buffers.Binary;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Protocols.Abstractions;
using System.Net.Sockets;

namespace IndustrialSim.Protocols.Modbus;

public sealed class ModbusAdapter : IProtocolAdapter
{
    private IDeviceRuntime? _runtime; private IReadOnlyDictionary<string, ValidatedModbusMapping> _mappings = new Dictionary<string, ValidatedModbusMapping>(); private TcpListener? _listener; private CancellationTokenSource? _serverCts;
    public string Name => "modbus"; public bool IsRunning { get; private set; }
    public bool IsDisconnected { get; private set; } public TimeSpan Latency { get; private set; }
    public void ApplyTransportFault(string fault, TimeSpan duration) { IsDisconnected = fault.Equals("disconnect", StringComparison.OrdinalIgnoreCase) || fault.Equals("timeout", StringComparison.OrdinalIgnoreCase); Latency = fault.Equals("latency", StringComparison.OrdinalIgnoreCase) ? duration : TimeSpan.Zero; }
    public void RecoverTransportFault() { IsDisconnected = false; Latency = TimeSpan.Zero; }
    public async Task StartAsync(IDeviceRuntime runtime, ProtocolOptions options, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime)); IsRunning = true; if (options.Port > 0) await StartServerAsync(options.Port, cancellationToken); }
    public async Task StopAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _serverCts?.Cancel(); _listener?.Stop(); IsRunning = false; _runtime = null; if (_serverCts is not null) await Task.CompletedTask; }
    public Task StartServerAsync(int port, CancellationToken cancellationToken = default)
    {
        if (!IsRunning) throw new InvalidOperationException("Adapter must be started before opening the server.");
        _listener = new TcpListener(System.Net.IPAddress.Any, port); _listener.Start(); Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port; _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _ = AcceptLoopAsync(_serverCts.Token); return Task.CompletedTask;
    }
    public int Port { get; private set; }
    public void Configure(IEnumerable<ValidatedModbusMapping> mappings) => _mappings = mappings.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    public object? Read(string datapoint) { EnsureTransport(); Ensure(); return _runtime!.Read(datapoint)?.Value; }
    public void Write(string datapoint, object? value) { Ensure(); var result = _runtime!.Write(datapoint, value); if (!result.Succeeded) throw new InvalidOperationException(result.Error); }
    public byte[] ReadRegisters(int address, int count) { if (count < 1) throw new ArgumentException("Count must be positive."); var bytes = new byte[count * 2]; for (var i = 0; i < count; i++) { var mapping = _mappings.Values.FirstOrDefault(m => m.Kind == "register" && m.Address <= address + i && address + i < m.Address + m.Width) ?? throw new ArgumentException("Illegal address."); var raw = Encode(mapping, Read(mapping.Name)); var offset = (address + i - mapping.Address) * 2; bytes[i * 2] = raw[offset]; bytes[i * 2 + 1] = raw[offset + 1]; } return bytes; }
    private void Ensure() { if (!IsRunning || _runtime is null) throw new InvalidOperationException("Modbus adapter is not running."); }
    private void EnsureTransport() { if (IsDisconnected) throw new IOException("Modbus transport disconnected."); }
    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client; try { client = await _listener!.AcceptTcpClientAsync(token); } catch { break; }
            _ = HandleClientAsync(client, token);
        }
    }
    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using var clientScope = client; var stream = client.GetStream(); var header = new byte[7];
        while (!token.IsCancellationRequested && client.Connected)
        {
            var read = 0; while (read < header.Length) { var n = await stream.ReadAsync(header.AsMemory(read, header.Length - read), token); if (n == 0) return; read += n; }
            if (Latency > TimeSpan.Zero) await Task.Delay(Latency, token);
            var length = (header[4] << 8) | header[5]; var body = new byte[length - 1]; read = 0; while (read < body.Length) { var n = await stream.ReadAsync(body.AsMemory(read, body.Length - read), token); if (n == 0) return; read += n; }
            var function = body[0]; var address = (body[1] << 8) | body[2]; var count = (body[3] << 8) | body[4];
            try {
                EnsureTransport();
                if (function is 1 or 2) { var bits = new byte[(count + 7) / 8]; for (var i = 0; i < count; i++) { var m = _mappings.Values.FirstOrDefault(x => x.Kind == (function == 1 ? "coil" : "discrete") && x.Address == address + i) ?? throw new ArgumentException("Illegal address."); if (Convert.ToBoolean(Read(m.Name))) bits[i / 8] |= (byte)(1 << (i % 8)); } await Reply(stream, header, function, bits, token); }
                else if (function is 3 or 4) { var payload = ReadRegisters(address, count); await Reply(stream, header, function, new[] { (byte)payload.Length }.Concat(payload).ToArray(), token); }
                else if (function == 5) { var m = _mappings.Values.FirstOrDefault(x => x.Kind == "coil" && x.Address == address) ?? throw new ArgumentException("Illegal address."); Write(m.Name, body[3] == 0xFF); await Reply(stream, header, function, body[1..5], token); }
                else if (function == 6) { var value = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(3,2)); var m = _mappings.Values.FirstOrDefault(x => x.Kind == "register" && x.Address == address) ?? throw new ArgumentException("Illegal address."); Write(m.Name, ConvertValue(m.DataType, value)); await Reply(stream, header, function, body[1..5], token); }
                else if (function == 16) { var byteCount = body[5]; for (var i=0;i<count;i++) { var m = _mappings.Values.FirstOrDefault(x => x.Kind == "register" && x.Address == address+i) ?? throw new ArgumentException("Illegal address."); Write(m.Name, ConvertValue(m.DataType, BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(6+i*2,2)))); } await Reply(stream, header, function, body[1..5], token); }
                else throw new InvalidOperationException("Unsupported function.");
            } catch { var error = new byte[] { (byte)(function | 0x80), 2 }; await Reply(stream, header, error[0], new[]{error[1]}, token); }
        }
    }
    private static async Task Reply(NetworkStream stream, byte[] header, byte function, byte[] payload, CancellationToken token) { var response = new byte[8 + payload.Length]; Array.Copy(header, response, 4); response[4]=0; response[5]=(byte)(2+payload.Length); response[6]=header[6]; response[7]=function; Array.Copy(payload,0,response,8,payload.Length); await stream.WriteAsync(response, token); }
    private static byte[] Encode(ValidatedModbusMapping m, object? value) { var b = new byte[m.Width*2]; switch(m.DataType.ToLowerInvariant()) { case "float32": BinaryPrimitives.WriteSingleBigEndian(b, Convert.ToSingle(value)); break; case "int32": BinaryPrimitives.WriteInt32BigEndian(b, Convert.ToInt32(value)); break; case "uint32": BinaryPrimitives.WriteUInt32BigEndian(b, Convert.ToUInt32(value)); break; case "int16": BinaryPrimitives.WriteInt16BigEndian(b, Convert.ToInt16(value)); break; default: BinaryPrimitives.WriteUInt16BigEndian(b, Convert.ToUInt16(value)); break; } return b; }
    private static object ConvertValue(string type, ushort value) => type.ToLowerInvariant() switch { "int16" => (short)value, "int32" => (int)value, "uint32" => (uint)value, _ => value };
}
