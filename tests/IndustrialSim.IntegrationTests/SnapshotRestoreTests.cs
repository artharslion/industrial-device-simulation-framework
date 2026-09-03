using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Hosting.Snapshots;
using IndustrialSim.Persistence;
using IndustrialSim.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.IntegrationTests;

public sealed class SnapshotRestoreTests
{
    [Fact]
    public async Task Explicit_snapshot_restores_version_time_seed_and_state()
    {
        await using var host = SimulationHost.Create(Definition("snapshot-device"), new SimulationHostOptions(true, 42));
        await host.StartAsync();
        host.State.SetInternal(new DataPointId("speed"), 900);
        host.Tick(TimeSpan.FromSeconds(2));
        var service = new RuntimeSnapshotService();
        var snapshot = service.Capture(host, "snap-1", DateTimeOffset.Parse("2026-09-03T01:02:03Z"));

        host.State.SetInternal(new DataPointId("speed"), 1200);
        host.Tick(TimeSpan.FromSeconds(3));
        service.Restore(host, snapshot);

        Assert.Equal(RuntimeSnapshotService.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(DateTimeOffset.Parse("2026-09-03T01:02:03Z"), snapshot.CreatedUtc);
        Assert.Equal(42, snapshot.Seed);
        Assert.Equal(TimeSpan.FromSeconds(2), host.Engine.CurrentTime.Elapsed);
        Assert.Equal(900, host.State.GetInternal(new DataPointId("speed"))!.Value);
        Assert.True(host.IsRunning);
    }

    [Fact]
    public async Task Snapshot_restore_rejects_incompatible_seed_atomically()
    {
        await using var source = SimulationHost.Create(Definition("device"), new SimulationHostOptions(true, 7));
        await source.StartAsync();
        source.State.SetInternal(new DataPointId("speed"), 700);
        var snapshot = new RuntimeSnapshotService().Capture(source, "snap");

        await using var target = SimulationHost.Create(Definition("device"), new SimulationHostOptions(true, 8));
        target.State.SetInternal(new DataPointId("speed"), 100);
        var error = Assert.Throws<SnapshotCompatibilityException>(() => new RuntimeSnapshotService().Restore(target, snapshot));
        Assert.Equal("snapshotSeedMismatch", error.ErrorCode);
        Assert.Equal(100, target.State.GetInternal(new DataPointId("speed"))!.Value);
    }

    [Fact]
    public async Task Locked_database_does_not_stop_an_existing_simulation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-sim-lock-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={path};Default Timeout=1;Pooling=False";
            await using (var setup = Context(connectionString))
            {
                await setup.Database.MigrateAsync();
            }
            await using var host = SimulationHost.Create(Definition("live"), new SimulationHostOptions(true, 1));
            await host.StartAsync();

            await using var locker = new SqliteConnection(connectionString);
            await locker.OpenAsync();
            await using var transaction = await locker.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await using (var command = locker.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = "UPDATE __EFMigrationsHistory SET ProductVersion = ProductVersion";
                await command.ExecuteNonQueryAsync();
            }

            await using var blocked = Context(connectionString);
            await new SettingCatalogRepository(blocked).UpsertAsync(new("locked", "true", 0));
            await Assert.ThrowsAnyAsync<Exception>(() => blocked.CommitAsync());
            host.Tick(TimeSpan.FromSeconds(1));

            Assert.True(host.IsRunning);
            Assert.Equal(TimeSpan.FromSeconds(1), host.Engine.CurrentTime.Elapsed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IndustrialSimDbContext Context(string connectionString) => new(
        new DbContextOptionsBuilder<IndustrialSimDbContext>().UseSqlite(connectionString).Options);

    private static DeviceDefinition Definition(string id) => new(
        new DeviceId(id), "custom",
        [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)], [], []);
}
