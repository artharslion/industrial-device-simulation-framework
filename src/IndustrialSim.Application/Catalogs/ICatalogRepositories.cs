namespace IndustrialSim.Application.Catalogs;

public interface IDeviceCatalogRepository
{
    Task<DeviceCatalogItem?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceCatalogItem>> ListAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(DeviceCatalogItem item, CancellationToken cancellationToken = default);
    Task<bool> SetDesiredStateAsync(string id, string desiredState, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default);
}

public interface IScenarioCatalogRepository
{
    Task<ScenarioCatalogItem?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScenarioCatalogItem>> ListAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(ScenarioCatalogItem item, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default);
}

public interface ISettingCatalogRepository
{
    Task<SettingCatalogItem?> FindAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SettingCatalogItem>> ListAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(SettingCatalogItem item, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public interface ISnapshotCatalogRepository
{
    Task<RuntimeSnapshotCatalogItem?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuntimeSnapshotCatalogItem>> ListForDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
    Task AddAsync(RuntimeSnapshotCatalogItem item, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default);
}
