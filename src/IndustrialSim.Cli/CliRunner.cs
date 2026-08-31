using IndustrialSim.Configuration;
using IndustrialSim.Scenarios;
using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.Engine;
using IndustrialSim.Runtime.Time;
using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Protocols.OpcUa;
using IndustrialSim.Protocols.Modbus;

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
            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                var loaded = new YamlConfigurationLoader().Load(await File.ReadAllTextAsync(args[1]));
                var runtime = new InMemoryDeviceRuntime(loaded.Device);
                var engine = new SimulationEngine(new DeterministicClock()); await engine.StartAsync();
                var opc = new OpcUaAdapter(); var modbus = new ModbusAdapter();
                if (loaded.Configuration.Protocols?.Opcua?.Enabled == true) { await opc.StartAsync(runtime, new ProtocolOptions(loaded.Configuration.Protocols.Opcua.Endpoint)); await opc.StartServerAsync(4840); }
                if (loaded.Configuration.Protocols?.Modbus?.Enabled == true) { modbus.Configure(loaded.ModbusMappings); await modbus.StartAsync(runtime, new ProtocolOptions(Port: loaded.Configuration.Protocols.Modbus.Port)); await modbus.StartServerAsync(loaded.Configuration.Protocols.Modbus.Port); }
                var duration = ParseDuration(args.Skip(2).ToArray()); if (duration > TimeSpan.Zero) engine.Tick(duration);
                await output.WriteLineAsync($"Runtime started at {engine.CurrentTime.Elapsed}. OPC UA={opc.IsRunning}, Modbus={modbus.IsRunning}."); await modbus.StopAsync(); await opc.StopAsync(); return 0;
            }
            if (args.Length >= 3 && args[0].Equals("scenario", StringComparison.OrdinalIgnoreCase) && args[1].Equals("run", StringComparison.OrdinalIgnoreCase)) { var scenario = new ScenarioParser().Parse(await File.ReadAllTextAsync(args[2])); await output.WriteLineAsync($"Scenario '{scenario.Name}' loaded."); return 0; }
            await error.WriteLineAsync("Unknown command."); return 2;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { await error.WriteLineAsync($"Error: {ex.Message}"); return 1; }
    }
    private static TimeSpan ParseDuration(string[] args)
    { var i = Array.IndexOf(args, "--duration"); return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out var seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero; }
}
