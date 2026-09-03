namespace IndustrialSim.Persistence.Entities;

public sealed class DeviceCatalogEntity
{
    public required string Id { get; set; }
    public required string DefinitionJson { get; set; }
    public required string DesiredState { get; set; }
    public long Version { get; set; }
}
