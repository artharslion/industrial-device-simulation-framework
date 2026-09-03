namespace IndustrialSim.Persistence.Entities;

public sealed class MappingProfileEntity
{
    public required string TemplateId { get; set; }
    public required string TemplateVersion { get; set; }
    public required string Protocol { get; set; }
    public required string Name { get; set; }
    public required string DocumentJson { get; set; }
}
