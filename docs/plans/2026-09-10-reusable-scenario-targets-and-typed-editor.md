# Reusable Scenario Targets and Typed Editor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make single-device scenarios reusable across compatible device instances and constrain the graphical editor using the selected reference device's capabilities.

**Architecture:** Extend the public Scenario YAML with an optional `scenario.target.type` contract. New actions may omit concrete device IDs when a target is declared; the selected `SimulationHost` becomes the bound target at execution, while legacy device-bound YAML remains valid. The Vue editor uses a reference device only for authoring metadata and validates the actual run target through the backend runner.

**Tech Stack:** .NET 10, YamlDotNet, xUnit, Vue 3, TypeScript, Pinia, Vitest.

---

### Task 1: Extend the Scenario YAML contract

**Files:**
- Modify: `docs/PROJECT_SPEC.md`
- Modify: `src/IndustrialSim.Scenarios/ScenarioModel.cs`
- Test: `tests/IndustrialSim.Scenarios.Tests/ScenarioParserTests.cs`

1. Add parser tests for `scenario.target.type` with targetless set, ramp, command, when, and device fault actions.
2. Add regression coverage proving legacy action-level device IDs still parse.
3. Reject targetless device actions when neither a scenario target nor legacy device ID is present.
4. Parse the target into `ScenarioDefinition` without introducing protocol concepts.
5. Document the additive YAML shape and compatibility behavior in the normative specification.

### Task 2: Bind reusable scenarios to the selected runtime

**Files:**
- Modify: `src/IndustrialSim.Scenarios/ScenarioRunner.cs`
- Test: `tests/IndustrialSim.Scenarios.Tests/ScenarioSchedulerTests.cs`
- Test: `tests/IndustrialSim.IntegrationTests/SimulationRegistryTests.cs`

1. Add failing tests that run one targetless Pump scenario against two compatible device IDs.
2. Validate `scenario.target.type` against the selected runtime definition before scheduling actions.
3. Resolve omitted action and trigger device references to the current runtime device.
4. Preserve strict matching for legacy YAML that explicitly names a device ID.
5. Return actionable errors for incompatible device type, datapoint type/value, or command.

### Task 3: Expose authoring capabilities

**Files:**
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`
- Modify: `src/IndustrialSim.Web/ClientApp/src/types.ts`
- Test: `tests/IndustrialSim.Web.Tests/V1ApiContractTests.cs`

1. Add commands and events to the existing device-detail definition response.
2. Update the TypeScript detail contract without changing device creation requests.
3. Add an API contract assertion covering datapoints, commands, events, and protocol bindings.

### Task 4: Add constrained graphical authoring

**Files:**
- Modify: `src/IndustrialSim.Web/ClientApp/src/composables/useScenarioEditor.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/components/scenarios/ScenarioEditor.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/styles.css`
- Test: `src/IndustrialSim.Web/ClientApp/src/composables/useScenarioEditor.test.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/components/VisualEditors.test.ts`

1. Add a reference-device selector and persist only its device type as the reusable scenario target.
2. Replace duration text entry with a non-negative numeric field and `ms/s/min/h` unit selector; require positive values for `every` and ramp duration.
3. Populate datapoint selects from the reference definition and restrict ramp/condition options to supported scalar types.
4. Render Boolean, numeric, and string value controls according to datapoint type.
5. Populate command and protocol selects from the reference definition.
6. Replace free-form `when` entry with datapoint/operator/value controls matching the runtime expression grammar.
7. Preserve imports of legacy YAML and normalize them into the reusable editor representation.
8. Filter run targets by target type and leave final compatibility validation to the backend.

### Task 5: Verify built-in Pump interaction

**Files:**
- Test: `tests/IndustrialSim.Runtime.Tests/PumpTests.cs`
- Test: `tests/IndustrialSim.IntegrationTests/RuntimeCompositionTests.cs`

1. Add focused coverage proving `start` activates continuous Pump temperature behavior.
2. Add coverage documenting that built-in behavior currently owns calculated speed and pressure updates.
3. Do not silently change behavior ownership in this editor-focused change; report the overlap as an explicit remaining design decision.

### Task 6: Verification

1. Run `dotnet test tests/IndustrialSim.Scenarios.Tests/IndustrialSim.Scenarios.Tests.csproj`.
2. Run `dotnet test tests/IndustrialSim.Web.Tests/IndustrialSim.Web.Tests.csproj`.
3. Run the focused runtime and integration tests.
4. Run `npm test` and `npm run build` in `src/IndustrialSim.Web/ClientApp`.
5. Manually inspect new and imported scenarios in both light and dark modes.

