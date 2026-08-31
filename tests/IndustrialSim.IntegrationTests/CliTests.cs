using IndustrialSim.Cli;

namespace IndustrialSim.IntegrationTests;

public class CliTests
{
    [Fact]
    public async Task Validate_command_returns_success_for_valid_file()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "devices", "pump.yaml");
        Assert.Equal(0, await CliRunner.RunAsync(new[] { "validate", file }));
    }
}
