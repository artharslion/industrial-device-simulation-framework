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

    [Fact]
    public async Task Run_command_hosts_until_cancelled_and_reports_deterministic_options()
    {
        var config = Path.GetTempFileName();
        await File.WriteAllTextAsync(config, """
            device:
              id: cli-runtime
              type: sensor
              datapoints:
                value: { type: int32, initial: 1, access: readwrite }
            """);
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        var run = CliRunner.RunAsync(new[] { "run", config, "--deterministic", "--seed", "123" }, output, cancellationToken: cancellation.Token);
        await Task.Delay(100);
        Assert.False(run.IsCompleted);
        cancellation.Cancel();
        Assert.Equal(0, await run);
        Assert.Contains("Deterministic=True", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Seed=123", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_seed_returns_nonzero_with_actionable_error()
    {
        var config = Path.GetTempFileName();
        await File.WriteAllTextAsync(config, "device: { id: bad-seed, type: sensor, datapoints: { value: { type: int32, initial: 1 } } }");
        using var error = new StringWriter();
        Assert.NotEqual(0, await CliRunner.RunAsync(new[] { "run", config, "--deterministic", "--seed", "invalid", "--duration", "0" }, error: error));
        Assert.Contains("seed", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
