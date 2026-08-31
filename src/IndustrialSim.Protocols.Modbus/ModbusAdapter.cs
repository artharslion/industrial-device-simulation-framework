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
    public void ApplyTransportFault(string fault, TimeSpan duration) { IsDisconnected = fault.Equals("disconnect", StringComparison.OrdinalIgnoreCase); Latency = fault.Equals("latency", StringComparison.OrdinalIgnoreCase) ? duration : TimeSpan.Zero; }
    public void RecoverTransportFault() { IsDisconnected = false; Latency = TimeSpan.Zero; }
    public Task StartAsync(IDeviceRuntime runtime, ProtocolOptions options, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime)); IsRunning = true; return Task.CompletedTask; }
    public async Task StopAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _serverCts?.Cancel(); _listener?.Stop(); IsRunning = false; _runtime = null; if (_serverCts is not null) await Task.CompletedTask; }
    public Task StartServerAsync(int port, CancellationToken cancellationToken = default)
    {
        if (!IsRunning) throw new InvalidOperationException("Adapter must be started before opening the server.");
        _listener = new TcpListener(System.Net.IPAddress.Any, port); _listener.Start(); _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _ = AcceptLoopAsync(_serverCts.Token); return Task.CompletedTask;
    }
    public void Configure(IEnumerable<ValidatedModbusMapping> mappings) => _mappings = mappings.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    public object? Read(string datapoint) { EnsureTransport(); Ensure(); return _runtime!.Read(datapoint)?.Value; }
    public void Write(string datapoint, object? value) { Ensure(); var result = _runtime!.Write(datapoint, value); if (!result.Succeeded) throw new InvalidOperationException(result.Error); }
    public byte[] ReadRegisters(int address, int count) { var mapping = _mappings.Values.FirstOrDefault(m => m.Address == address) ?? throw new ArgumentException("No mapping at address."); var value = Read(mapping.Name); var bytes = new byte[mapping.Width * 2]; if (mapping.DataType == "float32") BinaryPrimitives.WriteSingleBigEndian(bytes, Convert.ToSingle(value)); else BinaryPrimitives.WriteUInt16BigEndian(bytes, Convert.ToUInt16(value)); return bytes[..Math.Min(bytes.Length, count * 2)]; }
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
            var length = (header[4] << 8) | header[5]; var body = new byte[length - 1]; read = 0; while (read < body.Length) { var n = await stream.ReadAsync(body.AsMemory(read, body.Length - read), token); if (n == 0) return; read += n; }
            var function = body[0]; if (function != 3) continue; var address = (body[1] << 8) | body[2]; var count = (body[3] << 8) | body[4]; var payload = new byte[count * 2]; for (var i = 0; i < count; i++) { var bytes = ReadRegisters(address + i, 1); payload[i * 2] = bytes[0]; payload[i * 2 + 1] = bytes[1]; }
            var response = new byte[9 + payload.Length]; Array.Copy(header, response, 4); response[4] = 0; response[5] = (byte)(3 + payload.Length); response[6] = header[6]; response[7] = function; response[8] = (byte)payload.Length; Array.Copy(payload, 0, response, 9, payload.Length); await stream.WriteAsync(response, token);
        }
    }
}
