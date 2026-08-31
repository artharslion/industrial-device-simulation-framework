namespace IndustrialSim.Web.Tests;

public class ConfigurationLoadingTests
{
    [Fact]
    public void Web_host_supports_external_device_configuration_path()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "IndustrialSim.Web", "Program.cs"));
        Assert.Contains("INDUSTRIALSIM_DEVICE_CONFIG", source, StringComparison.Ordinal);
    }
}
