using IndustrialSim.Cli;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
return await CliRunner.RunAsync(args, cancellationToken: cancellation.Token);
