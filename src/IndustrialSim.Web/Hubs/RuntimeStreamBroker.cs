using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;

namespace IndustrialSim.Web.Hubs;

public sealed record RuntimeStreamEvent(long Sequence, string DeviceId, string DataPoint, JsonElement Value, TimeSpan SimulationTime);

public sealed class RuntimeStreamSubscription : IAsyncDisposable
{
    private readonly Action _dispose;
    private int _disposed;

    internal RuntimeStreamSubscription(ChannelReader<RuntimeStreamEvent> reader, Action dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    public ChannelReader<RuntimeStreamEvent> Reader { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class RuntimeStreamBroker
{
    private readonly ConcurrentDictionary<Guid, Channel<RuntimeStreamEvent>> _subscribers = new();
    private readonly HashSet<SimulationHost> _attached = [];
    private readonly object _attachGate = new();
    private readonly int _capacity;
    private long _sequence;
    private long _droppedEvents;

    public RuntimeStreamBroker(int capacity = 256) => _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);

    public void Attach(ISimulationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var summary in registry.List()) Attach(registry.Get(summary.DeviceId));
        registry.SimulationAdded += Attach;
    }

    public RuntimeStreamSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<RuntimeStreamEvent>(new BoundedChannelOptions(_capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _subscribers[id] = channel;
        return new RuntimeStreamSubscription(channel.Reader, () =>
        {
            if (_subscribers.TryRemove(id, out var removed)) removed.Writer.TryComplete();
        });
    }

    private void Attach(SimulationHandle handle)
    {
        lock (_attachGate)
        {
            if (!_attached.Add(handle.Host)) return;
            handle.Host.State.DataPointChanged += changed => Publish(handle.Host, changed);
        }
    }

    private void Publish(SimulationHost host, DataPointChanged changed)
    {
        var @event = new RuntimeStreamEvent(
            Interlocked.Increment(ref _sequence),
            changed.DeviceId.Value,
            changed.DataPointId.Value,
            JsonSerializer.SerializeToElement(changed.NewValue.Value, changed.NewValue.Value.GetType()),
            host.Engine.CurrentTime.Elapsed);
        foreach (var channel in _subscribers.Values)
            if (!channel.Writer.TryWrite(@event)) Interlocked.Increment(ref _droppedEvents);
    }
}
