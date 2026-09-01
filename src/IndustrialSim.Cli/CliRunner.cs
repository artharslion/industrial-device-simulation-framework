using IndustrialSim.Configuration;
using IndustrialSim.Hosting;

namespace IndustrialSim.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter? output = null,
        TextWriter? error = null,
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
                await output.WriteLineAsync("Configuration valid.");
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                await using var host = await SimulationHost.LoadAsync(args[1], cancellationToken);
                await host.StartAsync(cancellationToken);
                var duration = ParseDuration(args);
                if (duration.HasValue) host.Tick(duration.Value);
                await output.WriteLineAsync($"Runtime started at {host.Engine.CurrentTime.Elapsed}. OPC UA={IsRunning(host, "opcua")}, Modbus={IsRunning(host, "modbus")}.");
                return 0;
            }

            if (args.Length >= 3 && args[0].Equals("scenario", StringComparison.OrdinalIgnoreCase) && args[1].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                var configPath = Option(args, "--config") ?? throw new ArgumentException("scenario run requires --config <device-file>.");
                await using var host = await SimulationHost.LoadAsync(configPath, cancellationToken);
                await host.StartAsync(cancellationToken);
                host.RunScenario(await File.ReadAllTextAsync(args[2], cancellationToken));
                host.Tick(ParseDuration(args) ?? TimeSpan.Zero);
                var state = string.Join(", ", host.State.Snapshot().Select(item => $"{item.Key}={item.Value?.Value}"));
                await output.WriteLineAsync($"Scenario '{host.ActiveScenarioName}' executed. {state}");
                return 0;
            }

            await error.WriteLineAsync("Unknown command.");
            return 2;
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("Operation cancelled.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
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
}
