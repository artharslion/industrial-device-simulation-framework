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
    public void Release_evidence_gate_defines_ci_server_integration_and_image_publication()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var specification = File.ReadAllText(Path.Combine(root, "docs", "PROJECT_SPEC.md"));
        var matrix = File.ReadAllText(Path.Combine(root, "docs", "PROTOFORGE_BASELINE_MATRIX.md"));
        var comparison = File.ReadAllText(Path.Combine(root, "docs", "PROTOFORGE_COMPARISON.md"));
        var releasePlan = File.ReadAllText(Path.Combine(root, "docs", "plans", "2026-09-14-ci-release-evidence.md"));
        var implementationNotes = File.ReadAllText(Path.Combine(root, "docs", "IMPLEMENTATION_NOTES.md"));

        Assert.Contains("Wave 2.5: public-repository CI", specification, StringComparison.Ordinal);
        Assert.Contains("Microsoft.AspNetCore.Mvc.Testing", matrix, StringComparison.Ordinal);
        Assert.Contains("Docker Hub", matrix, StringComparison.Ordinal);
        Assert.Contains("| release evidence: CI, server integration, container smoke, and image publication | Delivery | 2.5 |", matrix, StringComparison.Ordinal);
        Assert.Contains("Docker Hub run `34802093256` / `ci-smoke` digest | Verified |", matrix, StringComparison.Ordinal);
        Assert.Contains("GitHub Actions", comparison, StringComparison.Ordinal);
        Assert.DoesNotContain("仓库当前没有 GitHub Actions workflow", comparison, StringComparison.Ordinal);
        Assert.Contains("34801843690", comparison, StringComparison.Ordinal);
        Assert.Contains("34802093256", releasePlan, StringComparison.Ordinal);
        Assert.Contains("sha256:3b90a83c8631", releasePlan, StringComparison.Ordinal);
        Assert.Contains("Docker Desktop Linux daemon is available", implementationNotes, StringComparison.Ordinal);
        Assert.Contains("14b4e35", comparison, StringComparison.Ordinal);
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

    [Fact]
    public void Wave_three_one_plan_preserves_runtime_authority_and_defines_observability_contracts()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var plan = File.ReadAllText(Path.Combine(root, "docs", "plans", "2026-09-14-wave-3-1-observability.md"));
        var adr = File.ReadAllText(Path.Combine(root, "docs", "adr", "0007-observability-boundary.md"));

        string[] requiredPlanTerms =
        [
            "StateStore remains the only live-state authority",
            "bounded ingress channel", "TryWrite", "dropped-event",
            "industrial_simulation_ticks_total", "industrial_stream_events_dropped_total",
            "/health/live", "/health/ready", "/metrics",
            "trace correlation", "secret redaction", "simulation/runtime isolation"
        ];

        foreach (var term in requiredPlanTerms)
            Assert.Contains(term, plan, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("observer", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not own or mutate device state", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SQLite", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("batch", adr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wave_three_one_observability_gate_records_executed_acceptance_evidence()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var matrix = File.ReadAllText(Path.Combine(root, "docs", "PROTOFORGE_BASELINE_MATRIX.md"));
        var comparison = File.ReadAllText(Path.Combine(root, "docs", "PROTOFORGE_COMPARISON.md"));
        var implementationNotes = File.ReadAllText(Path.Combine(root, "docs", "IMPLEMENTATION_NOTES.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var plan = File.ReadAllText(Path.Combine(root, "docs", "plans", "2026-09-14-wave-3-1-observability.md"));

        Assert.Contains("| metrics: health, Prometheus, and tracing | Observability | 3 |", matrix, StringComparison.Ordinal);
        Assert.Contains("TraceRedactionTests`; source and Docker endpoint checks | Verified |", matrix, StringComparison.Ordinal);
        Assert.Contains("Accepted on 2026-09-14 through `5fe021e`", matrix, StringComparison.Ordinal);
        Assert.Contains("208 .NET tests", matrix, StringComparison.Ordinal);
        Assert.Contains("Wave 3.1 Observability Gate 已关闭", comparison, StringComparison.Ordinal);
        Assert.DoesNotContain("尚未完成 liveness/readiness", comparison, StringComparison.Ordinal);
        Assert.Contains("Wave 3.2 User Testing", comparison, StringComparison.Ordinal);
        Assert.Contains("sha256:a6ed5bc949db", implementationNotes, StringComparison.Ordinal);
        Assert.Contains("`/health/live`", readme, StringComparison.Ordinal);
        Assert.Contains("`/health/ready`", readme, StringComparison.Ordinal);
        Assert.Contains("`/metrics`", readme, StringComparison.Ordinal);
        Assert.Contains("Status (2026-09-14):** Completed", plan, StringComparison.Ordinal);
        Assert.Contains("No Wave 3.2 work is included", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_opcua_design_preserves_state_authority_and_device_isolation()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var design = File.ReadAllText(Path.Combine(root, "docs", "plans", "2026-09-15-shared-opc-ua-server-design.md"));

        string[] requiredTerms =
        [
            "process-level pool keyed by normalized endpoint",
            "StateStore",
            "owns live state",
            "Objects/",
            "IndustrialSim/",
            "Devices/",
            "first successful registration",
            "final member",
            "ApplicationUri",
            "BadNotConnected",
            "BadTimeout",
            "same normalized endpoint",
            "Third-party client",
            "interoperability is not claimed"
        ];

        foreach (var term in requiredTerms)
            Assert.Contains(term, design, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("### A. One fixed global server and endpoint", design, StringComparison.Ordinal);
        Assert.Contains("### B. A process-level pool keyed by normalized endpoint", design, StringComparison.Ordinal);
        Assert.Contains("### C. Per-device servers behind a proxy or port forwarder", design, StringComparison.Ordinal);
        Assert.Contains("does not persist continuous", design, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("live values", design, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("proxy process", design, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shared_opcua_plan_is_test_first_and_has_independent_commit_boundaries()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var plan = File.ReadAllText(Path.Combine(root, "docs", "plans", "2026-09-15-shared-opc-ua-server-implementation-plan.md"));

        string[] tasks =
        [
            "Task 1: Add normalized endpoint identity and shared server lifecycle",
            "Task 2: Add dynamic multi-device address space and runtime routing",
            "Task 3: Integrate SimulationHost and SimulationRegistry endpoint ownership",
            "Task 4: Isolate OPC UA Network Faults on shared endpoints",
            "Task 5: Update public contracts, examples, and acceptance evidence"
        ];
        string[] commits =
        [
            "feat: add shared opc ua server lifecycle",
            "feat: expose multiple devices through shared opc ua address space",
            "refactor: integrate simulation hosts with shared opc ua server",
            "fix: isolate opc ua faults on shared endpoints",
            "docs: document shared opc ua endpoint hosting"
        ];

        foreach (var task in tasks)
            Assert.Contains(task, plan, StringComparison.Ordinal);
        foreach (var commit in commits)
            Assert.Contains(commit, plan, StringComparison.Ordinal);

        Assert.Contains("Write failing", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet test IndustrialSim.sln", plan, StringComparison.Ordinal);
        Assert.Contains("docker build", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rollback", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("third-party OPC UA interoperability", plan, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Public_documentation_defines_shared_opcua_endpoint_hosting_and_evidence_limits()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var specification = File.ReadAllText(Path.Combine(root, "docs", "PROJECT_SPEC.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var startup = File.ReadAllText(Path.Combine(root, "docs", "STARTUP_GUIDE.md"));
        var manual = File.ReadAllText(Path.Combine(root, "docs", "USER_MANUAL.md"));
        var matrix = File.ReadAllText(Path.Combine(root, "docs", "PROTOFORGE_BASELINE_MATRIX.md"));

        foreach (var document in new[] { specification, readme, startup, manual })
            Assert.Contains("shared OPC UA", document, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Objects/IndustrialSim/Devices", specification, StringComparison.Ordinal);
        Assert.Contains("BadNotConnected", specification, StringComparison.Ordinal);
        Assert.Contains("BadTimeout", specification, StringComparison.Ordinal);
        Assert.Contains("ApplicationUri", specification, StringComparison.Ordinal);
        Assert.Contains("one OPC UA port", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must reserve unique protocol ports", startup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shared endpoint", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repository OPC Foundation client", matrix, StringComparison.OrdinalIgnoreCase);
    }
}
