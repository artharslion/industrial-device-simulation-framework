namespace IndustrialSim.Persistence.Entities;

public sealed class RuntimeSnapshotEntity
{
    public required string Id { get; set; }
    public required string DeviceId { get; set; }
    public int SchemaVersion { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public int Seed { get; set; }
    public long SimulationTimeTicks { get; set; }
    public required string DefinitionFingerprint { get; set; }
    public required string StateJson { get; set; }
}
