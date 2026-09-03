using IndustrialSim.Application.Catalogs;
using IndustrialSim.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Persistence.Repositories;

public sealed class DeviceCatalogRepository(IndustrialSimDbContext db) : IDeviceCatalogRepository
{
    public async Task<DeviceCatalogItem?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        (await db.Devices.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)) is { } entity ? Map(entity) : null;

    public async Task<IReadOnlyList<DeviceCatalogItem>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Devices.OrderBy(item => item.Id).Select(item => new DeviceCatalogItem(item.Id, item.DefinitionJson, item.DesiredState, item.Version)).ToArrayAsync(cancellationToken);

    public async Task UpsertAsync(DeviceCatalogItem item, CancellationToken cancellationToken = default)
    {
        var entity = await db.Devices.SingleOrDefaultAsync(value => value.Id == item.Id, cancellationToken);
        if (entity is null)
        {
            await db.Devices.AddAsync(new DeviceCatalogEntity { Id = item.Id, DefinitionJson = item.DefinitionJson, DesiredState = item.DesiredState }, cancellationToken);
            return;
        }
        EnsureVersion(item.Version, entity.Version, item.Id);
        entity.DefinitionJson = item.DefinitionJson;
        entity.DesiredState = item.DesiredState;
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Devices.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null) return false;
        db.Devices.Remove(entity);
        return true;
    }

    private static DeviceCatalogItem Map(DeviceCatalogEntity entity) => new(entity.Id, entity.DefinitionJson, entity.DesiredState, entity.Version);
    private static void EnsureVersion(long requested, long current, string id)
    {
        if (requested != current) throw new DbUpdateConcurrencyException($"Device '{id}' version {requested} does not match current version {current}.");
    }
}
