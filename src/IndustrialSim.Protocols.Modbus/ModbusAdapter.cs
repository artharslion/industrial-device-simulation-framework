using System.Buffers.Binary;
using IndustrialSim.Configuration.Models;
using IndustrialSim.Protocols.Abstractions;

namespace IndustrialSim.Protocols.Modbus;

public sealed class ModbusAdapter : IProtocolAdapter
{
    private IDeviceRuntime? _runtime; private IReadOnlyDictionary<string, ValidatedModbusMapping> _mappings = new Dictionary<string, ValidatedModbusMapping>();
    public string Name => "modbus"; public bool IsRunning { get; private set; }
    public Task StartAsync(IDeviceRuntime runtime, ProtocolOptions options, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime)); IsRunning = true; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); IsRunning = false; _runtime = null; return Task.CompletedTask; }
    public void Configure(IEnumerable<ValidatedModbusMapping> mappings) => _mappings = mappings.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    public object? Read(string datapoint) { Ensure(); return _runtime!.Read(datapoint)?.Value; }
    public void Write(string datapoint, object? value) { Ensure(); var result = _runtime!.Write(datapoint, value); if (!result.Succeeded) throw new InvalidOperationException(result.Error); }
    public byte[] ReadRegisters(int address, int count) { var mapping = _mappings.Values.FirstOrDefault(m => m.Address == address) ?? throw new ArgumentException("No mapping at address."); var value = Read(mapping.Name); var bytes = new byte[mapping.Width * 2]; if (mapping.DataType == "float32") BinaryPrimitives.WriteSingleBigEndian(bytes, Convert.ToSingle(value)); else BinaryPrimitives.WriteUInt16BigEndian(bytes, Convert.ToUInt16(value)); return bytes[..Math.Min(bytes.Length, count * 2)]; }
    private void Ensure() { if (!IsRunning || _runtime is null) throw new InvalidOperationException("Modbus adapter is not running."); }
}
