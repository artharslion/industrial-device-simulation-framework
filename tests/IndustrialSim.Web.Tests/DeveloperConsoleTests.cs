using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace IndustrialSim.Web.Tests;

public sealed class DeveloperConsoleTests
{
    [Fact]
    public async Task Developer_console_exposes_operational_controls_and_lifecycle_states()
    {
        await using var simulation = SimulationHost.Create(new DeviceDefinition(new DeviceId("console"), "pump", new[]
        {
            new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0),
            new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false)
        }));
        await simulation.StartAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapIndustrialSimApi(simulation);
        app.MapIndustrialSimDeveloperConsole();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var html = await client.GetStringAsync("/");
        Assert.Contains("<div id=\"app\"></div>", html, StringComparison.Ordinal);
        var scriptPath = Regex.Match(html, "src=\"(?<path>/assets/[^\"]+\\.js)\"").Groups["path"].Value;
        Assert.NotEmpty(scriptPath);
        var script = await client.GetStringAsync(scriptPath);
        Assert.Contains("Industrial Device Simulation", script, StringComparison.Ordinal);
        Assert.Contains("Scenario control", script, StringComparison.Ordinal);
        Assert.Contains("Fault control", script, StringComparison.Ordinal);
        Assert.Contains("validation-error", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Developer_console_uses_scalable_workspace_layout()
    {
        await using var simulation = SimulationHost.Create(new DeviceDefinition(new DeviceId("console"), "pump", new[]
        {
            new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)
        }));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapIndustrialSimApi(simulation);
        app.MapIndustrialSimDeveloperConsole();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        var html = await client.GetStringAsync("/");

        var stylePath = Regex.Match(html, "href=\"(?<path>/assets/[^\"]+\\.css)\"").Groups["path"].Value;
        Assert.NotEmpty(stylePath);
        var style = await client.GetStringAsync(stylePath);
        Assert.Contains(".workspace-sidebar", style, StringComparison.Ordinal);
        Assert.Contains(".workspace-header", style, StringComparison.Ordinal);
        Assert.Contains(".command-center", style, StringComparison.Ordinal);
        Assert.Contains("@media", style, StringComparison.Ordinal);
    }
}
