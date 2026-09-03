using IndustrialSim.Application.Catalogs;
using IndustrialSim.Persistence;
using IndustrialSim.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Persistence.Tests;

public sealed class CatalogPersistenceTests
{
    [Fact]
    public async Task Devices_scenarios_settings_and_snapshots_survive_reopen()
    {
        var path = TempDatabase();
        try
        {
            await using (var db = CreateContext(path))
            {
                await db.Database.MigrateAsync();
                await new DeviceCatalogRepository(db).UpsertAsync(new DeviceCatalogItem("pump-1", "{\"type\":\"pump\"}", "Stopped", 0));
                await new ScenarioCatalogRepository(db).UpsertAsync(new ScenarioCatalogItem("startup", "Startup", "scenario: {}", 0));
                await new SettingCatalogRepository(db).UpsertAsync(new SettingCatalogItem("ui.refreshSeconds", "1", 0));
                await new SnapshotCatalogRepository(db).AddAsync(new RuntimeSnapshotCatalogItem(
                    "snap-1", "pump-1", 1, DateTimeOffset.Parse("2026-09-03T00:00:00Z"), 123,
                    TimeSpan.FromSeconds(4), "fingerprint", "{\"speed\":900}"));
                await db.CommitAsync();
            }

            await using (var reopened = CreateContext(path))
            {
                Assert.Equal("pump-1", (await new DeviceCatalogRepository(reopened).ListAsync()).Single().Id);
                Assert.Equal("startup", (await new ScenarioCatalogRepository(reopened).ListAsync()).Single().Id);
                Assert.Equal("1", (await new SettingCatalogRepository(reopened).FindAsync("ui.refreshSeconds"))!.ValueJson);
                var snapshot = (await new SnapshotCatalogRepository(reopened).ListForDeviceAsync("pump-1")).Single();
                Assert.Equal(1, snapshot.SchemaVersion);
                Assert.Equal(123, snapshot.Seed);
                Assert.Equal(TimeSpan.FromSeconds(4), snapshot.SimulationTime);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Concurrent_catalog_updates_use_optimistic_versions()
    {
        var path = TempDatabase();
        try
        {
            await using (var setup = CreateContext(path))
            {
                await setup.Database.MigrateAsync();
                await new SettingCatalogRepository(setup).UpsertAsync(new SettingCatalogItem("mode", "\"one\"", 0));
                await setup.CommitAsync();
            }

            await using var first = CreateContext(path);
            await using var second = CreateContext(path);
            var firstRepository = new SettingCatalogRepository(first);
            var secondRepository = new SettingCatalogRepository(second);
            var firstValue = await firstRepository.FindAsync("mode");
            var secondValue = await secondRepository.FindAsync("mode");
            await firstRepository.UpsertAsync(firstValue! with { ValueJson = "\"two\"" });
            await secondRepository.UpsertAsync(secondValue! with { ValueJson = "\"three\"" });
            await first.CommitAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.CommitAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IndustrialSimDbContext CreateContext(string path) => new(
        new DbContextOptionsBuilder<IndustrialSimDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options);

    private static string TempDatabase() => Path.Combine(Path.GetTempPath(), $"industrial-sim-{Guid.NewGuid():N}.db");
}
