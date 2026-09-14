namespace IndustrialSim.Observability.Events;

public sealed record RuntimeEventLogOptions(
    int IngressCapacity = 2048,
    int RetentionCapacity = 1000,
    int SubscriberCapacity = 256)
{
    public RuntimeEventLogOptions Validate()
    {
        if (IngressCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(IngressCapacity));
        if (RetentionCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(RetentionCapacity));
        if (SubscriberCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(SubscriberCapacity));
        return this;
    }
}
