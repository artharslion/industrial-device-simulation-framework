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

    [Fact]
    public void Parses_reusable_single_target_scenario_without_device_ids()
    {
        const string yaml = """
            scenario:
              name: reusable-pump
              target:
                type: pump
              steps:
                - at: 0s
                  command: { name: start }
                - after: 1s
                  set: { datapoint: speed, value: 100 }
                - after: 1s
                  ramp: { datapoint: speed, from: 100, to: 1400, duration: 500ms }
                - when: { condition: "temperature > 80" }
                  fault: { type: overheat }
            """;

        var scenario = new ScenarioParser().Parse(yaml);

        Assert.Equal("pump", scenario.Target!.Type);
        Assert.Equal(TimeSpan.FromMilliseconds(500), Assert.IsType<RampAction>(scenario.Steps[2].Action).Duration);
        Assert.All(scenario.Steps, step =>
        {
            if (step.Trigger is WhenTrigger when) Assert.Empty(when.Device);
            if (step.Action is SetAction set) Assert.Empty(set.Device);
            if (step.Action is RampAction ramp) Assert.Empty(ramp.Device);
            if (step.Action is CommandAction command) Assert.Empty(command.Device);
            if (step.Action is FaultAction fault) Assert.Empty(fault.Device);
        });
    }

    [Fact]
    public void Targetless_device_action_requires_scenario_target_or_legacy_device_id()
    {
        const string yaml = """
            scenario:
              name: invalid
              steps:
                - at: 0s
                  command: { name: start }
            """;

        var error = Assert.Throws<ArgumentException>(() => new ScenarioParser().Parse(yaml));

        Assert.Contains("scenario.target", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
