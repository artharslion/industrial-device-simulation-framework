using IndustrialSim.Application.Templates;
using IndustrialSim.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSim.Persistence.Repositories;

public sealed class TemplateCatalogRepository(IndustrialSimDbContext db) : ITemplateCatalogRepository
{
    public async Task<DeviceTemplateCatalogItem?> FindAsync(string id, string version, CancellationToken cancellationToken = default) =>
        (await db.DeviceTemplates.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.Version == version, cancellationToken)) is { } entity
            ? Map(entity)
            : null;

    public async Task<IReadOnlyList<DeviceTemplateCatalogItem>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.DeviceTemplates.AsNoTracking()
            .OrderBy(item => item.DisplayName).ThenByDescending(item => item.Version)
            .Select(item => new DeviceTemplateCatalogItem(item.Id, item.Version, item.DisplayName, item.DeviceType, item.TagsJson, item.DocumentJson, item.CreatedUtc))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<MappingProfileCatalogItem>> ListMappingsAsync(string id, string version, CancellationToken cancellationToken = default) =>
        await db.MappingProfiles.AsNoTracking()
            .Where(item => item.TemplateId == id && item.TemplateVersion == version)
            .OrderBy(item => item.Protocol).ThenBy(item => item.Name)
            .Select(item => new MappingProfileCatalogItem(item.TemplateId, item.TemplateVersion, item.Protocol, item.Name, item.DocumentJson))
            .ToArrayAsync(cancellationToken);

    public async Task AddAsync(DeviceTemplateCatalogItem template, IReadOnlyList<MappingProfileCatalogItem> mappings, CancellationToken cancellationToken = default)
    {
        if (await db.DeviceTemplates.AnyAsync(item => item.Id == template.Id && item.Version == template.Version, cancellationToken))
            throw new InvalidOperationException($"Template '{template.Id}@{template.Version}' already exists and immutable versions cannot be overwritten.");

        await db.DeviceTemplates.AddAsync(new DeviceTemplateEntity
        {
            Id = template.Id,
            Version = template.Version,
            DisplayName = template.DisplayName,
            DeviceType = template.DeviceType,
            TagsJson = template.TagsJson,
            DocumentJson = template.DocumentJson,
            CreatedUtc = template.CreatedUtc
        }, cancellationToken);
        await db.MappingProfiles.AddRangeAsync(mappings.Select(mapping => new MappingProfileEntity
        {
            TemplateId = mapping.TemplateId,
            TemplateVersion = mapping.TemplateVersion,
            Protocol = mapping.Protocol,
            Name = mapping.Name,
            DocumentJson = mapping.DocumentJson
        }), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string id, string version, CancellationToken cancellationToken = default)
    {
        var entity = await db.DeviceTemplates.SingleOrDefaultAsync(item => item.Id == id && item.Version == version, cancellationToken);
        if (entity is null) return false;
        db.DeviceTemplates.Remove(entity);
        return true;
    }

    private static DeviceTemplateCatalogItem Map(DeviceTemplateEntity item) =>
        new(item.Id, item.Version, item.DisplayName, item.DeviceType, item.TagsJson, item.DocumentJson, item.CreatedUtc);
}
