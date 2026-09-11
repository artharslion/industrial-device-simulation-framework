using System.Text.Json;
using IndustrialSim.Application.Catalogs;
using IndustrialSim.Application.Templates;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Templates;

namespace IndustrialSim.Application.Devices;

public sealed record DeviceRestoreResult(string DeviceId, bool Restored, bool Started, string? ErrorCode = null, string? Error = null);

public sealed class DeviceCatalogRestoreService(
    IDeviceCatalogRepository devices,
    ITemplateCatalogRepository templates,
    ISimulationRegistry registry)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DeviceRestoreResult>> RestoreAsync(bool autoStartDesiredRunning, CancellationToken cancellationToken = default)
    {
        var templateDefinitions = new Dictionary<(string Id, string Version), DeviceTemplateDocument>();
        foreach (var item in await templates.ListAsync(cancellationToken))
        {
            try
            {
                if (JsonSerializer.Deserialize<DeviceTemplateDocument>(item.DocumentJson, JsonOptions) is { } template)
                    templateDefinitions.TryAdd((template.Id.ToLowerInvariant(), template.Version), template);
            }
            catch (JsonException)
            {
                // A malformed template only makes legacy instances that depend on it unrestorable.
            }
        }
        DeviceDefinition? ResolveTemplate(string id, string version) =>
            templateDefinitions.TryGetValue((id.ToLowerInvariant(), version), out var template) ? TemplateCatalog.Instantiate(template, "placeholder") : null;

        var results = new List<DeviceRestoreResult>();
        foreach (var item in await devices.ListAsync(cancellationToken))
        {
            try
            {
                var launch = DeviceLaunchDocumentSerializer.Deserialize(item.LaunchJson, ResolveTemplate);
                var handle = await registry.CreateAsync(launch, cancellationToken);
                var started = false;
                if (autoStartDesiredRunning && item.DesiredState.Equals("Running", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await registry.StartAsync(item.Id, cancellationToken);
                        started = true;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        results.Add(new DeviceRestoreResult(item.Id, true, false, "protocolStartFailed", exception.Message));
                        continue;
                    }
                }
                results.Add(new DeviceRestoreResult(handle.DeviceId, true, started));
            }
            catch (DeviceLaunchDocumentException exception)
            {
                results.Add(new DeviceRestoreResult(item.Id, false, false, exception.ErrorCode, exception.Message));
            }
            catch (SimulationConflictException exception)
            {
                results.Add(new DeviceRestoreResult(item.Id, false, false, exception.ErrorCode, exception.Message));
            }
            catch (DeviceLaunchException exception)
            {
                results.Add(new DeviceRestoreResult(item.Id, false, false, exception.ErrorCode, exception.Message));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new DeviceRestoreResult(item.Id, false, false, "deviceRestoreFailed", exception.Message));
            }
        }
        return results;
    }
}
