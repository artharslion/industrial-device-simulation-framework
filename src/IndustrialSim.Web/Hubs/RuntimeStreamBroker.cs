using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using IndustrialSim.Observability.Events;
using IndustrialSim.Observability.Metrics;

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

public sealed class RuntimeStreamBroker : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, Channel<RuntimeStreamEvent>> _subscribers = new();
    private readonly RuntimeEventSubscription _source;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _pump;
    private readonly int _capacity;
    private readonly IndustrialSimMetrics? _metrics;
    private long _droppedEvents;

    public RuntimeStreamBroker(RuntimeEventLog eventLog, IndustrialSimMetrics? metrics = null, int capacity = 256)
    {
        ArgumentNullException.ThrowIfNull(eventLog);
        _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
        _metrics = metrics;
        _source = eventLog.Subscribe(
            new RuntimeEventQuery(EventTypes: ["DataPointChanged"], Limit: 1000),
            Math.Max(256, capacity));
        _pump = Task.Run(() => PumpAsync(_cancellation.Token), CancellationToken.None);
    }

    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);

    public RuntimeStreamSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<RuntimeStreamEvent>(new BoundedChannelOptions(_capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        _subscribers[id] = channel;
        return new RuntimeStreamSubscription(channel.Reader, () =>
        {
            if (_subscribers.TryRemove(id, out var removed)) removed.Writer.TryComplete();
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        await _pump;
        await _source.DisposeAsync();
        _cancellation.Dispose();
        foreach (var channel in _subscribers.Values) channel.Writer.TryComplete();
        _subscribers.Clear();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var envelope in _source.Reader.ReadAllAsync(cancellationToken))
            {
                var @event = new RuntimeStreamEvent(
                    envelope.Sequence,
                    envelope.DeviceId,
                    envelope.Data.GetProperty("dataPoint").GetString()!,
                    envelope.Data.GetProperty("newValue").Clone(),
                    envelope.SimulationTime);
                foreach (var channel in _subscribers.Values)
                    if (!channel.Writer.TryWrite(@event))
                    {
                        Interlocked.Increment(ref _droppedEvents);
                        _metrics?.RecordDroppedEvent("signalr", "subscriber");
                    }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
