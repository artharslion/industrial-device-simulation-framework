using System.Collections.Concurrent;
using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.OpcUa;

namespace IndustrialSim.Hosting;

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
    Task<SimulationHandle> ReplaceAsync(string deviceId, DeviceLaunchDefinition definition, Func<CancellationToken, Task> commitControlPlane, CancellationToken cancellationToken = default);
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
    private readonly Dictionary<int, PortReservation> _reservedPorts = [];
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private bool _disposed;

    public SimulationRegistry() => OpcUaServers = new OpcUaEndpointHostManager();

    public OpcUaEndpointHostManager OpcUaServers { get; }

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
            ValidateReservations(bindings, deviceId);

            var host = SimulationHost.Create(definition, OpcUaServers);
            var handle = new SimulationHandle(host, bindings);
            if (!_simulations.TryAdd(deviceId, handle))
            {
                await host.DisposeAsync();
                throw new SimulationConflictException($"Simulation '{deviceId}' already exists.", "duplicateDeviceId");
            }
            AddReservations(bindings, deviceId);
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
        var bindings = NormalizeBindings(portBindings ?? host.PortBindings);
        host.UseOpcUaServers(OpcUaServers);
        await _catalogGate.WaitAsync(cancellationToken);
        try
        {
            if (_simulations.ContainsKey(deviceId))
                throw new SimulationConflictException($"Simulation '{deviceId}' already exists.", "duplicateDeviceId");
            ValidateReservations(bindings, deviceId);
            var handle = new SimulationHandle(host, bindings);
            if (!_simulations.TryAdd(deviceId, handle))
                throw new SimulationConflictException($"Simulation '{deviceId}' already exists.", "duplicateDeviceId");
            AddReservations(bindings, deviceId);
            SimulationAdded?.Invoke(handle);
            return handle;
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    public async Task<SimulationHandle> ReplaceAsync(
        string deviceId,
        DeviceLaunchDefinition definition,
        Func<CancellationToken, Task> commitControlPlane,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(commitControlPlane);
        if (!definition.Definition.Id.Value.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Replacement definition id must match the target device id.", nameof(definition));

        var bindings = NormalizeBindings(definition.PortBindings);
        var candidateHost = SimulationHost.Create(definition, OpcUaServers);
        var candidate = new SimulationHandle(candidateHost, bindings);
        var committed = false;

        await _catalogGate.WaitAsync(cancellationToken);
        try
        {
            var current = Get(deviceId);
            await current.LifecycleGate.WaitAsync(cancellationToken);
            try
            {
                if (current.Host.IsRunning)
                    throw new SimulationConflictException($"Simulation '{deviceId}' must be stopped before its definition can be replaced.", "deviceMustBeStopped");
                ReleaseReservations(current.PortBindings, deviceId);
                try { ValidateReservations(bindings, deviceId); }
                catch
                {
                    AddReservations(current.PortBindings, deviceId);
                    throw;
                }

                _simulations[deviceId] = candidate;
                AddReservations(bindings, deviceId);
                try
                {
                    await commitControlPlane(cancellationToken);
                    committed = true;
                    SimulationAdded?.Invoke(candidate);
                }
                catch
                {
                    _simulations[deviceId] = current;
                    ReleaseReservations(bindings, deviceId);
                    AddReservations(current.PortBindings, deviceId);
                    throw;
                }

                await current.Host.DisposeAsync();
                return candidate;
            }
            finally
            {
                current.LifecycleGate.Release();
            }
        }
        finally
        {
            _catalogGate.Release();
            if (!committed) await candidateHost.DisposeAsync();
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
                ReleaseReservations(handle.PortBindings, deviceId);
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
        await OpcUaServers.DisposeAsync();
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

    private void ValidateReservations(IReadOnlyList<ProtocolPortBinding> bindings, string deviceId)
    {
        foreach (var binding in bindings)
        {
            if (!_reservedPorts.TryGetValue(binding.Port, out var existing)) continue;
            var compatibleSharedOpcUa = binding.Protocol.Equals("opcua", StringComparison.OrdinalIgnoreCase)
                && existing.Protocol.Equals("opcua", StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.ListenerKey, ListenerKey(binding, deviceId), StringComparison.Ordinal);
            if (compatibleSharedOpcUa) continue;
            throw new SimulationConflictException(
                $"Port {binding.Port} for protocol '{binding.Protocol}' conflicts with listener '{existing.ListenerKey}' used by simulation '{existing.Members.Order(StringComparer.OrdinalIgnoreCase).First()}'.",
                "portConflict");
        }
    }

    private void AddReservations(IReadOnlyList<ProtocolPortBinding> bindings, string deviceId)
    {
        foreach (var binding in bindings)
        {
            if (!_reservedPorts.TryGetValue(binding.Port, out var reservation))
            {
                reservation = new PortReservation(binding.Protocol.ToLowerInvariant(), ListenerKey(binding, deviceId));
                _reservedPorts.Add(binding.Port, reservation);
            }
            reservation.Members.Add(deviceId);
        }
    }

    private void ReleaseReservations(IReadOnlyList<ProtocolPortBinding> bindings, string deviceId)
    {
        foreach (var binding in bindings)
        {
            if (!_reservedPorts.TryGetValue(binding.Port, out var reservation)) continue;
            reservation.Members.Remove(deviceId);
            if (reservation.Members.Count == 0) _reservedPorts.Remove(binding.Port);
        }
    }

    private static string ListenerKey(ProtocolPortBinding binding, string deviceId) =>
        binding.Protocol.Equals("opcua", StringComparison.OrdinalIgnoreCase)
            ? binding.ListenerKey ?? throw new ArgumentException("OPC UA port binding requires a normalized endpoint listener key.")
            : $"{binding.Protocol.ToLowerInvariant()}:{deviceId.ToLowerInvariant()}";

    private sealed class PortReservation(string protocol, string listenerKey)
    {
        public string Protocol { get; } = protocol;
        public string ListenerKey { get; } = listenerKey;
        public HashSet<string> Members { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
