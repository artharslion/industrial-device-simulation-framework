using IndustrialSim.Hosting;
using IndustrialSim.Protocols.Modbus;
using System.Net;
using System.Net.Sockets;

namespace IndustrialSim.Web.Tests;

public class ConfigurationLoadingTests
{
    [Fact]
    public void Web_host_supports_external_device_configuration_path()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "IndustrialSim.Web", "Program.cs"));
        Assert.Contains("INDUSTRIALSIM_DEVICE_CONFIG", source, StringComparison.Ordinal);
        Assert.Contains("INDUSTRIALSIM_LOG_LEVEL", source, StringComparison.Ordinal);
        Assert.Contains("LogInformation", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_composition_loads_the_shared_yaml_host_and_rejects_a_missing_explicit_path()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, """
            device:
              id: web-device
              type: sensor
              datapoints:
                value: { type: double, initial: 1, access: readwrite }
            """);
        await using var host = await WebHostComposition.CreateAsync(path);
        Assert.Equal("web-device", host.Runtime.Definition.Id.Value);
        await Assert.ThrowsAsync<FileNotFoundException>(() => WebHostComposition.CreateAsync(path + ".missing"));
    }

    [Fact]
    public async Task Host_override_precedence_prefers_cli_then_environment_then_yaml()
    {
        var environment = new Dictionary<string, string?>
        {
            ["INDUSTRIALSIM_OPCUA_ENDPOINT"] = "opc.tcp://127.0.0.1:41001",
            ["INDUSTRIALSIM_MODBUS_PORT"] = "41002",
            ["INDUSTRIALSIM_WEB_PORT"] = "41003",
            ["INDUSTRIALSIM_LOG_LEVEL"] = "Debug"
        };
        var overrides = HostConfigurationOverrides.Resolve(
            cliOpcUaEndpoint: "opc.tcp://127.0.0.1:42001",
            cliModbusPort: "42002",
            environment: name => environment.GetValueOrDefault(name));
        Assert.Equal("opc.tcp://127.0.0.1:42001", overrides.OpcUaEndpoint);
        Assert.Equal(42002, overrides.ModbusPort);
        Assert.Equal(41003, overrides.WebPort);
        Assert.Equal("Debug", overrides.LogLevel);

        var yamlPort = FreePort();
        var environmentPort = FreePort();
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, $$"""
            device:
              id: override-device
              type: sensor
              datapoints:
                value: { type: uint16, initial: 1, access: readwrite }
            protocols:
              modbus:
                enabled: true
                port: {{yamlPort}}
                mappings:
                  value: { holdingRegister: 10, type: uint16, access: readwrite }
            """);
        var environmentOnly = HostConfigurationOverrides.Resolve(environment: name => name == "INDUSTRIALSIM_MODBUS_PORT" ? environmentPort.ToString() : null);
        await using var host = await WebHostComposition.CreateAsync(path, false, new SimulationHostOptions(Overrides: environmentOnly));
        await host.StartAsync();
        Assert.Equal(environmentPort, Assert.IsType<ModbusAdapter>(host.Protocols["modbus"]).Port);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
