using System.Collections.Concurrent;
using IndustrialSim.Core.Domain;

namespace IndustrialSim.Hosting;

public sealed record ProtocolPortBinding(string Protocol, int Port);

public sealed record DeviceLaunchDefinition(
    DeviceDefinition Definition,
    SimulationHostOptions? Options = null,
    IReadOnlyList<ProtocolPortBinding>? PortBindings = null);

public sealed record SimulationSummary(
    string DeviceId,
    string DeviceType,
    bool IsRunning,
    TimeSpan SimulationTime,
    bool IsDeterministic,
    int Seed,
    int ActiveFaults);

public sealed record SimulationBatchResult(IReadOnlyList<string> Completed, IReadOnlyDictionary<string, string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

public sealed class SimulationConflictException(string message, string errorCode) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class SimulationNotFoundException(string deviceId)
    : KeyNotFoundException($"Simulation '{deviceId}' was not found.")
{
    public string DeviceId { get; } = deviceId;
    public string ErrorCode => "simulationNotFound";
}

public sealed class SimulationHandle
{
    internal SimulationHandle(SimulationHost host, IReadOnlyList<ProtocolPortBinding> portBindings)
    {
        Host = host;
        PortBindings = portBindings;
    }

    internal SemaphoreSlim LifecycleGate { get; } = new(1, 1);
    public IReadOnlyList<ProtocolPortBinding> PortBindings { get; }
    public SimulationHost Host { get; }
    public string DeviceId => Host.Runtime.Definition.Id.Value;
}

public interface ISimulationRegistry
{
    event Action<SimulationHandle>? SimulationAdded;
    Task<SimulationHandle> CreateAsync(DeviceLaunchDefinition definition, CancellationToken cancellationToken = default);
    Task<SimulationHandle> AddAsync(SimulationHost host, IReadOnlyList<ProtocolPortBinding>? portBindings = null, CancellationToken cancellationToken = default);
    Task StartAsync(string deviceId, CancellationToken cancellationToken = default);
    Task StopAsync(string deviceId, CancellationToken cancellationToken = default);
    Task RemoveAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<SimulationBatchResult> StartManyAsync(IEnumerable<string> deviceIds, CancellationToken cancellationToken = default);
    Task<SimulationBatchResult> StopManyAsync(IEnumerable<string> deviceIds, CancellationToken cancellationToken = default);
    Task<SimulationBatchResult> RemoveManyAsync(IEnumerable<string> deviceIds, CancellationToken cancellationToken = default);
    SimulationHandle Get(string deviceId);
    IReadOnlyList<SimulationSummary> List();
}

public sealed class SimulationRegistry : ISimulationRegistry, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SimulationHandle> _simulations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _reservedPorts = [];
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private bool _disposed;

    public event Action<SimulationHandle>? SimulationAdded;

    public async Task<SimulationHandle> CreateAsync(DeviceLaunchDefinition definition, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Definition);
        var deviceId = definition.Definition.Id.Value;
        var bindings = NormalizeBindings(definition.PortBindings);

        await _catalogGate.WaitAsync(cancellationToken);
        try
        {
            if (_simulations.ContainsKey(deviceId))
                throw new SimulationConflictException($"Simulation '{deviceId}' already exists.", "duplicateDeviceId");
            foreach (var binding in bindings)
                if (_reservedPorts.TryGetValue(binding.Port, out var owner))
                    throw new SimulationConflictException(
                        $"Port {binding.Port} for protocol '{binding.Protocol}' is already reserved by simulation '{owner}'.",
                        "portConflict");

            var host = SimulationHost.Create(definition.Definition, definition.Options);
            var handle = new SimulationHandle(host, bindings);
            if (!_simulations.TryAdd(deviceId, handle))
            {
                await host.DisposeAsync();
                throw new SimulationConflictException($"Simulation '{deviceId}' already exists.", "duplicateDeviceId");
            }
            foreach (var binding in bindings) _reservedPorts.Add(binding.Port, deviceId);
            SimulationAdded?.Invoke(handle);
            return handle;
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    public async Task<SimulationHandle> AddAsync(
        SimulationHost host,
        IReadOnlyList<ProtocolPortBinding>? portBindings = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(host);
        var deviceId = host.Runtime.Definition.Id.Value;
        var bindings = NormalizeBindings(portBindings);
        await _catalogGate.WaitAsync(cancellationToken);
        try
        {
            if (_simulations.ContainsKey(deviceId))
                throw new SimulationConflictException($"Simulation '{deviceId}' already exists.", "duplicateDeviceId");
            foreach (var binding in bindings)
                if (_reservedPorts.TryGetValue(binding.Port, out var owner))
                    throw new SimulationConflictException($"Port {binding.Port} is already reserved by simulation '{owner}'.", "portConflict");
            var handle = new SimulationHandle(host, bindings);
            if (!_simulations.TryAdd(deviceId, handle))
                throw new SimulationConflictException($"Simulation '{deviceId}' already exists.", "duplicateDeviceId");
            foreach (var binding in bindings) _reservedPorts.Add(binding.Port, deviceId);
            SimulationAdded?.Invoke(handle);
            return handle;
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    public SimulationHandle Get(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Device id cannot be blank.", nameof(deviceId));
        return _simulations.TryGetValue(deviceId, out var handle) ? handle : throw new SimulationNotFoundException(deviceId);
    }

    public Task StartAsync(string deviceId, CancellationToken cancellationToken = default) =>
        WithLifecycleGateAsync(deviceId, static (host, token) => host.StartAsync(token), cancellationToken);

    public Task StopAsync(string deviceId, CancellationToken cancellationToken = default) =>
        WithLifecycleGateAsync(deviceId, static (host, token) => host.StopAsync(token), cancellationToken);

    public async Task RemoveAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _catalogGate.WaitAsync(cancellationToken);
        try
        {
            var handle = Get(deviceId);
            await handle.LifecycleGate.WaitAsync(cancellationToken);
            try
            {
                await handle.Host.StopAsync(cancellationToken);
                await handle.Host.DisposeAsync();
                _simulations.TryRemove(deviceId, out _);
                foreach (var binding in handle.PortBindings) _reservedPorts.Remove(binding.Port);
            }
            finally
            {
                handle.LifecycleGate.Release();
            }
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    public Task<SimulationBatchResult> StartManyAsync(IEnumerable<string> deviceIds, CancellationToken cancellationToken = default) =>
        RunBatchAsync(deviceIds, StartAsync, cancellationToken);

    public Task<SimulationBatchResult> StopManyAsync(IEnumerable<string> deviceIds, CancellationToken cancellationToken = default) =>
        RunBatchAsync(deviceIds, StopAsync, cancellationToken);

    public Task<SimulationBatchResult> RemoveManyAsync(IEnumerable<string> deviceIds, CancellationToken cancellationToken = default) =>
        RunBatchAsync(deviceIds, RemoveAsync, cancellationToken);

    public IReadOnlyList<SimulationSummary> List() => _simulations.Values
        .Select(handle => new SimulationSummary(
            handle.DeviceId,
            handle.Host.Runtime.Definition.Type,
            handle.Host.IsRunning,
            handle.Host.Engine.CurrentTime.Elapsed,
            handle.Host.IsDeterministic,
            handle.Host.Seed,
            handle.Host.FaultManager.ActiveFaults.Count))
        .OrderBy(summary => summary.DeviceId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        foreach (var deviceId in _simulations.Keys.ToArray())
            await RemoveAsync(deviceId, CancellationToken.None);
        _disposed = true;
        _catalogGate.Dispose();
    }

    private async Task WithLifecycleGateAsync(
        string deviceId,
        Func<SimulationHost, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var handle = Get(deviceId);
        await handle.LifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await operation(handle.Host, cancellationToken);
        }
        finally
        {
            handle.LifecycleGate.Release();
        }
    }

    private static async Task<SimulationBatchResult> RunBatchAsync(
        IEnumerable<string> deviceIds,
        Func<string, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);
        var ids = deviceIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var completed = new ConcurrentBag<string>();
        var errors = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await Task.WhenAll(ids.Select(async id =>
        {
            try
            {
                await operation(id, cancellationToken);
                completed.Add(id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors[id] = exception.Message;
            }
        }));
        return new SimulationBatchResult(
            completed.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            new Dictionary<string, string>(errors, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<ProtocolPortBinding> NormalizeBindings(IReadOnlyList<ProtocolPortBinding>? bindings)
    {
        var normalized = bindings?.ToArray() ?? [];
        foreach (var binding in normalized)
        {
            if (string.IsNullOrWhiteSpace(binding.Protocol)) throw new ArgumentException("Protocol name cannot be blank.", nameof(bindings));
            if (binding.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(bindings), "Protocol ports must be between 1 and 65535.");
        }
        var duplicate = normalized.GroupBy(binding => binding.Port).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new SimulationConflictException($"Port {duplicate.Key} is requested more than once.", "portConflict");
        return normalized;
    }
}
