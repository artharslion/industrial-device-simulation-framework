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
    StateBatchTransitionResult WriteBatch(IEnumerable<(string DataPoint, object? Value)> updates);
    Task InvokeCommandAsync(string command, CancellationToken cancellationToken = default);
    event Action<RuntimeEvent>? RuntimeEventPublished;
    void Publish(RuntimeEvent runtimeEvent);
}

public sealed class InMemoryDeviceRuntime : IDeviceRuntime
{
    private readonly HashSet<string> _commands;
    private readonly IReadOnlyDictionary<string, Func<CancellationToken, Task>> _commandHandlers;
    private readonly Func<SimulationTime> _timestamp;
    public InMemoryDeviceRuntime(DeviceDefinition definition)
        : this(definition, null, null, null) { }
    public InMemoryDeviceRuntime(DeviceDefinition definition, StateStore? state, IReadOnlyDictionary<string, Func<CancellationToken, Task>>? commandHandlers, Func<SimulationTime>? timestamp)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        State = state ?? new StateStore(definition);
        _commands = definition.Commands.Select(command => command.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _commandHandlers = commandHandlers ?? new Dictionary<string, Func<CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase);
        _timestamp = timestamp ?? (() => SimulationTime.Zero);
        State.DataPointChanged += change => RuntimeEventPublished?.Invoke(change);
    }
    public DeviceDefinition Definition { get; }
    public StateStore State { get; }
    public int CommandsInvoked { get; private set; }
    public event Action<RuntimeEvent>? RuntimeEventPublished;
    public ScalarValue? Read(string datapoint) => State.Get(new DataPointId(datapoint));
    public StateTransitionResult Write(string datapoint, object? value) => State.Set(new DataPointId(datapoint), value);
    public StateBatchTransitionResult WriteBatch(IEnumerable<(string DataPoint, object? Value)> updates) =>
        State.SetBatch(updates.Select(update => (new DataPointId(update.DataPoint), update.Value)));
    public async Task InvokeCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_commands.Contains(command)) throw new ArgumentException($"Command '{command}' does not exist.");
        if (_commandHandlers.TryGetValue(command, out var handler)) await handler(cancellationToken);
        CommandsInvoked++;
        Publish(new CommandExecuted(_timestamp(), Definition.Id, command));
    }
    public void Publish(RuntimeEvent runtimeEvent) => RuntimeEventPublished?.Invoke(runtimeEvent ?? throw new ArgumentNullException(nameof(runtimeEvent)));
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
