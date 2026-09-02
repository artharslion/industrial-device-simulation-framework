# MVP Compliance Remediation Implementation Plan

**Completion status (2026-09-02):** Tasks 1-10 are implemented and committed.
The .NET Release gate and Compose configuration validation pass. Docker image
build/live container verification remains unexecuted only because the local
Docker Desktop daemon was unavailable; local Release-host HTTP and protocol
port smoke verification passed. Detailed evidence is recorded in
`docs/review-current-implementation.md`.

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Close the Phase 0-8 compliance gaps found in the full implementation review without expanding the v0.1 product scope.

**Architecture:** Preserve the protocol-independent Core and the single runtime-owned `StateStore`. Add validation before scheduling or activation, model persistent effects at the runtime boundary, and keep transport faults inside adapters. Use existing projects and abstractions; introduce only the minimal device/runtime services needed for Pump, Motor, and Sensor.

**Tech Stack:** .NET 10, C#, ASP.NET Core, YamlDotNet, OPC Foundation .NET, xUnit, Docker Compose.

---

### Task 1: Validate Scenario references and implement wait semantics

**Files:**
- Modify: `src/IndustrialSim.Scenarios/ScenarioRunner.cs`
- Modify: `src/IndustrialSim.Hosting/SimulationHost.cs`
- Test: `tests/IndustrialSim.Scenarios.Tests/ScenarioSchedulerTests.cs`
- Test: `tests/IndustrialSim.IntegrationTests/RuntimeCompositionTests.cs`

1. Add failing tests proving wrong device/datapoint/command references fail before scheduling and rejected state transitions are not reported as executed.
2. Add a failing sequence test proving `wait` offsets subsequent relative actions.
3. Run the focused tests and confirm failure.
4. Add a runtime-aware Scenario validator and minimal timeline offset handling for `wait`.
5. Run focused tests and commit `fix: validate scenario execution contracts`.

### Task 2: Make Fault activation transactional and Data Faults persistent

**Files:**
- Modify: `src/IndustrialSim.Faults/Faults.cs`
- Modify: `src/IndustrialSim.Hosting/SimulationHost.cs`
- Test: `tests/IndustrialSim.Faults.Tests/`
- Test: `tests/IndustrialSim.IntegrationTests/RuntimeCompositionTests.cs`

1. Add failing tests for invalid targets/types leaving no active fault.
2. Add failing tests proving stale/freeze survive behavior ticks and recovery does not restore obsolete state.
3. Implement pre-activation validation and active data-fault projection/interception.
4. Run focused tests and commit `fix: enforce persistent transactional faults`.

### Task 3: Make real-time lifecycle control simulation time

**Files:**
- Modify: `src/IndustrialSim.Runtime/Time/SimulationClock.cs`
- Modify: `src/IndustrialSim.Runtime/Engine/SimulationEngine.cs`
- Test: `tests/IndustrialSim.Runtime.Tests/SimulationClockTests.cs`
- Test: `tests/IndustrialSim.Runtime.Tests/SimulationEngineTests.cs`

1. Add failing pause/stop/resume clock tests.
2. Implement a lifecycle-aware real-time clock controlled by the engine.
3. Run focused tests and commit `fix: pause real time simulation clock`.

### Task 4: Implement Motor and Sensor built-in devices

**Files:**
- Create: `src/IndustrialSim.Devices/Motor/Motor.cs`
- Create: `src/IndustrialSim.Devices/Sensor/Sensor.cs`
- Modify: `src/IndustrialSim.Hosting/SimulationHost.cs`
- Create: `examples/devices/motor.yaml`
- Create: `examples/devices/sensor.yaml`
- Test: `tests/IndustrialSim.Runtime.Tests/`
- Test: `tests/IndustrialSim.Configuration.Tests/YamlConfigurationTests.cs`

1. Add failing deterministic behavior/command tests for both device types.
2. Implement minimal documented models and host composition.
3. Add canonical YAML examples, run focused tests, and commit `feat: add motor and sensor devices`.

### Task 5: Enforce real transport Network Faults

**Files:**
- Modify: OPC UA and Modbus adapter/server files
- Test: protocol projects and integration tests

1. Add failing real-client disconnect, timeout, and latency tests for both protocols.
2. Route OPC UA service access through transport-fault state and distinguish Modbus disconnect/timeout behavior.
3. Verify simulation continues, run focused tests, and commit `fix: enforce network faults on wire transports`.

### Task 6: Complete configuration validation and atomic Modbus writes

**Files:**
- Modify: configuration loader/validator, runtime state transitions, and Modbus adapter
- Test: configuration and Modbus test projects

1. Add failing tests for missing mapping targets, incompatible access, invalid ports/endpoints, and late multi-write rejection.
2. Validate mappings against `DeviceDefinition` before host startup.
3. Add one atomic StateStore batch transition used by function 16.
4. Run focused tests and commit `fix: validate and atomically apply modbus mappings`.

### Task 7: Complete ordered events and access control

**Files:**
- Modify: Core events, StateStore, runtime contract, hosting
- Test: Core, Runtime, Integration tests

1. Add failing concurrent observer-order and write-only read tests.
2. Serialize transition publication in commit order and distinguish internal/external reads.
3. Emit command and device lifecycle events.
4. Run focused tests and commit `fix: order runtime events and enforce access`.

### Task 8: Complete OPC UA scalar/event mapping

**Files:**
- Modify: `src/IndustrialSim.Protocols.OpcUa/IndustrialOpcUaServer.cs`
- Test: `tests/IndustrialSim.Protocols.OpcUa.Tests/`

1. Add failing SByte/Byte and runtime-event tests.
2. Map all v0.1 scalar types and expose supported runtime events.
3. Run focused tests and commit `fix: complete opc ua runtime mappings`.

### Task 9: Complete host, Web, Docker, and cleanup contracts

**Files:**
- Modify: CLI/Web composition, Web page rendering, Docker Compose, docs
- Delete: placeholder `Class1.cs` files
- Test: Web and integration projects

1. Add failing tests for YAML/environment port precedence and safe UI rendering.
2. Wire structured logging and configuration overrides.
3. Mount documented scenario configuration in Compose and remove template residue.
4. Run focused tests and commit `fix: complete host and release contracts`.

### Task 10: Release verification

1. Run restore, Release build, full tests, `git diff --check`, and Compose validation.
2. Run Docker build/live endpoint verification when the daemon is available; otherwise retain the explicit environment limitation.
3. Update review/implementation notes and commit `docs: record mvp compliance verification`.
