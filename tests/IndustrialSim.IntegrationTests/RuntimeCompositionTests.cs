using System.Net;
using System.Net.Sockets;
using IndustrialSim.Hosting;

namespace IndustrialSim.IntegrationTests;

public sealed class RuntimeCompositionTests
{
    [Fact]
    public async Task Shared_host_loads_one_yaml_runtime_and_starts_configured_adapters()
    {
        var opcPort = FreePort();
        var modbusPort = FreePort();
        var file = WriteTempConfig(opcPort, modbusPort);
        await using var host = await SimulationHost.LoadAsync(file, new SimulationHostOptions(Deterministic: true));

        Assert.Equal("configured-device", host.Runtime.Definition.Id.Value);
        Assert.Same(host.Runtime.State, host.State);
        Assert.NotNull(host.FaultManager);
        await host.StartAsync();
        Assert.True(host.Protocols["opcua"].IsRunning);
        Assert.True(host.Protocols["modbus"].IsRunning);

        host.RunScenario("""
            scenario:
              name: configured
              steps:
                - at: 0s
                  set:
                    device: configured-device
                    datapoint: speed
                    value: 42
            """);
        host.Tick(TimeSpan.Zero);
        Assert.Equal(42, host.State.Get(new("speed"))!.Value);

        await host.StopAsync();
        Assert.All(host.Protocols.Values, protocol => Assert.False(protocol.IsRunning));
    }

    [Fact]
    public async Task Shared_host_fails_fast_for_missing_invalid_and_cancelled_start()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => SimulationHost.LoadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yaml")));
        var invalid = Path.GetTempFileName();
        await File.WriteAllTextAsync(invalid, "device: {}");
        await Assert.ThrowsAsync<ArgumentException>(() => SimulationHost.LoadAsync(invalid));

        var valid = WriteTempConfig(null, null);
        await using var host = await SimulationHost.LoadAsync(valid, new SimulationHostOptions(Deterministic: true));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.StartAsync(cts.Token));
        Assert.False(host.IsRunning);
    }

    private static string WriteTempConfig(int? opcPort, int? modbusPort)
    {
        var protocols = opcPort.HasValue || modbusPort.HasValue
            ? $"""
              protocols:
                opcua:
                  enabled: {opcPort.HasValue.ToString().ToLowerInvariant()}
                  endpoint: "opc.tcp://127.0.0.1:{opcPort ?? 4840}"
                modbus:
                  enabled: {modbusPort.HasValue.ToString().ToLowerInvariant()}
                  port: {modbusPort ?? 5020}
                  mappings:
                    speed:
                      holdingRegister: 10
                      type: int32
                      access: readwrite
              """
            : string.Empty;
        var path = Path.Combine(Path.GetTempPath(), $"industrial-sim-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, $$"""
            device:
              id: configured-device
              type: pump
              datapoints:
                speed:
                  type: int32
                  initial: 0
                  access: readwrite
                alarm:
                  type: boolean
                  initial: false
                  access: read
              commands:
                start:
            {{protocols}}
            """);
        return path;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
