using System.Threading.Channels;

namespace IndustrialSim.Observability.Events;

public sealed class RuntimeEventSubscription : IAsyncDisposable
{
    private readonly Action _dispose;
    private int _disposed;

    internal RuntimeEventSubscription(ChannelReader<RuntimeEventEnvelope> reader, Action dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    public ChannelReader<RuntimeEventEnvelope> Reader { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _dispose();
        return ValueTask.CompletedTask;
    }
}
