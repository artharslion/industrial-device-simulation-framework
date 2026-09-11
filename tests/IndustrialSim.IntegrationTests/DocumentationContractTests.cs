namespace IndustrialSim.IntegrationTests;

public sealed class DocumentationContractTests
{
    [Fact]
    public void Documentation_separates_service_startup_from_the_ordered_operator_workflow()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var startupGuide = File.ReadAllText(Path.Combine(root, "docs", "STARTUP_GUIDE.md"));
        var chineseStartupGuide = File.ReadAllText(Path.Combine(root, "docs", "STARTUP_GUIDE.zh-CN.md"));
        var manual = File.ReadAllText(Path.Combine(root, "docs", "USER_MANUAL.md"));
        var chineseManual = File.ReadAllText(Path.Combine(root, "docs", "USER_MANUAL.zh-CN.md"));
        var startupScenario = File.ReadAllText(Path.Combine(root, "examples", "scenarios", "startup.yaml"));

        Assert.Contains("docs/STARTUP_GUIDE.md", readme, StringComparison.Ordinal);
        Assert.Contains("docs/STARTUP_GUIDE.zh-CN.md", readme, StringComparison.Ordinal);
        Assert.Contains("docs/USER_MANUAL.md", readme, StringComparison.Ordinal);
        Assert.Contains("docs/USER_MANUAL.zh-CN.md", readme, StringComparison.Ordinal);

        string[] startupSections =
        [
            "Start with Docker Compose", "Start from source", "Configuration overrides", "Authentication",
            "SQLite persistence", "Startup troubleshooting"
        ];
        foreach (var section in startupSections)
            Assert.Contains(section, startupGuide, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("使用 Docker Compose 启动", chineseStartupGuide, StringComparison.Ordinal);
        Assert.Contains("从源码启动", chineseStartupGuide, StringComparison.Ordinal);
        Assert.Contains("启动问题排查", chineseStartupGuide, StringComparison.Ordinal);

        string[] workflowSteps =
        [
            "Step 1: Open the console", "Step 2: Decide whether to use an existing device or create one",
            "Step 3: Start the device", "Step 4: Run the first scenario", "Step 6: Inject a fault",
            "Step 7: Verify the device", "Step 8: Use events", "Step 9: Save and reuse"
        ];
        foreach (var step in workflowSteps)
            Assert.Contains(step, manual, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("openapi/v1.json", manual, StringComparison.Ordinal);
        Assert.Contains("behavior profile", manual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reusable target type", manual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("## 2. Prerequisites", manual, StringComparison.Ordinal);
        Assert.DoesNotContain("## 3. Start with Docker Compose", manual, StringComparison.Ordinal);

        Assert.Contains("第一步：进入控制台", chineseManual, StringComparison.Ordinal);
        Assert.Contains("第二步：决定使用现有设备还是创建设备", chineseManual, StringComparison.Ordinal);
        Assert.Contains("第四步：运行第一个场景", chineseManual, StringComparison.Ordinal);
        Assert.Contains("第六步：注入故障并验证恢复", chineseManual, StringComparison.Ordinal);
        Assert.Contains("第八步：使用事件解释系统行为", chineseManual, StringComparison.Ordinal);
        Assert.Contains("behavior profile", chineseManual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("可复用目标类型", chineseManual, StringComparison.Ordinal);
        Assert.DoesNotContain("## 3. 使用 Docker Compose 启动", chineseManual, StringComparison.Ordinal);

        Assert.Contains("target:", startupScenario, StringComparison.Ordinal);
        Assert.Contains("type: pump", startupScenario, StringComparison.Ordinal);
        Assert.DoesNotContain("device: pump-001", startupScenario, StringComparison.Ordinal);
    }

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
    public void Specification_defines_safe_complete_device_launch_and_restore()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var specification = File.ReadAllText(Path.Combine(root, "docs", "PROJECT_SPEC.md"));

        Assert.Contains("versioned launch document", specification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("protocol port alone", specification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("implicit register allocation", specification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AutoStartDesiredRunning=false", specification, StringComparison.Ordinal);
        Assert.Contains("adapter startup failures", specification, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Visual_modeling_wave_has_verified_acceptance_evidence()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var matrix = File.ReadAllText(Path.Combine(root, "docs", "PROTOFORGE_BASELINE_MATRIX.md"));

        Assert.Contains("TemplateCatalogTests`, `TemplatePersistenceTests`, `VisualModelingApiTests`, and real-client template-instance access | Verified", matrix, StringComparison.Ordinal);
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
