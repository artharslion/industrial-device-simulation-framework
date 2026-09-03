using System.Text.Json;
using IndustrialSim.Application.Catalogs;
using IndustrialSim.Application.Security;
using IndustrialSim.Application.Templates;
using IndustrialSim.Hosting;
using IndustrialSim.Persistence;
using IndustrialSim.Scenarios;
using IndustrialSim.Templates;

namespace IndustrialSim.Web.Api.V1;

public static class VisualModelingEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapVisualModelingEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/templates", ListTemplatesAsync);
        api.MapGet("/templates/{id}/{version}", GetTemplateAsync);
        api.MapPost("/templates", CreateTemplateAsync).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapPost("/templates/import", CreateTemplateAsync).RequireAuthorization(IndustrialPolicies.Operator);
        api.MapGet("/templates/{id}/{version}/export", ExportTemplateAsync);
        api.MapDelete("/templates/{id}/{version}", DeleteTemplateAsync).RequireAuthorization(IndustrialPolicies.Admin);
        api.MapPost("/templates/{id}/{version}/instantiate", InstantiateTemplateAsync).RequireAuthorization(IndustrialPolicies.Operator);

        api.MapGet("/scenarios/{scenarioId}/export", ExportScenarioAsync);
        api.MapPost("/scenarios/import", ImportScenarioAsync).RequireAuthorization(IndustrialPolicies.Operator);
        return api;
    }

    private static async Task<IResult> ListTemplatesAsync(
        string? q,
        string? tag,
        ITemplateCatalogRepository repository,
        CancellationToken cancellationToken)
    {
        var documents = (await repository.ListAsync(cancellationToken))
            .Select(item => JsonSerializer.Deserialize<DeviceTemplateDocument>(item.DocumentJson, JsonOptions)!)
            .ToArray();
        return Results.Ok(TemplateCatalog.Search(documents, q, tag));
    }

    private static async Task<IResult> GetTemplateAsync(
        string id,
        string version,
        ITemplateCatalogRepository repository,
        CancellationToken cancellationToken)
    {
        var template = await repository.FindAsync(id, version, cancellationToken);
        if (template is null) return NotFound(id, version);
        return Results.Ok(await PackageAsync(template, repository, cancellationToken));
    }

    private static async Task<IResult> CreateTemplateAsync(
        TemplatePackage package,
        ITemplateCatalogRepository repository,
        IndustrialSimDbContext db,
        CancellationToken cancellationToken)
    {
        try
        {
            TemplateCatalog.Validate(package.Template, package.Mappings);
            var documentJson = JsonSerializer.Serialize(package.Template, JsonOptions);
            var item = new DeviceTemplateCatalogItem(
                package.Template.Id,
                package.Template.Version,
                package.Template.DisplayName,
                package.Template.DeviceType,
                JsonSerializer.Serialize(package.Template.Tags, JsonOptions),
                documentJson,
                DateTimeOffset.UtcNow);
            var mappings = package.Mappings.Select(mapping => new MappingProfileCatalogItem(
                mapping.TemplateId, mapping.TemplateVersion, mapping.Protocol, mapping.Name,
                JsonSerializer.Serialize(mapping, JsonOptions))).ToArray();
            await repository.AddAsync(item, mappings, cancellationToken);
            await db.CommitAsync(cancellationToken);
            return Results.Created($"/api/v1/templates/{item.Id}/{item.Version}", package);
        }
        catch (TemplateValidationException exception)
        {
            return IndustrialSimProblemDetails.Result(400, "Invalid template", exception.Message, "templateInvalid");
        }
        catch (InvalidOperationException exception)
        {
            return IndustrialSimProblemDetails.Result(409, "Template version exists", exception.Message, "templateVersionExists");
        }
    }

    private static async Task<IResult> ExportTemplateAsync(
        string id,
        string version,
        ITemplateCatalogRepository repository,
        CancellationToken cancellationToken)
    {
        var template = await repository.FindAsync(id, version, cancellationToken);
        if (template is null) return NotFound(id, version);
        var package = await PackageAsync(template, repository, cancellationToken);
        return Results.Text(TemplateCatalog.Export(package.Template, package.Mappings), "application/json");
    }

    private static async Task<IResult> DeleteTemplateAsync(
        string id,
        string version,
        ITemplateCatalogRepository repository,
        IndustrialSimDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await repository.RemoveAsync(id, version, cancellationToken)) return NotFound(id, version);
        await db.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> InstantiateTemplateAsync(
        string id,
        string version,
        InstantiateTemplateRequest request,
        ITemplateCatalogRepository templates,
        IDeviceCatalogRepository devices,
        ISimulationRegistry registry,
        IndustrialSimDbContext db,
        CancellationToken cancellationToken)
    {
        var item = await templates.FindAsync(id, version, cancellationToken);
        if (item is null) return NotFound(id, version);
        var template = JsonSerializer.Deserialize<DeviceTemplateDocument>(item.DocumentJson, JsonOptions)!;
        DeviceLaunchDefinition launch;
        try
        {
            launch = new DeviceLaunchDefinition(
                TemplateCatalog.Instantiate(template, request.DeviceId),
                new SimulationHostOptions(request.Deterministic, request.Seed),
                request.PortBindings?.Select(binding => new ProtocolPortBinding(binding.Protocol, binding.Port)).ToArray());
        }
        catch (TemplateValidationException exception)
        {
            return IndustrialSimProblemDetails.Result(400, "Invalid template", exception.Message, "templateInvalid");
        }

        var handle = await registry.CreateAsync(launch, cancellationToken);
        try
        {
            await devices.UpsertAsync(new DeviceCatalogItem(
                request.DeviceId,
                JsonSerializer.Serialize(new
                {
                    id = request.DeviceId,
                    type = template.DeviceType,
                    templateId = template.Id,
                    templateVersion = template.Version,
                    deterministic = request.Deterministic,
                    seed = request.Seed,
                    dataPoints = template.DataPoints,
                    portBindings = request.PortBindings
                }, JsonOptions),
                "Stopped",
                0), cancellationToken);
            await db.CommitAsync(cancellationToken);
        }
        catch
        {
            await registry.RemoveAsync(handle.DeviceId, CancellationToken.None);
            throw;
        }
        return Results.Created($"/api/v1/devices/{request.DeviceId}", new
        {
            deviceId = handle.DeviceId,
            templateId = template.Id,
            templateVersion = template.Version,
            deviceType = template.DeviceType
        });
    }

    private static async Task<IResult> ExportScenarioAsync(
        string scenarioId,
        IScenarioCatalogRepository repository,
        CancellationToken cancellationToken) =>
        await repository.FindAsync(scenarioId, cancellationToken) is { } scenario
            ? Results.Text(scenario.Yaml, "application/yaml")
            : IndustrialSimProblemDetails.Result(404, "Scenario not found", $"Scenario '{scenarioId}' was not found.", "scenarioNotFound");

    private static async Task<IResult> ImportScenarioAsync(
        ImportScenarioRequest request,
        IScenarioCatalogRepository repository,
        IndustrialSimDbContext db,
        CancellationToken cancellationToken)
    {
        _ = new ScenarioParser().Parse(request.Yaml);
        _ = JsonDocument.Parse(request.EditorJson);
        if (await repository.FindAsync(request.Id, cancellationToken) is not null)
            return IndustrialSimProblemDetails.Result(409, "Scenario exists", $"Scenario '{request.Id}' already exists.", "scenarioExists");
        await repository.UpsertAsync(new ScenarioCatalogItem(request.Id, request.Name, request.Yaml, 0, request.EditorJson), cancellationToken);
        await db.CommitAsync(cancellationToken);
        return Results.Created($"/api/v1/scenarios/{request.Id}", await repository.FindAsync(request.Id, cancellationToken));
    }

    private static async Task<TemplatePackage> PackageAsync(
        DeviceTemplateCatalogItem item,
        ITemplateCatalogRepository repository,
        CancellationToken cancellationToken)
    {
        var template = JsonSerializer.Deserialize<DeviceTemplateDocument>(item.DocumentJson, JsonOptions)!;
        var mappings = (await repository.ListMappingsAsync(item.Id, item.Version, cancellationToken))
            .Select(mapping => JsonSerializer.Deserialize<ProtocolMappingProfile>(mapping.DocumentJson, JsonOptions)!)
            .ToArray();
        return new TemplatePackage(template, mappings);
    }

    private static IResult NotFound(string id, string version) =>
        IndustrialSimProblemDetails.Result(404, "Template not found", $"Template '{id}@{version}' was not found.", "templateNotFound");
}
