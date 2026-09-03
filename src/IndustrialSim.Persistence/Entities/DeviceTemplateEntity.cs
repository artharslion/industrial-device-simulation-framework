namespace IndustrialSim.Persistence.Entities;

public sealed class DeviceTemplateEntity
{
    public required string Id { get; set; }
    public required string Version { get; set; }
    public required string DisplayName { get; set; }
    public required string DeviceType { get; set; }
    public required string TagsJson { get; set; }
    public required string DocumentJson { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
