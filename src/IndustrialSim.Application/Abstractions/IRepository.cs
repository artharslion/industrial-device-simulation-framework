namespace IndustrialSim.Application.Abstractions;

public interface IRepository<TAggregate, in TId>
    where TAggregate : class
{
    Task<TAggregate?> FindAsync(TId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TAggregate>> ListAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);
    void Remove(TAggregate aggregate);
}
