using IndustrialSim.Scenarios;

namespace IndustrialSim.Scenarios.Tests;

public class ScenarioParserTests
{
    [Fact]
    public void Parses_all_supported_triggers_and_actions()
    {
        const string yaml = """
        scenario:
          name: demo
          steps:
            - at: 0s
              set: { device: pump-001, datapoint: speed, value: 10 }
            - after: 1s
              ramp: { device: pump-001, datapoint: speed, from: 10, to: 100, duration: 5s }
            - every: 2s
              command: { device: pump-001, name: start }
            - when: { device: pump-001, condition: "temperature > 90" }
              fault: { device: pump-001, type: overheat }
            - after: 3s
              wait: { duration: 1s }
        """;

        var scenario = new ScenarioParser().Parse(yaml);

        Assert.Equal("demo", scenario.Name);
        Assert.Equal(5, scenario.Steps.Count);
        Assert.IsType<AtTrigger>(scenario.Steps[0].Trigger);
        Assert.IsType<SetAction>(scenario.Steps[0].Action);
        Assert.IsType<RampAction>(scenario.Steps[1].Action);
        Assert.IsType<CommandAction>(scenario.Steps[2].Action);
        Assert.IsType<WhenTrigger>(scenario.Steps[3].Trigger);
        Assert.IsType<FaultAction>(scenario.Steps[3].Action);
        Assert.IsType<WaitAction>(scenario.Steps[4].Action);
    }

    [Fact]
    public void Rejects_protocol_address_and_malformed_references()
    {
        const string yaml = """
        scenario:
          name: bad
          steps:
            - at: 0s
              set: { address: 40001, datapoint: speed, value: 1 }
        """;
        var ex = Assert.Throws<ArgumentException>(() => new ScenarioParser().Parse(yaml));
        Assert.Contains("device", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parses_documented_data_and_network_fault_shapes()
    {
        const string yaml = """
            scenario:
              name: faults
              steps:
                - at: 0s
                  fault:
                    type: stale
                    target: { device: pump-001, datapoint: temperature }
                    duration: 2s
                - after: 1s
                  fault:
                    type: network.timeout
                    protocol: modbus
                    duration: 3s
            """;
        var scenario = new ScenarioParser().Parse(yaml);
        var data = Assert.IsType<FaultAction>(scenario.Steps[0].Action);
        Assert.Equal("temperature", data.DataPoint);
        Assert.Equal(TimeSpan.FromSeconds(2), data.Duration);
        var network = Assert.IsType<FaultAction>(scenario.Steps[1].Action);
        Assert.Equal("modbus", network.Protocol);
        Assert.Equal(TimeSpan.FromSeconds(3), network.Duration);
    }
}
