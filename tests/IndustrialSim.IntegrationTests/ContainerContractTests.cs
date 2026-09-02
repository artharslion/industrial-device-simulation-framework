namespace IndustrialSim.IntegrationTests;

public sealed class ContainerContractTests
{
    [Fact]
    public void Container_contract_publishes_web_host_and_exposes_mvp_ports()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));
        Assert.Contains("IndustrialSim.Web.csproj", dockerfile, StringComparison.Ordinal);
        Assert.Contains("EXPOSE 4840 5020 8080", dockerfile, StringComparison.Ordinal);
        Assert.Contains("INDUSTRIALSIM_DEVICE_CONFIG", dockerfile, StringComparison.Ordinal);
        Assert.Contains("IndustrialSim.Web.dll", dockerfile, StringComparison.Ordinal);

        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));
        Assert.Contains("4840:4840", compose, StringComparison.Ordinal);
        Assert.Contains("5020:5020", compose, StringComparison.Ordinal);
        Assert.Contains("8080:8080", compose, StringComparison.Ordinal);
        Assert.Contains("./examples/devices/pump.yaml:/app/config/device.yaml:ro", compose, StringComparison.Ordinal);
        Assert.Contains("./examples/scenarios:/app/config/scenarios:ro", compose, StringComparison.Ordinal);

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "src"), "Class1.cs", SearchOption.AllDirectories));
    }
}
