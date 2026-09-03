using IndustrialSim.Application.Catalogs;
using IndustrialSim.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Persistence.Repositories;

public sealed class ScenarioCatalogRepository(IndustrialSimDbContext db) : IScenarioCatalogRepository
{
    public async Task<ScenarioCatalogItem?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        (await db.Scenarios.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)) is { } entity
            ? new(entity.Id, entity.Name, entity.Yaml, entity.Version) : null;

    public async Task<IReadOnlyList<ScenarioCatalogItem>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Scenarios.OrderBy(item => item.Id).Select(item => new ScenarioCatalogItem(item.Id, item.Name, item.Yaml, item.Version)).ToArrayAsync(cancellationToken);

    public async Task UpsertAsync(ScenarioCatalogItem item, CancellationToken cancellationToken = default)
    {
        var entity = await db.Scenarios.SingleOrDefaultAsync(value => value.Id == item.Id, cancellationToken);
        if (entity is null)
        {
            await db.Scenarios.AddAsync(new ScenarioCatalogEntity { Id = item.Id, Name = item.Name, Yaml = item.Yaml }, cancellationToken);
            return;
        }
        if (item.Version != entity.Version) throw new DbUpdateConcurrencyException($"Scenario '{item.Id}' version conflict.");
        entity.Name = item.Name;
        entity.Yaml = item.Yaml;
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Scenarios.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null) return false;
        db.Scenarios.Remove(entity);
        return true;
    }
}
