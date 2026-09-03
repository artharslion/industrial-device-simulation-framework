using IndustrialSim.Application.Abstractions;
using IndustrialSim.Persistence.Entities;
using IndustrialSim.Persistence.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Persistence;

public sealed class IndustrialSimDbContext(DbContextOptions<IndustrialSimDbContext> options)
    : IdentityDbContext<IndustrialSimUser>(options), IUnitOfWork
{
    public DbSet<DeviceCatalogEntity> Devices => Set<DeviceCatalogEntity>();
    public DbSet<ScenarioCatalogEntity> Scenarios => Set<ScenarioCatalogEntity>();
    public DbSet<SettingCatalogEntity> Settings => Set<SettingCatalogEntity>();
    public DbSet<RuntimeSnapshotEntity> RuntimeSnapshots => Set<RuntimeSnapshotEntity>();
    public DbSet<DeviceTemplateEntity> DeviceTemplates => Set<DeviceTemplateEntity>();
    public DbSet<MappingProfileEntity> MappingProfiles => Set<MappingProfileEntity>();

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
        base.OnModelCreating(modelBuilder);
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
        modelBuilder.Entity<DeviceTemplateEntity>(entity =>
        {
            entity.ToTable("DeviceTemplates");
            entity.HasKey(item => new { item.Id, item.Version });
            entity.HasIndex(item => new { item.DisplayName, item.DeviceType });
        });
        modelBuilder.Entity<MappingProfileEntity>(entity =>
        {
            entity.ToTable("MappingProfiles");
            entity.HasKey(item => new { item.TemplateId, item.TemplateVersion, item.Protocol, item.Name });
            entity.HasOne<DeviceTemplateEntity>()
                .WithMany()
                .HasForeignKey(item => new { item.TemplateId, item.TemplateVersion })
                .OnDelete(DeleteBehavior.Cascade);
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
