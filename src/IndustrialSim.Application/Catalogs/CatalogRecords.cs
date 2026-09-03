namespace IndustrialSim.Application.Catalogs;

public sealed record DeviceCatalogItem(string Id, string DefinitionJson, string DesiredState, long Version);
public sealed record ScenarioCatalogItem(string Id, string Name, string Yaml, long Version);
public sealed record SettingCatalogItem(string Key, string ValueJson, long Version);
public sealed record RuntimeSnapshotCatalogItem(
    string Id,
    string DeviceId,
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    int Seed,
    TimeSpan SimulationTime,
    string DefinitionFingerprint,
    string StateJson);
