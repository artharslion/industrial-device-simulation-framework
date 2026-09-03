using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.Contains("Industrial Device Simulation", html, StringComparison.Ordinal);
        Assert.Contains("Scenario", html, StringComparison.Ordinal);
        Assert.Contains("Fault", html, StringComparison.Ordinal);
        Assert.Contains("Paused", html, StringComparison.Ordinal);
        Assert.Contains("validation-error", html, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML=", html, StringComparison.Ordinal);
        Assert.Contains("createElement", html, StringComparison.Ordinal);
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

        Assert.Contains("workspace-sidebar", html, StringComparison.Ordinal);
        Assert.Contains("workspace-header", html, StringComparison.Ordinal);
        Assert.Contains("command-center", html, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", html, StringComparison.Ordinal);
    }
}
