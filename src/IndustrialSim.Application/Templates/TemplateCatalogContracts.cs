namespace IndustrialSim.Application.Templates;

public sealed record DeviceTemplateCatalogItem(
    string Id,
    string Version,
    string DisplayName,
    string DeviceType,
    string TagsJson,
    string DocumentJson,
    DateTimeOffset CreatedUtc);

public sealed record MappingProfileCatalogItem(
    string TemplateId,
    string TemplateVersion,
    string Protocol,
    string Name,
    string DocumentJson);

public interface ITemplateCatalogRepository
{
    Task<DeviceTemplateCatalogItem?> FindAsync(string id, string version, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceTemplateCatalogItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MappingProfileCatalogItem>> ListMappingsAsync(string id, string version, CancellationToken cancellationToken = default);
    Task AddAsync(DeviceTemplateCatalogItem template, IReadOnlyList<MappingProfileCatalogItem> mappings, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string id, string version, CancellationToken cancellationToken = default);
}
