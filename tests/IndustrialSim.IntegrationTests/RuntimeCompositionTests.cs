using System.Net;
using System.Net.Sockets;
using IndustrialSim.Hosting;
using IndustrialSim.Faults;
using IndustrialSim.Protocols.Modbus;

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

    [Fact]
    public async Task Scenario_network_fault_uses_protocol_target_and_recovers_after_duration()
    {
        var file = WriteTempConfig(null, FreePort());
        await using var host = await SimulationHost.LoadAsync(file, new SimulationHostOptions(Deterministic: true));
        await host.StartAsync();
        host.RunScenario("""
            scenario:
              name: network
              steps:
                - at: 0s
                  fault:
                    type: network.timeout
                    protocol: modbus
                    duration: 1s
            """);
        host.Tick(TimeSpan.Zero);
        var adapter = Assert.IsType<ModbusAdapter>(host.Protocols["modbus"]);
        Assert.True(adapter.IsDisconnected);
        host.Tick(TimeSpan.FromSeconds(1));
        Assert.False(adapter.IsDisconnected);
    }

    [Fact]
    public async Task Yaml_pump_commands_and_time_drive_the_runtime_behavior_model()
    {
        var file = WriteTempConfig(null, null);
        await using var host = await SimulationHost.LoadAsync(file, new SimulationHostOptions(Deterministic: true));
        await host.StartAsync();
        await host.Runtime.InvokeCommandAsync("start");
        host.Tick(TimeSpan.FromSeconds(5));
        Assert.True(Convert.ToBoolean(host.Runtime.Read("running")!.Value));
        Assert.True(Convert.ToInt32(host.Runtime.Read("speed")!.Value) > 0);
    }

    [Fact]
    public async Task Yaml_motor_start_and_tick_drive_speed_current_and_temperature()
    {
        var file = WriteTempBuiltInDeviceConfig("motor");
        await using var host = await SimulationHost.LoadAsync(file, new SimulationHostOptions(Deterministic: true));
        await host.StartAsync();

        await host.Runtime.InvokeCommandAsync("start");
        host.Tick(TimeSpan.FromSeconds(5));

        Assert.True(Convert.ToInt32(host.Runtime.Read("speed")!.Value) > 0);
        Assert.True(Convert.ToDouble(host.Runtime.Read("current")!.Value) > 0);
        Assert.True(Convert.ToDouble(host.Runtime.Read("temperature")!.Value) > 25d);
    }

    [Fact]
    public async Task Yaml_sensor_tick_and_reset_drive_value_and_quality()
    {
        var file = WriteTempBuiltInDeviceConfig("sensor");
        await using var host = await SimulationHost.LoadAsync(file, new SimulationHostOptions(Deterministic: true));
        await host.StartAsync();

        host.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(2d, Convert.ToDouble(host.Runtime.Read("value")!.Value));

        await host.Runtime.InvokeCommandAsync("reset");
        Assert.Equal(0d, Convert.ToDouble(host.Runtime.Read("value")!.Value));
        Assert.Equal("Good", host.Runtime.Read("quality")!.Value);
    }

    [Fact]
    public async Task Shared_host_rejects_scenario_references_to_another_device()
    {
        var file = WriteTempConfig(null, null);
        await using var host = await SimulationHost.LoadAsync(file, new SimulationHostOptions(Deterministic: true));
        await host.StartAsync();

        Assert.Throws<ArgumentException>(() => host.RunScenario("""
            scenario:
              name: invalid-reference
              steps:
                - at: 0s
                  set: { device: another-device, datapoint: speed, value: 10 }
            """));
        host.Tick(TimeSpan.Zero);
        Assert.Equal(0, host.Runtime.Read("speed")!.Value);
    }

    [Fact]
    public async Task Invalid_fault_does_not_enter_the_active_lifecycle()
    {
        var file = WriteTempConfig(null, null);
        await using var host = await SimulationHost.LoadAsync(file, new SimulationHostOptions(Deterministic: true));
        await host.StartAsync();
        var fault = new FaultSpec(
            "invalid-data-fault",
            FaultCategory.Data,
            host.Runtime.Definition.Id.Value,
            "missing",
            host.Engine.CurrentTime.Elapsed,
            Type: "stale");

        Assert.Throws<ArgumentException>(() => host.ActivateFault(fault));
        Assert.Empty(host.FaultManager.ActiveFaults);
    }

    [Fact]
    public async Task Stale_fault_remains_active_across_behavior_ticks_and_recovery_keeps_latest_state()
    {
        var file = WriteTempConfig(null, null);
        await using var host = await SimulationHost.LoadAsync(file, new SimulationHostOptions(Deterministic: true));
        await host.StartAsync();
        await host.Runtime.InvokeCommandAsync("start");
        host.Tick(TimeSpan.FromSeconds(1));
        var frozen = Convert.ToDouble(host.Runtime.Read("temperature")!.Value);
        var stale = new FaultSpec(
            "stale-temperature",
            FaultCategory.Data,
            host.Runtime.Definition.Id.Value,
            "temperature",
            host.Engine.CurrentTime.Elapsed,
            Type: "stale");

        host.ActivateFault(stale);
        host.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(frozen, Convert.ToDouble(host.Runtime.Read("temperature")!.Value));
        Assert.True(host.RecoverFault(stale.Id));
        Assert.True(Convert.ToDouble(host.Runtime.Read("temperature")!.Value) > frozen);
        host.Tick(TimeSpan.FromSeconds(1));
        Assert.True(Convert.ToDouble(host.Runtime.Read("temperature")!.Value) > frozen);

        var spike = new FaultSpec(
            "speed-spike",
            FaultCategory.Data,
            host.Runtime.Definition.Id.Value,
            "speed",
            host.Engine.CurrentTime.Elapsed,
            Type: "spike",
            Metadata: new Dictionary<string, string> { ["parameter"] = "25" });
        host.ActivateFault(spike);
        host.State.SetInternal(new("speed"), 500, host.Engine.CurrentTime);
        Assert.True(host.RecoverFault(spike.Id));
        Assert.Equal(500, host.Runtime.Read("speed")!.Value);
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
                running:
                  type: boolean
                  initial: false
                  access: read
                temperature:
                  type: double
                  initial: 25
                  access: read
                pressure:
                  type: double
                  initial: 0
                  access: read
              commands:
                start:
            {{protocols}}
            """);
        return path;
    }

    private static string WriteTempBuiltInDeviceConfig(string type)
    {
        var yaml = type switch
        {
            "motor" => """
                device:
                  id: motor-001
                  type: motor
                  datapoints:
                    speed: { type: int32, initial: 0, access: readwrite }
                    temperature: { type: double, initial: 25, access: read }
                    current: { type: double, initial: 0, access: read }
                    running: { type: boolean, initial: false, access: read }
                    alarm: { type: boolean, initial: false, access: read }
                  commands:
                    start:
                    stop:
                """,
            "sensor" => """
                device:
                  id: sensor-001
                  type: sensor
                  datapoints:
                    value: { type: double, initial: 0, access: readwrite }
                    quality: { type: string, initial: Good, access: read }
                  commands:
                    reset:
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported built-in device type.")
        };
        var path = Path.Combine(Path.GetTempPath(), $"industrial-sim-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
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
