using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Protocols.Abstractions;

public sealed record ProtocolOptions(string? Endpoint = null, int Port = 0);
public interface IDeviceRuntime
{
    DeviceDefinition Definition { get; }
    StateStore State { get; }
    ScalarValue? Read(string datapoint);
    StateTransitionResult Write(string datapoint, object? value);
    Task InvokeCommandAsync(string command, CancellationToken cancellationToken = default);
}

public sealed class InMemoryDeviceRuntime : IDeviceRuntime
{
    private readonly HashSet<string> _commands;
    public InMemoryDeviceRuntime(DeviceDefinition definition)
    { Definition = definition ?? throw new ArgumentNullException(nameof(definition)); State = new StateStore(definition); _commands = definition.Commands.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase); }
    public DeviceDefinition Definition { get; }
    public StateStore State { get; }
    public int CommandsInvoked { get; private set; }
    public ScalarValue? Read(string datapoint) => State.Get(new DataPointId(datapoint));
    public StateTransitionResult Write(string datapoint, object? value) => State.Set(new DataPointId(datapoint), value);
    public Task InvokeCommandAsync(string command, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); if (!_commands.Contains(command)) throw new ArgumentException($"Command '{command}' does not exist."); CommandsInvoked++; return Task.CompletedTask; }
}

public interface IProtocolAdapter
{
    string Name { get; }
    bool IsRunning { get; }
    Task StartAsync(IDeviceRuntime runtime, ProtocolOptions options, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
