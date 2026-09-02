using IndustrialSim.Cli;
using Microsoft.Extensions.Logging;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
var configuredLogLevel = Option(args, "--log-level") ?? Environment.GetEnvironmentVariable("INDUSTRIALSIM_LOG_LEVEL") ?? "Information";
if (!Enum.TryParse<LogLevel>(configuredLogLevel, true, out var logLevel))
{
    await Console.Error.WriteLineAsync($"Error: log level '{configuredLogLevel}' is invalid.");
    return 1;
}
using var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(logLevel).AddSimpleConsole(options => options.SingleLine = true));
return await CliRunner.RunAsync(args, logger: loggerFactory.CreateLogger("IndustrialSim.Cli"), cancellationToken: cancellation.Token);

static string? Option(string[] values, string name)
{
    var index = Array.FindIndex(values, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
}
