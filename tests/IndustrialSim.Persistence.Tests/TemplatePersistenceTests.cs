using IndustrialSim.Application.Templates;
using IndustrialSim.Persistence;
using IndustrialSim.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Persistence.Tests;

public sealed class TemplatePersistenceTests
{
    [Fact]
    public async Task Template_versions_and_mappings_survive_reopen_and_are_immutable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-sim-template-{Guid.NewGuid():N}.db");
        try
        {
            var item = new DeviceTemplateCatalogItem("pump", "1.0.0", "Pump", "Pump", "[\"water\"]", "{\"id\":\"pump\"}", DateTimeOffset.Parse("2026-09-03T00:00:00Z"));
            var mapping = new MappingProfileCatalogItem("pump", "1.0.0", "modbus", "holding", "{\"entries\":[]}");
            await using (var db = CreateContext(path))
            {
                await db.Database.MigrateAsync();
                var repository = new TemplateCatalogRepository(db);
                await repository.AddAsync(item, [mapping]);
                await db.CommitAsync();
            }

            await using (var reopened = CreateContext(path))
            {
                var repository = new TemplateCatalogRepository(reopened);
                Assert.Equal("Pump", (await repository.FindAsync("pump", "1.0.0"))!.DisplayName);
                Assert.Equal("modbus", Assert.Single(await repository.ListMappingsAsync("pump", "1.0.0")).Protocol);
                await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(item, [mapping]));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IndustrialSimDbContext CreateContext(string path) => new(
        new DbContextOptionsBuilder<IndustrialSimDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
}
