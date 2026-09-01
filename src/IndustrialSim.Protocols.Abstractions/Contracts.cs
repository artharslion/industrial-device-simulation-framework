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
    private readonly IReadOnlyDictionary<string, Func<CancellationToken, Task>> _commandHandlers;
    public InMemoryDeviceRuntime(DeviceDefinition definition)
        : this(definition, null, null) { }
    public InMemoryDeviceRuntime(DeviceDefinition definition, StateStore? state, IReadOnlyDictionary<string, Func<CancellationToken, Task>>? commandHandlers)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        State = state ?? new StateStore(definition);
        _commands = definition.Commands.Select(command => command.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _commandHandlers = commandHandlers ?? new Dictionary<string, Func<CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase);
    }
    public DeviceDefinition Definition { get; }
    public StateStore State { get; }
    public int CommandsInvoked { get; private set; }
    public ScalarValue? Read(string datapoint) => State.Get(new DataPointId(datapoint));
    public StateTransitionResult Write(string datapoint, object? value) => State.Set(new DataPointId(datapoint), value);
    public async Task InvokeCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_commands.Contains(command)) throw new ArgumentException($"Command '{command}' does not exist.");
        if (_commandHandlers.TryGetValue(command, out var handler)) await handler(cancellationToken);
        CommandsInvoked++;
    }
}

public interface IProtocolAdapter
{
    string Name { get; }
    bool IsRunning { get; }
    Task StartAsync(IDeviceRuntime runtime, ProtocolOptions options, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    void ApplyTransportFault(string fault, TimeSpan duration);
    void RecoverTransportFault();
}
