namespace IndustrialSim.Web.Tests;

public class ConfigurationLoadingTests
{
    [Fact]
    public void Web_host_supports_external_device_configuration_path()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "IndustrialSim.Web", "Program.cs"));
        Assert.Contains("INDUSTRIALSIM_DEVICE_CONFIG", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_composition_loads_the_shared_yaml_host_and_rejects_a_missing_explicit_path()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, """
            device:
              id: web-device
              type: sensor
              datapoints:
                value: { type: double, initial: 1, access: readwrite }
            """);
        await using var host = await WebHostComposition.CreateAsync(path);
        Assert.Equal("web-device", host.Runtime.Definition.Id.Value);
        await Assert.ThrowsAsync<FileNotFoundException>(() => WebHostComposition.CreateAsync(path + ".missing"));
    }
}
