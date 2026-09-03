namespace IndustrialSim.IntegrationTests;

public sealed class DocumentationContractTests
{
    [Fact]
    public void Protoforge_baseline_matrix_covers_capabilities_and_protocols()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var matrix = File.ReadAllText(Path.Combine(root, "docs", "PROTOFORGE_BASELINE_MATRIX.md"));

        string[] capabilities =
        [
            "devices", "protocols", "templates", "scenarios", "testing", "forwarding",
            "recording", "webhooks", "metrics", "authentication", "settings", "sdk"
        ];
        string[] protocols =
        [
            "Modbus TCP", "Modbus RTU", "OPC UA", "MQTT", "HTTP", "GB28181", "BACnet",
            "Siemens S7", "Mitsubishi MC", "Omron FINS", "EtherNet/IP", "OPC DA",
            "FANUC FOCAS", "MTConnect", "Mettler-Toledo"
        ];

        foreach (var capability in capabilities)
            Assert.Contains(capability, matrix, StringComparison.OrdinalIgnoreCase);
        foreach (var protocol in protocols)
            Assert.Contains(protocol, matrix, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Owner module", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Target wave", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Acceptance evidence", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Status", matrix, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wave_two_is_limited_to_visual_modeling_and_the_multi_page_console()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var specification = File.ReadAllText(Path.Combine(root, "docs", "PROJECT_SPEC.md"));
        var plan = File.ReadAllText(Path.Combine(root, "docs", "plans", "2026-09-02-protoforge-baseline-implementation-plan.md"));

        Assert.Contains("visual device template", specification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("graphical scenario", specification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("multi-page Vue", specification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local template catalog", plan, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TemplateMarketplaceTests", plan, StringComparison.Ordinal);
    }
}
