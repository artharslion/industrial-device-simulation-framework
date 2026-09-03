using IndustrialSim.Application.Catalogs;
using IndustrialSim.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Persistence.Repositories;

public sealed class SnapshotCatalogRepository(IndustrialSimDbContext db) : ISnapshotCatalogRepository
{
    public async Task<RuntimeSnapshotCatalogItem?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        (await db.RuntimeSnapshots.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)) is { } entity ? Map(entity) : null;

    public async Task<IReadOnlyList<RuntimeSnapshotCatalogItem>> ListForDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var entities = await db.RuntimeSnapshots.Where(item => item.DeviceId == deviceId).ToArrayAsync(cancellationToken);
        return entities.OrderByDescending(item => item.CreatedUtc).Select(Map).ToArray();
    }

    public async Task AddAsync(RuntimeSnapshotCatalogItem item, CancellationToken cancellationToken = default) =>
        await db.RuntimeSnapshots.AddAsync(new RuntimeSnapshotEntity
        {
            Id = item.Id,
            DeviceId = item.DeviceId,
            SchemaVersion = item.SchemaVersion,
            CreatedUtc = item.CreatedUtc,
            Seed = item.Seed,
            SimulationTimeTicks = item.SimulationTime.Ticks,
            DefinitionFingerprint = item.DefinitionFingerprint,
            StateJson = item.StateJson
        }, cancellationToken);

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await db.RuntimeSnapshots.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null) return false;
        db.RuntimeSnapshots.Remove(entity);
        return true;
    }

    private static RuntimeSnapshotCatalogItem Map(RuntimeSnapshotEntity item) => new(
        item.Id, item.DeviceId, item.SchemaVersion, item.CreatedUtc, item.Seed,
        TimeSpan.FromTicks(item.SimulationTimeTicks), item.DefinitionFingerprint, item.StateJson);
}
