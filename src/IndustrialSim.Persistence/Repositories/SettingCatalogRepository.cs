using IndustrialSim.Application.Catalogs;
using IndustrialSim.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Persistence.Repositories;

public sealed class SettingCatalogRepository(IndustrialSimDbContext db) : ISettingCatalogRepository
{
    public async Task<SettingCatalogItem?> FindAsync(string key, CancellationToken cancellationToken = default) =>
        (await db.Settings.SingleOrDefaultAsync(item => item.Key == key, cancellationToken)) is { } entity
            ? new(entity.Key, entity.ValueJson, entity.Version) : null;

    public async Task<IReadOnlyList<SettingCatalogItem>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Settings.OrderBy(item => item.Key).Select(item => new SettingCatalogItem(item.Key, item.ValueJson, item.Version)).ToArrayAsync(cancellationToken);

    public async Task UpsertAsync(SettingCatalogItem item, CancellationToken cancellationToken = default)
    {
        var entity = await db.Settings.SingleOrDefaultAsync(value => value.Key == item.Key, cancellationToken);
        if (entity is null)
        {
            await db.Settings.AddAsync(new SettingCatalogEntity { Key = item.Key, ValueJson = item.ValueJson }, cancellationToken);
            return;
        }
        if (item.Version != entity.Version) throw new DbUpdateConcurrencyException($"Setting '{item.Key}' version conflict.");
        entity.ValueJson = item.ValueJson;
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var entity = await db.Settings.SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (entity is null) return false;
        db.Settings.Remove(entity);
        return true;
    }
}
