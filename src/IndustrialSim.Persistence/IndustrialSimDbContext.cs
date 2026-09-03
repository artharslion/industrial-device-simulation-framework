using IndustrialSim.Application.Abstractions;
using IndustrialSim.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Persistence;

public sealed class IndustrialSimDbContext(DbContextOptions<IndustrialSimDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<DeviceCatalogEntity> Devices => Set<DeviceCatalogEntity>();
    public DbSet<ScenarioCatalogEntity> Scenarios => Set<ScenarioCatalogEntity>();
    public DbSet<SettingCatalogEntity> Settings => Set<SettingCatalogEntity>();
    public DbSet<RuntimeSnapshotEntity> RuntimeSnapshots => Set<RuntimeSnapshotEntity>();

    public async Task CommitAsync(CancellationToken cancellationToken = default) =>
        await SaveChangesAsync(cancellationToken);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AdvanceVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AdvanceVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceCatalogEntity>(entity =>
        {
            entity.ToTable("Devices");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<ScenarioCatalogEntity>(entity =>
        {
            entity.ToTable("Scenarios");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<SettingCatalogEntity>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasKey(item => item.Key);
            entity.Property(item => item.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<RuntimeSnapshotEntity>(entity =>
        {
            entity.ToTable("RuntimeSnapshots");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.DeviceId, item.CreatedUtc });
        });
    }

    private void AdvanceVersions()
    {
        foreach (var entry in ChangeTracker.Entries().Where(entry =>
                     entry.Entity is DeviceCatalogEntity or ScenarioCatalogEntity or SettingCatalogEntity))
        {
            var version = entry.Property(nameof(DeviceCatalogEntity.Version));
            if (entry.State == EntityState.Added) version.CurrentValue = 1L;
            if (entry.State == EntityState.Modified) version.CurrentValue = (long)(version.OriginalValue ?? 0L) + 1L;
        }
    }
}
