namespace IndustrialSim.Persistence.Entities;

public sealed class ScenarioCatalogEntity
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Yaml { get; set; }
    public long Version { get; set; }
}
