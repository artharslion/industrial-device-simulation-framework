# Explicit Built-in Device Behaviors Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make Pump, Motor, and Sensor behavior explicit and discoverable during device creation while preserving legacy YAML and custom devices.

**Architecture:** Store protocol-independent behavior metadata on `DeviceDefinition`. A catalog in `IndustrialSim.Devices` owns the three built-in profiles, their required schemas, commands, events, descriptions, and numeric defaults. Hosting binds the selected profile and validates explicit configurations; definitions without behavior metadata retain legacy type/schema inference.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, Vue 3, TypeScript, Vitest, xUnit.

---

### Task 1: Add behavior metadata and built-in profile catalog

**Files:**
- Modify: `src/IndustrialSim.Core/Domain/Definitions.cs`
- Create: `src/IndustrialSim.Devices/BuiltInDeviceProfiles.cs`
- Modify: `src/IndustrialSim.Devices/Pump/Pump.cs`
- Modify: `src/IndustrialSim.Devices/Motor/Motor.cs`
- Modify: `src/IndustrialSim.Devices/Sensor/Sensor.cs`
- Test: `tests/IndustrialSim.Runtime.Tests/BuiltInDeviceTests.cs`

1. Add `DeviceBehaviorDefinition(profile, parameters)` as optional device-definition metadata.
2. Define Pump, Motor, Sensor profile schemas and defaults in one catalog.
3. Add typed Motor and Sensor behavior parameters.
4. Test profile creation, validation, and parameter-driven updates.

### Task 2: Bind and validate explicit behavior profiles

**Files:**
- Modify: `src/IndustrialSim.Hosting/SimulationHost.cs`
- Modify: `src/IndustrialSim.Configuration/Models/ConfigurationModels.cs`
- Modify: `src/IndustrialSim.Configuration/YamlConfigurationLoader.cs`
- Test: `tests/IndustrialSim.Configuration.Tests/YamlConfigurationTests.cs`
- Test: `tests/IndustrialSim.Runtime.Tests/BuiltInDeviceTests.cs`

1. Read optional `device.behavior.profile` and numeric parameters from YAML.
2. Reject explicit built-in profiles whose type/schema/commands are incompatible.
3. Bind parameters into the corresponding runtime behavior.
4. Preserve legacy inference when behavior metadata is absent.

### Task 3: Expose profiles and persist complete device definitions

**Files:**
- Modify: `src/IndustrialSim.Web/Api/V1/V1Contracts.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/VisualModelingEndpoints.cs`
- Test: `tests/IndustrialSim.Web.Tests/V1ApiContractTests.cs`

1. Add a read-only built-in-profile endpoint.
2. Accept and persist commands, events, and behavior metadata on device create/update.
3. Return behavior metadata in device details.
4. Parse template `BehaviorJson` when instantiating templates.

### Task 4: Build the guided device-creation UI

**Files:**
- Modify: `src/IndustrialSim.Web/ClientApp/src/types.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/api.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/composables/useDeviceEditor.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/components/devices/DeviceCreateDialog.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/styles.css`
- Test: `src/IndustrialSim.Web/ClientApp/src/composables/useDeviceEditor.test.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/App.test.ts`

1. Replace free-text built-in selection with Pump/Motor/Sensor/Custom choices.
2. Populate canonical datapoints, commands, events, and default parameters.
3. Show behavior summary and editable numeric parameters.
4. Keep Custom datapoints fully editable and label it as having no built-in behavior.

### Task 5: Synchronize the public contract and verify

**Files:**
- Modify: `docs/PROJECT_SPEC.md`
- Modify: `examples/devices/pump.yaml`
- Modify: `examples/devices/motor.yaml`
- Modify: `examples/devices/sensor.yaml`

1. Document explicit behavior metadata and legacy inference.
2. Update canonical examples.
3. Run focused .NET and frontend tests, production build, and Light/Dark browser verification.
