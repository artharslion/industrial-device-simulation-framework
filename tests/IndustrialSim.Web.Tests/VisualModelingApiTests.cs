using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Web;
using IndustrialSim.Web.Api.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialSim.Web.Tests;

public sealed class VisualModelingApiTests
{
    [Fact]
    public async Task Templates_can_be_versioned_exported_and_instantiated()
    {
        await using var fixture = await Fixture.StartAsync();
        var package = TemplatePackage();
        var created = await fixture.Client.PostAsJsonAsync("/api/v1/templates", package);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await fixture.Client.PostAsJsonAsync("/api/v1/templates", package);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("templateVersionExists", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var exported = await fixture.Client.GetStringAsync("/api/v1/templates/pump/1.0.0/export");
        Assert.Contains("Centrifugal Pump", exported, StringComparison.Ordinal);
        var instantiate = await fixture.Client.PostAsJsonAsync("/api/v1/templates/pump/1.0.0/instantiate", new
        {
            deviceId = "template-pump",
            deterministic = true,
            seed = 42,
            protocols = new { modbus = new { enabled = true, port = FreePort(), mappingProfile = "holding" } }
        });
        Assert.Equal(HttpStatusCode.Created, instantiate.StatusCode);
        Assert.Contains("template-pump", await fixture.Client.GetStringAsync("/api/v1/devices"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scenarios_preserve_editor_metadata_and_support_yaml_import_export()
    {
        await using var fixture = await Fixture.StartAsync();
        const string yaml = "scenario:\n  name: startup\n  steps:\n    - at: 0s\n      command:\n        device: legacy\n        name: start";
        var saved = await fixture.Client.PutAsJsonAsync("/api/v1/scenarios/startup", new
        {
            name = "Startup",
            yaml,
            editorJson = "{\"steps\":[{\"id\":\"step-1\",\"order\":0}]}",
            version = 0
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("step-1", await saved.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var exported = await fixture.Client.GetStringAsync("/api/v1/scenarios/startup/export");
        Assert.Equal(yaml, exported);
        var imported = await fixture.Client.PostAsJsonAsync("/api/v1/scenarios/import", new
        {
            id = "startup-copy",
            name = "Startup Copy",
            yaml,
            editorJson = "{\"steps\":[]}"
        });
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        Assert.Contains("startup-copy", await fixture.Client.GetStringAsync("/api/v1/scenarios"), StringComparison.Ordinal);
    }

    private static object TemplatePackage() => new
    {
        template = new
        {
            id = "pump",
            version = "1.0.0",
            displayName = "Centrifugal Pump",
            deviceType = "Pump",
            description = "Reusable visual template",
            tags = new[] { "water" },
            dataPoints = new[] { new { name = "speed", dataType = "Double", access = "ReadWrite", initial = 0, unit = "rpm" } },
            commands = new[] { "start", "stop" },
            events = Array.Empty<string>(),
            behaviorJson = "{}"
        },
        mappings = new[]
        {
            new
            {
                templateId = "pump", templateVersion = "1.0.0", protocol = "modbus", name = "holding",
                entries = new[] { new { dataPoint = "speed", address = "40001", dataType = "Float", byteOrder = "BigEndian", wordOrder = "HighLow" } }
            }
        }
    };

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class Fixture(WebApplication app, HttpClient client, string databasePath, SimulationRegistry registry) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<Fixture> StartAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"industrial-sim-modeling-{Guid.NewGuid():N}.db");
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var registry = new SimulationRegistry();
            var legacy = SimulationHost.Create(new DeviceDefinition(
                new DeviceId("legacy"), "custom",
                [new DataPointDefinition("speed", DataType.Double, DataPointAccess.ReadWrite, 0)],
                [new CommandDefinition("start")], []), new SimulationHostOptions(true, 1));
            await registry.AddAsync(legacy);
            builder.Services.AddIndustrialSimControlPlane(registry, $"Data Source={databasePath};Pooling=False");
            var app = builder.Build();
            app.UseIndustrialSimProblemDetails();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapIndustrialSimV1Api();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new Fixture(app, new HttpClient { BaseAddress = new Uri(address) }, databasePath, registry);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            await registry.DisposeAsync();
            File.Delete(databasePath);
        }
    }
}
