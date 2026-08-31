using IndustrialSim.Configuration;
using IndustrialSim.Scenarios;

namespace IndustrialSim.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? error = null)
    {
        output ??= Console.Out; error ??= Console.Error;
        try
        {
            if (args.Length < 2) { await error.WriteLineAsync("Usage: industrial-sim validate <file> | run <file> | scenario run <file>"); return 2; }
            if (args[0].Equals("validate", StringComparison.OrdinalIgnoreCase)) { new YamlConfigurationLoader().Load(await File.ReadAllTextAsync(args[1])); await output.WriteLineAsync("Configuration valid."); return 0; }
            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)) { new YamlConfigurationLoader().Load(await File.ReadAllTextAsync(args[1])); await output.WriteLineAsync("Runtime started."); return 0; }
            if (args.Length >= 3 && args[0].Equals("scenario", StringComparison.OrdinalIgnoreCase) && args[1].Equals("run", StringComparison.OrdinalIgnoreCase)) { var scenario = new ScenarioParser().Parse(await File.ReadAllTextAsync(args[2])); await output.WriteLineAsync($"Scenario '{scenario.Name}' loaded."); return 0; }
            await error.WriteLineAsync("Unknown command."); return 2;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { await error.WriteLineAsync($"Error: {ex.Message}"); return 1; }
    }
}
