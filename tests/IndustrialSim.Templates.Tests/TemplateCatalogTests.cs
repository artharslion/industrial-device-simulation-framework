using IndustrialSim.Core.Domain;
using IndustrialSim.Templates;
using IndustrialSim.Application.Templates;
using IndustrialSim.Hosting;

namespace IndustrialSim.Templates.Tests;

public sealed class TemplateCatalogTests
{
    [Fact]
    public void Validates_versions_datapoints_and_mapping_references()
    {
        var template = Template("1.2.0") with
        {
            DataPoints =
            [
                new("speed", "Double", "ReadWrite", 0, "rpm", null),
                new("speed", "Double", "Read", 0, null, null)
            ]
        };

        var duplicate = Assert.Throws<TemplateValidationException>(() => TemplateCatalog.Validate(template, []));
        Assert.Contains("Duplicate datapoint", duplicate.Message);

        var mapping = new ProtocolMappingProfile("pump", "1.2.0", "modbus", "holding-registers",
            [new("missing", "40001", "Float", "BigEndian", "HighLow")]);
        var missing = Assert.Throws<TemplateValidationException>(() => TemplateCatalog.Validate(Template("1.2.0"), [mapping]));
        Assert.Contains("missing", missing.Message);

        var behavior = Assert.Throws<TemplateValidationException>(() => TemplateCatalog.Validate(Template("1.2.0") with { BehaviorJson = "{" }, []));
        Assert.Contains("Behavior JSON", behavior.Message);
    }

    [Fact]
    public void Instantiates_a_new_logical_device_without_protocol_addresses()
    {
        var definition = TemplateCatalog.Instantiate(Template("1.0.0"), "line-7-pump");

        Assert.Equal(new DeviceId("line-7-pump"), definition.Id);
        Assert.Equal("Pump", definition.Type);
        Assert.Equal("speed", Assert.Single(definition.DataPoints).Name);
    }

    [Fact]
    public void Instantiates_explicit_behavior_metadata_from_template_json()
    {
        var definition = TemplateCatalog.Instantiate(Template("1.0.0") with
        {
            BehaviorJson = """{"profile":"pump","parameters":{"ratedSpeed":1200}}"""
        }, "configured-pump");

        Assert.Equal("pump", definition.Behavior!.Profile);
        Assert.Equal(1200, definition.Behavior.Parameters["ratedSpeed"]);
    }

    [Fact]
    public void Imports_exports_and_searches_immutable_versions()
    {
        var templates = new[] { Template("1.0.0"), Template("2.0.0") with { Tags = ["water", "critical"] } };
        var json = TemplateCatalog.Export(templates[1], []);
        var imported = TemplateCatalog.Import(json);
        var results = TemplateCatalog.Search(templates, "pump", "critical");

        Assert.Equal("2.0.0", imported.Template.Version);
        Assert.Equal("pump", imported.Template.Id);
        Assert.Single(results);
        Assert.Equal("2.0.0", results[0].Version);
    }

    [Fact]
    public void Template_mapping_profile_compiles_to_real_modbus_launch_configuration()
    {
        var template = Template("1.0.0") with
        {
            DataPoints = [new TemplateDataPoint("speed", "Int32", "ReadWrite", 0)]
        };
        var profile = new ProtocolMappingProfile("pump", "1.0.0", "modbus", "holding",
            [new ProtocolMappingEntry("speed", "40101", "int32", "BigEndian", "HighLow")]);

        var launch = TemplateLaunchComposer.Compose(template, [profile], "mapped-pump", new SimulationHostOptions(true, 4),
            [new TemplateProtocolSelection("modbus", "holding", 15020)]);

        Assert.Equal(15020, launch.Modbus!.Port);
        Assert.Equal(100, launch.Modbus.Mappings.Single().Address);
        Assert.Equal("holding", launch.Source!.MappingProfiles!.Single().Name);
    }

    private static DeviceTemplateDocument Template(string version) => new(
        "pump", version, "Centrifugal Pump", "Pump", "A reusable pump definition", ["water"],
        [new TemplateDataPoint("speed", "Double", "ReadWrite", 0, "rpm", null)],
        ["start", "stop"], ["overheat"], "{}");
}
