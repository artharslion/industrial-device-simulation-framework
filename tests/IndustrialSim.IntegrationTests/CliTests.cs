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

    [Fact]
    public async Task Scenario_command_runs_against_a_yaml_composed_runtime()
    {
        var config = Path.GetTempFileName();
        await File.WriteAllTextAsync(config, """
            device:
              id: cli-device
              type: pump
              datapoints:
                speed: { type: int32, initial: 0, access: readwrite }
            """);
        var scenario = Path.GetTempFileName();
        await File.WriteAllTextAsync(scenario, """
            scenario:
              name: cli-scenario
              steps:
                - at: 0s
                  set: { device: cli-device, datapoint: speed, value: 12 }
            """);
        using var output = new StringWriter();
        var exitCode = await CliRunner.RunAsync(new[] { "scenario", "run", scenario, "--config", config, "--duration", "0" }, output);
        Assert.Equal(0, exitCode);
        Assert.Contains("cli-scenario", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("speed=12", output.ToString(), StringComparison.Ordinal);
    }
}
