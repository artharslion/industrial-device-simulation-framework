namespace IndustrialSim.Persistence.Entities;

public sealed class SettingCatalogEntity
{
    public required string Key { get; set; }
    public required string ValueJson { get; set; }
    public long Version { get; set; }
}
