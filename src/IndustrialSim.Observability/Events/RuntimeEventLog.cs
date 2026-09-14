using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;
using IndustrialSim.Observability.Metrics;

namespace IndustrialSim.Observability.Events;

public sealed class RuntimeEventLog : IAsyncDisposable
{
    private readonly RuntimeEventLogOptions _options;
    private readonly IRuntimeEventEnvelopeFactory _factory;
    private readonly TimeProvider _timeProvider;
    private readonly IndustrialSimMetrics? _metrics;
    private readonly Channel<EventCandidate> _ingress;
    private readonly ConcurrentDictionary<Guid, Subscriber> _subscribers = new();
    private readonly Queue<RuntimeEventEnvelope> _retained = new();
    private readonly object _retentionGate = new();
    private readonly object _attachmentGate = new();
    private readonly ConditionalWeakTable<SimulationHost, object> _attachedHosts = new();
    private ISimulationRegistry? _registry;
    private CancellationTokenSource? _pumpCancellation;
    private Task? _pump;
    private long _sequence;
    private long _ingressDropped;
    private long _subscriberDropped;
    private int _running;
    private bool _disposed;

    public RuntimeEventLog(
        RuntimeEventLogOptions options,
        IRuntimeEventEnvelopeFactory factory,
        TimeProvider timeProvider,
        IndustrialSimMetrics? metrics = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _metrics = metrics;
        _ingress = Channel.CreateBounded<EventCandidate>(new BoundedChannelOptions(_options.IngressCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public bool IsRunning => Volatile.Read(ref _running) == 1;
    public Exception? Failure { get; private set; }
    public long IngressDropped => Interlocked.Read(ref _ingressDropped);
    public long SubscriberDropped => Interlocked.Read(ref _subscriberDropped);

    public void Attach(ISimulationRegistry registry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(registry);
        lock (_attachmentGate)
        {
            if (_registry is not null)
            {
                if (ReferenceEquals(_registry, registry)) return;
                throw new InvalidOperationException("A runtime event log can observe only one simulation registry.");
            }
            _registry = registry;
            registry.SimulationAdded += AttachHost;
            _metrics?.Attach(registry);
        }
        foreach (var summary in registry.List()) AttachHost(registry.Get(summary.DeviceId));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return Task.CompletedTask;
        _pumpCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pump = Task.Run(() => PumpAsync(_pumpCancellation.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public IReadOnlyList<RuntimeEventEnvelope> Query(RuntimeEventQuery? query = null)
    {
        query ??= new RuntimeEventQuery(Limit: _options.RetentionCapacity);
        var limit = Math.Clamp(query.Limit, 1, Math.Min(1000, _options.RetentionCapacity));
        lock (_retentionGate)
            return _retained.Where(query.Matches).TakeLast(limit).ToArray();
    }

    public RuntimeEventSubscription Subscribe(RuntimeEventQuery? query = null, int? capacity = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        query ??= new RuntimeEventQuery(Limit: _options.RetentionCapacity);
        var subscriberCapacity = capacity ?? _options.SubscriberCapacity;
        if (subscriberCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        var channel = Channel.CreateBounded<RuntimeEventEnvelope>(new BoundedChannelOptions(subscriberCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var id = Guid.NewGuid();
        _subscribers[id] = new Subscriber(query, channel);
        return new RuntimeEventSubscription(channel.Reader, () =>
        {
            if (_subscribers.TryRemove(id, out var removed)) removed.Channel.Writer.TryComplete();
        });
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var pumpCancellation = _pumpCancellation;
        var pump = _pump;
        if (pumpCancellation is null || pump is null) return;
        await pumpCancellation.CancelAsync();
        await pump.WaitAsync(cancellationToken);
        pumpCancellation.Dispose();
        _pumpCancellation = null;
        _pump = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registry is not null) _registry.SimulationAdded -= AttachHost;
        _ingress.Writer.TryComplete();
        await StopAsync();
        foreach (var subscriber in _subscribers.Values) subscriber.Channel.Writer.TryComplete();
        _subscribers.Clear();
    }

    private void AttachHost(SimulationHandle handle)
    {
        var host = handle.Host;
        lock (_attachmentGate)
        {
            if (_attachedHosts.TryGetValue(host, out _)) return;
            _attachedHosts.Add(host, new object());
        }
        host.Runtime.RuntimeEventPublished += Enqueue;
        host.FaultManager.LifecycleChanged += Enqueue;
        host.ScenarioActionObserved += Enqueue;
        host.ProtocolLifecycleObserved += Enqueue;
    }

    private void Enqueue(object observation)
    {
        var candidate = new EventCandidate(
            observation,
            Interlocked.Increment(ref _sequence),
            _timeProvider.GetUtcNow(),
            Activity.Current?.Context);
        if (!_ingress.Writer.TryWrite(candidate))
        {
            Interlocked.Increment(ref _ingressDropped);
            _metrics?.RecordDroppedEvent("runtime-log", "ingress");
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var candidate in _ingress.Reader.ReadAllAsync(cancellationToken))
            {
                var envelope = _factory.Create(candidate.Observation, candidate.Sequence, candidate.ObservedAtUtc, candidate.ActivityContext);
                _metrics?.Observe(candidate.Observation);
                lock (_retentionGate)
                {
                    _retained.Enqueue(envelope);
                    while (_retained.Count > _options.RetentionCapacity) _retained.Dequeue();
                }
                foreach (var subscriber in _subscribers.Values)
                {
                    if (!subscriber.Query.Matches(envelope)) continue;
                    if (!subscriber.Channel.Writer.TryWrite(envelope))
                    {
                        Interlocked.Increment(ref _subscriberDropped);
                        _metrics?.RecordDroppedEvent("runtime-log", "subscriber");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Failure = exception;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private sealed record EventCandidate(object Observation, long Sequence, DateTimeOffset ObservedAtUtc, ActivityContext? ActivityContext);
    private sealed record Subscriber(RuntimeEventQuery Query, Channel<RuntimeEventEnvelope> Channel);
}
