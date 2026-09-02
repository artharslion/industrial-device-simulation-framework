using IndustrialSim.Configuration;
using IndustrialSim.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialSim.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter? output = null,
        TextWriter? error = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        try
        {
            if (args.Length < 2)
            {
                await error.WriteLineAsync("Usage: industrial-sim validate <file> | run <file> [--duration seconds] | scenario run <file> --config <device-file> [--duration seconds]");
                return 2;
            }

            if (args[0].Equals("validate", StringComparison.OrdinalIgnoreCase))
            {
                new YamlConfigurationLoader().Load(await File.ReadAllTextAsync(args[1], cancellationToken));
                logger?.LogInformation("Validated industrial simulation configuration {ConfigurationPath}", args[1]);
                await output.WriteLineAsync("Configuration valid.");
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                var options = ParseHostOptions(args);
                logger?.LogInformation("Starting industrial simulation from {ConfigurationPath} with deterministic mode {Deterministic} and seed {Seed}", args[1], options.Deterministic, options.Seed);
                await output.WriteLineAsync($"Starting runtime. Deterministic={options.Deterministic}, Seed={options.Seed}.");
                await using var host = await SimulationHost.LoadAsync(args[1], options, cancellationToken);
                await host.StartAsync(cancellationToken);
                var duration = ParseDuration(args);
                await output.WriteLineAsync($"Runtime started at {host.Engine.CurrentTime.Elapsed}. Deterministic={host.IsDeterministic}, Seed={host.Seed}, OPC UA={IsRunning(host, "opcua")}, Modbus={IsRunning(host, "modbus")}.");
                await RunForAsync(host, duration, cancellationToken);
                logger?.LogInformation("Industrial simulation for device {DeviceId} completed", host.Runtime.Definition.Id.Value);
                return 0;
            }

            if (args.Length >= 3 && args[0].Equals("scenario", StringComparison.OrdinalIgnoreCase) && args[1].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                var configPath = Option(args, "--config") ?? throw new ArgumentException("scenario run requires --config <device-file>.");
                var options = ParseHostOptions(args);
                await using var host = await SimulationHost.LoadAsync(configPath, options, cancellationToken);
                await host.StartAsync(cancellationToken);
                host.RunScenario(await File.ReadAllTextAsync(args[2], cancellationToken));
                logger?.LogInformation("Running scenario {ScenarioPath} for device {DeviceId}", args[2], host.Runtime.Definition.Id.Value);
                var duration = ParseDuration(args);
                if (duration.HasValue) await RunForAsync(host, duration, cancellationToken);
                else await RunForAsync(host, null, cancellationToken);
                var state = string.Join(", ", host.State.Snapshot().Select(item => $"{item.Key}={item.Value?.Value}"));
                await output.WriteLineAsync($"Scenario '{host.ActiveScenarioName}' executed. {state}");
                return 0;
            }

            await error.WriteLineAsync("Unknown command.");
            return 2;
        }
        catch (OperationCanceledException)
        {
            logger?.LogInformation("Industrial simulation operation was cancelled");
            await output.WriteLineAsync("Operation cancelled.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
            logger?.LogError(exception, "Industrial simulation command failed");
            await error.WriteLineAsync($"Error: {exception.Message}");
            return 1;
        }
    }

    private static bool IsRunning(SimulationHost host, string protocol) => host.Protocols.TryGetValue(protocol, out var adapter) && adapter.IsRunning;

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, argument => argument.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static TimeSpan? ParseDuration(string[] args)
    {
        var value = Option(args, "--duration");
        if (value is null) return null;
        if (!double.TryParse(value, out var seconds) || seconds < 0) throw new ArgumentException("--duration must be a non-negative number of seconds.");
        return TimeSpan.FromSeconds(seconds);
    }

    private static SimulationHostOptions ParseHostOptions(string[] args)
    {
        var clock = Option(args, "--clock");
        if (clock is not null && !clock.Equals("deterministic", StringComparison.OrdinalIgnoreCase) && !clock.Equals("realtime", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--clock must be 'deterministic' or 'realtime'.");
        var deterministic = args.Any(argument => argument.Equals("--deterministic", StringComparison.OrdinalIgnoreCase)) || clock?.Equals("deterministic", StringComparison.OrdinalIgnoreCase) == true;
        var seedText = Option(args, "--seed");
        if (seedText is not null && !int.TryParse(seedText, out _)) throw new ArgumentException("--seed must be a 32-bit integer.");
        var seed = seedText is null ? 0 : int.Parse(seedText);
        var overrides = HostConfigurationOverrides.Resolve(
            Option(args, "--opcua-endpoint"),
            Option(args, "--modbus-port"),
            cliLogLevel: Option(args, "--log-level"));
        return new SimulationHostOptions(deterministic, seed, overrides);
    }

    private static async Task RunForAsync(SimulationHost host, TimeSpan? duration, CancellationToken cancellationToken)
    {
        if (duration is null)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }
        if (host.IsDeterministic) host.Tick(duration.Value);
        else if (duration > TimeSpan.Zero) await Task.Delay(duration.Value, cancellationToken);
        host.Update();
    }
}
