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

    [Fact]
    public void Visual_modeling_wave_has_verified_acceptance_evidence()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var matrix = File.ReadAllText(Path.Combine(root, "docs", "PROTOFORGE_BASELINE_MATRIX.md"));

        Assert.Contains("TemplateCatalogTests`, `TemplatePersistenceTests`, and `VisualModelingApiTests` | Verified", matrix, StringComparison.Ordinal);
        Assert.Contains("ScenarioParserTests`, `VisualModelingApiTests`, and Vue editor tests | Verified", matrix, StringComparison.Ordinal);
        Assert.Contains("Vitest, typecheck, production build, and desktop/mobile browser evidence | Verified", matrix, StringComparison.Ordinal);
    }

    [Fact]
    public void Docker_deployment_uses_a_persistent_non_root_writable_sqlite_directory()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));
        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));

        Assert.Contains("/app/data", dockerfile, StringComparison.Ordinal);
        Assert.Contains("chown", dockerfile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConnectionStrings__IndustrialSim", compose, StringComparison.Ordinal);
        Assert.Contains("Data Source=/app/data/industrial-sim.db", compose, StringComparison.Ordinal);
        Assert.Contains("industrial-sim-data:/app/data", compose, StringComparison.Ordinal);
    }
}
