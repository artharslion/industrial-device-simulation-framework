# Device Launch and Restore Closure Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make YAML loading, Web Quick Create, template instantiation, and SQLite restoration produce the same validated `SimulationHost` semantics, including real OPC UA and Modbus TCP adapters.

**Architecture:** Introduce one typed launch definition in Hosting and make every entry point compose that definition before registry mutation. Persist a versioned launch document in the existing device catalog JSON column, restore each catalog row independently, and keep all live datapoint state in the host-owned `StateStore`.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core SQLite, YamlDotNet, OPC Foundation .NET Standard stack, TCP Modbus, Vue 3, TypeScript, Vitest, xUnit.

---

## Design audit

### Applicable requirements

- `docs/PROJECT_SPEC.md` sections 2.3 and 2.5 require SQLite to remain a control-plane store and `StateStore` to remain the live-state authority.
- Sections 4.2, 24, 33-38, and 71 require Core and Scenario to remain protocol-independent.
- Sections 44-45 require device and protocol mappings to be validated before runtime start and to fail with actionable errors.
- Sections 49-50 require real-client protocol evidence and shared logical state across OPC UA and Modbus.
- `docs/adr/0004-template-and-mapping-separation.md` requires instantiation to record exact template and mapping versions and to compose them before registry creation.
- The Wave 2 matrix gate requires templates to instantiate normal `SimulationHost` instances and mapping profiles to survive restart.

### Current construction paths

```text
YAML
  -> YamlConfigurationLoader.Load
  -> LoadedConfiguration(DeviceDefinition + RootConfiguration + validated Modbus mappings)
  -> SimulationHost
  -> configured OPC UA / Modbus adapters

Quick Create
  -> CreateDeviceRequest
  -> DeviceLaunchDefinition(DeviceDefinition + options + PortBindings)
  -> SimulationRegistry.CreateAsync
  -> SimulationHost.Create(DeviceDefinition, options)
  -> empty RootConfiguration
  -> no adapters

Template Instantiate
  -> template DeviceDefinition + PortBindings
  -> same empty-configuration host path
  -> saved mapping profiles are not selected, compiled, or passed to Hosting

Process start
  -> only INDUSTRIALSIM_DEVICE_CONFIG YAML is loaded
  -> device catalog is migrated lazily during endpoint mapping
  -> catalog devices are never restored
```

### Persistence JSON audit

The existing `Devices.DefinitionJson` values are not sufficient as a stable restore contract.

| Writer | Stored content | Recoverable today | Missing semantics |
|---|---|---|---|
| Quick Create / update | Serialized `CreateDeviceRequest` | Logical definition, behavior, deterministic flag, seed, and port reservations | Actual enabled adapters, OPC UA endpoint options, Modbus mappings, mapping provenance |
| Template Instantiate | Anonymous object with template reference, datapoints, options, and ports | Partial logical definition if the referenced template still exists | Commands, events, behavior, selected mapping profiles, resolved protocol configuration |
| Persistence tests / older rows | Arbitrary definition-shaped JSON | Not reliably | Schema/version discriminator and full launch semantics |

The column itself can remain, but its document needs a versioned envelope. A SQL schema migration is not required for the recommended approach: the entity may keep the physical `DefinitionJson` column while the Application contract treats its content as `LaunchJson`. Existing rows are upgraded in memory by a legacy reader and rewritten only on the next successful explicit edit. No startup migration may silently enable a protocol that was previously only reserved.

### Template mapping audit

- `ProtocolMappingProfile` contains `TemplateId`, `TemplateVersion`, `Protocol`, `Name`, and datapoint entries with an address plus optional data/byte/word order.
- Template validation currently checks template identity, duplicate addresses, and datapoint references, but does not validate protocol names or compile an entry into an adapter configuration.
- `InstantiateTemplateRequest` contains only device ID, simulation options, and `PortBindings`; it has no mapping-profile selection.
- Modbus profile addresses such as `40001` are stored as strings, while the adapter requires a validated zero-based address kind and width.
- OPC UA profiles can describe datapoint node identifiers, but `IndustrialNodeManager` currently hard-codes `${deviceId}/${datapoint}` and does not consume a profile.

### Confirmed defects and risks

1. `ProtocolPortBinding` is a reservation model, not a launch model, but the API and UI present it as protocol configuration.
2. Host replacement validates only duplicate reserved ports; it does not construct or validate real adapters before swapping the registry entry.
3. Desired lifecycle state is always written as `Stopped` and lifecycle endpoints never update it.
4. `Program.cs` starts one YAML host before the control-plane database is available and has no per-device restore loop.
5. The current global exception mapping collapses many protocol/configuration errors into `validationFailed`; the required stable error codes are absent.
6. A future naive restore could accidentally expose ports from legacy reservation-only JSON. Compatibility must be conservative.

## Architecture options

### Option A: Reuse `RootConfiguration` / `LoadedConfiguration` as the universal launch contract

All entry points would construct the same mutable configuration DTO used by YAML and pass a `LoadedConfiguration` into `SimulationHost`.

**Advantages:** smallest change to `SimulationHost`; exact parity with the current YAML path; existing Modbus validation can be reused directly.

**Costs:** leaks YAML-shaped mutable DTOs into Web/Application contracts; mixes Web configuration with device launch semantics; template compilers must manufacture YAML configuration objects; persistence becomes coupled to configuration-parser internals.

### Option B: Add typed protocol launch definitions and adapt YAML into them (recommended)

`DeviceLaunchDefinition` becomes the canonical immutable Hosting input. It contains the logical `DeviceDefinition`, `SimulationHostOptions`, typed OPC UA and Modbus launch definitions, resolved mappings, and optional source provenance. YAML, Quick Create, and template instantiation all compose this model; `SimulationHost` consumes only this model.

**Advantages:** narrow change aligned with current Hosting boundaries; protocol addresses remain outside Core; invalid mappings are rejected before registry insertion; persistence has one stable semantic document; no generic plugin system is introduced.

**Costs:** requires explicit conversion from YAML models and from template profiles; OPC UA and Modbus remain named types in Hosting, which is acceptable for the currently supported protocols but will need a later Wave 4 catalog/factory extraction.

### Option C: Introduce a generic protocol descriptor and adapter-factory registry now

Persist protocol name plus arbitrary options/mapping JSON, then resolve adapters through registered factories.

**Advantages:** most extensible for future protocols and plugins; Hosting can avoid concrete protocol option types.

**Costs:** overlaps deferred Wave 4 protocol catalog/supervision work; weakens compile-time validation; requires schemas/factories/version negotiation that are unnecessary for the two current adapters; materially increases failure and migration surface.

## Recommended design

Choose Option B. It is the smallest coherent closure that fixes real adapter construction without pre-implementing Wave 4.

### Canonical Hosting contract

Move launch records out of `SimulationRegistry.cs` into a dedicated Hosting file and replace reservation-only input with typed configuration:

```csharp
public sealed record DeviceLaunchDefinition(
    DeviceDefinition Definition,
    SimulationHostOptions Options,
    OpcUaLaunchDefinition? OpcUa = null,
    ModbusLaunchDefinition? Modbus = null,
    DeviceLaunchSource? Source = null);

public sealed record OpcUaLaunchDefinition(
    string Endpoint,
    IReadOnlyDictionary<string, string>? DataPointNodeIds = null);

public sealed record ModbusLaunchDefinition(
    int Port,
    IReadOnlyList<ValidatedModbusMapping> Mappings);

public sealed record DeviceLaunchSource(
    string Kind,
    string? TemplateId = null,
    string? TemplateVersion = null,
    IReadOnlyList<MappingProfileReference>? MappingProfiles = null);
```

`ProtocolPortBinding` becomes a derived read model on `SimulationHandle`; it must not be accepted as the source of adapter configuration. `SimulationHost.Create(DeviceLaunchDefinition)` constructs and configures adapters. `SimulationRegistry.CreateAsync` and `ReplaceAsync` construct the candidate through that one method before registration/swap.

YAML compatibility is preserved by converting `LoadedConfiguration` to `DeviceLaunchDefinition` with the same endpoint, port, and validated Modbus mappings. CLI/environment overrides remain higher priority and are applied during this conversion.

### Mapping-profile compilation

Application owns composition from immutable template artifacts into Hosting launch types:

- The instantiate request selects profiles by exact `(protocol, name)` for the requested template version.
- Unknown protocol, missing profile, duplicate selection, or profile/template mismatch fails before registry creation.
- Modbus profile addresses are parsed explicitly. Accepted forms are `coil:<zero-based>`, `discrete:<zero-based>`, `input:<zero-based>`, and `holding:<zero-based>`. Existing `0xxxx/1xxxx/3xxxx/4xxxx` display notation may be accepted only through a documented converter with unambiguous one-based-to-zero-based behavior. The resolved launch document stores canonical kind/address values, not the display string.
- Modbus entries pass through `ModbusMappingValidator` plus device datatype/access compatibility validation.
- OPC UA profile addresses are treated as explicit string NodeIds for datapoints; duplicate NodeIds and unknown datapoints fail validation. Commands retain the defined default `${deviceId}/${command}` NodeIds in this increment.
- If no OPC UA profile is selected, OPC UA may use the documented built-in default mapping (`${deviceId}` object, `${deviceId}/${datapoint}` variables, `${deviceId}/${command}` methods).
- Modbus has no implicit default address allocation. Enabling Modbus without a selected or inline complete mapping returns `modbusMappingRequired`.

### Web public contracts

Replace the misleading primary `portBindings` shape with typed protocol configuration while accepting the old field for one compatibility cycle:

```json
{
  "protocols": {
    "opcua": { "enabled": true, "endpoint": "opc.tcp://0.0.0.0:4841" },
    "modbus": {
      "enabled": true,
      "port": 5021,
      "mappings": [
        { "dataPoint": "speed", "kind": "holding", "address": 100, "dataType": "int32", "access": "readwrite" }
      ]
    }
  }
}
```

Template instantiation adds exact mapping selections:

```json
{
  "deviceId": "template-pump",
  "protocols": {
    "opcua": { "enabled": true, "port": 4842 },
    "modbus": { "enabled": true, "port": 5022, "mappingProfile": "holding-registers" }
  }
}
```

Compatibility behavior for legacy `portBindings` is deliberately safe:

- Empty or absent bindings create a no-protocol device.
- A legacy OPC UA binding may translate to the documented default OPC UA mapping.
- A legacy Modbus binding is rejected for new requests with `modbusMappingRequired`; it is never presented as a configured adapter.
- Unknown protocol names return `unknownProtocol`.
- Persisted legacy rows with reservation-only bindings restore the logical device with no protocol and emit a structured compatibility warning. Startup does not silently open a network port that was never opened before.

The Vue Quick Create and device editor must label the no-protocol state clearly. Selecting OPC UA shows its default mapping rule. Selecting Modbus requires complete mapping rows. Template Instantiate shows available profiles and requires selection for protocols whose mappings are not implicit.

### Versioned persisted launch document

Store one JSON document per catalog device:

```json
{
  "schemaVersion": 1,
  "device": { "id": "pump-1", "type": "pump", "dataPoints": [], "commands": [], "events": [], "behavior": {} },
  "simulation": { "deterministic": false, "seed": 0 },
  "protocols": {
    "opcua": { "endpoint": "opc.tcp://0.0.0.0:4841", "dataPointNodeIds": {} },
    "modbus": { "port": 5021, "mappings": [] }
  },
  "source": {
    "kind": "quickCreate",
    "templateId": null,
    "templateVersion": null,
    "mappingProfiles": []
  }
}
```

The document stores the resolved configuration required to recreate the host. Template references are provenance, not restore dependencies. Deleting a template therefore does not break an already instantiated device.

`DeviceCatalogItem.DefinitionJson` becomes `LaunchJson` at the Application contract level while the repository continues mapping it to the existing physical `Devices.DefinitionJson` column. This avoids a SQL migration and preserves existing SQLite files. The reader supports:

1. Schema version 1 launch documents.
2. Legacy Quick Create request JSON, restored conservatively without reservation-only protocols.
3. Legacy template-instance JSON by resolving the immutable referenced template for the logical definition; because no mapping selection was recorded, protocols remain disabled.

Malformed/unsupported legacy rows fail only that device with `deviceLaunchDocumentInvalid` or `deviceLaunchDocumentUnsupported`.

### Desired lifecycle and startup rule

Add a normative specification section before implementation.

- The catalog stores desired state as `Stopped` or `Running`; `Paused` is runtime-only and restores as `Stopped` unless a later specification adds pause snapshots.
- Lifecycle commands persist intent before changing runtime. If persistence is unavailable, the mutation is rejected and the existing runtime is not automatically stopped or rewritten.
- If adapter start fails after `Running` intent is committed, the host remains registered and stopped, the API returns a protocol-specific failure, and the desired state remains `Running` for explicit retry/reconciliation.
- On process startup, all valid catalog definitions are reconstructed in stopped state.
- Safe default: `IndustrialSim:Restore:AutoStartDesiredRunning=false`. With the default, `Running` intent is preserved but external protocol listeners are not opened automatically. When explicitly enabled, each desired-Running device is started independently; one start failure leaves that device stopped and does not block other devices or the Web host.
- The explicitly configured `INDUSTRIALSIM_DEVICE_CONFIG` YAML remains a boot device and starts as today. It is registered first. A catalog row with the same device ID or conflicting port fails only that catalog restore and cannot replace the boot device.

### Failure handling and stable error codes

Add typed launch/configuration exceptions and map them to Problem Details:

| Condition | HTTP | `errorCode` |
|---|---:|---|
| Duplicate device ID | 409 | `duplicateDeviceId` |
| Port conflict | 409 | `portConflict` |
| Unknown protocol | 400 | `unknownProtocol` |
| Enabled Modbus without mappings | 400 | `modbusMappingRequired` |
| Invalid/overlapping Modbus mapping | 400 | `invalidModbusMapping` |
| Invalid OPC UA endpoint or node mapping | 400 | `invalidOpcUaConfiguration` |
| Behavior/type/schema incompatibility | 400 | `invalidBehaviorProfile` |
| Missing/incompatible template mapping profile | 400 | `mappingProfileInvalid` |
| Protocol listener cannot start | 409 when address conflict, otherwise 503 | `protocolStartFailed` |
| Invalid persisted launch document | startup-isolated | `deviceLaunchDocumentInvalid` |
| Unsupported persisted document version | startup-isolated | `deviceLaunchDocumentUnsupported` |

REST restore is not added. Startup restoration reports a `DeviceRestoreResult` per row and logs device ID, stage, and stable code. It must not write live datapoints to SQLite and must not create Wave 3 observability infrastructure.

## Implementation plan

### Task 1: Add the normative launch/restore contract

**Files:**
- Modify: `docs/PROJECT_SPEC.md`
- Modify: `docs/PROTOFORGE_BASELINE_MATRIX.md`
- Test: `tests/IndustrialSim.IntegrationTests/DocumentationContractTests.cs`

1. Write failing documentation assertions for a versioned device launch document, conservative legacy restoration, explicit Modbus mappings, and the desired-Running safe default.
2. Run `dotnet test tests/IndustrialSim.IntegrationTests/IndustrialSim.IntegrationTests.csproj --filter DocumentationContractTests` and confirm failure.
3. Add a `Device Launch and Restore` section to the specification using the decisions above.
4. Change the protocol/template matrix rows from misleadingly verified wording to evidence that includes Web-created real-client access and restart restoration; do not mark the closure Verified until its tests pass.
5. Re-run the focused test and commit `docs: define device launch and restore semantics`.

### Task 2: Make typed launch configuration the only Host construction input

**Files:**
- Create: `src/IndustrialSim.Hosting/DeviceLaunchDefinition.cs`
- Modify: `src/IndustrialSim.Hosting/SimulationHost.cs`
- Modify: `src/IndustrialSim.Hosting/SimulationRegistry.cs`
- Modify: `src/IndustrialSim.Configuration/YamlConfigurationLoader.cs`
- Test: `tests/IndustrialSim.IntegrationTests/RuntimeCompositionTests.cs`
- Test: `tests/IndustrialSim.IntegrationTests/SimulationRegistryTests.cs`
- Test: `tests/IndustrialSim.Configuration.Tests/YamlConfigurationTests.cs`

1. Write failing tests proving a typed launch creates configured adapter instances, mappings are validated before registration, replacement builds the same candidate semantics, and YAML conversion produces an equivalent launch.
2. Run the three focused test classes and confirm the current empty-`RootConfiguration` path fails them.
3. Add typed OPC UA/Modbus/source launch records and derived port bindings.
4. Replace `SimulationHost.Create(DeviceDefinition, options)` registry usage with `SimulationHost.Create(DeviceLaunchDefinition)`; retain a no-protocol convenience overload only for existing unit tests.
5. Convert `LoadedConfiguration` plus overrides into the canonical launch model and use it in `LoadAsync`.
6. Re-run focused tests and commit `refactor: unify simulation host launch composition`.

### Task 3: Compile and apply template mapping profiles

**Files:**
- Create: `src/IndustrialSim.Application/Templates/TemplateLaunchComposer.cs`
- Modify: `src/IndustrialSim.Templates/TemplateCatalog.cs`
- Modify: `src/IndustrialSim.Protocols.OpcUa/OpcUaAdapter.cs`
- Modify: `src/IndustrialSim.Protocols.OpcUa/IndustrialOpcUaServer.cs`
- Test: `tests/IndustrialSim.Templates.Tests/TemplateCatalogTests.cs`
- Test: `tests/IndustrialSim.Protocols.OpcUa.Tests/ProtocolContractTests.cs`
- Test: `tests/IndustrialSim.Protocols.Modbus.Tests/ModbusContractRegressionTests.cs`

1. Write failing tests for exact profile selection, unknown protocol/profile, duplicate/invalid addresses, device incompatibility, canonical Modbus address conversion, and explicit OPC UA datapoint NodeIds.
2. Run focused template and protocol tests and confirm failure.
3. Implement the Application composer from template package plus selection into a complete `DeviceLaunchDefinition`.
4. Reuse existing Modbus validation; do not duplicate register-width or overlap rules.
5. Pass optional datapoint NodeIds into the OPC UA node manager while retaining current default NodeIds when no profile is selected.
6. Re-run focused tests and commit `feat: apply template protocol mappings at launch`.

### Task 4: Persist and read versioned launch documents

**Files:**
- Create: `src/IndustrialSim.Application/Devices/DeviceLaunchDocument.cs`
- Create: `src/IndustrialSim.Application/Devices/DeviceLaunchDocumentSerializer.cs`
- Modify: `src/IndustrialSim.Application/Catalogs/CatalogRecords.cs`
- Modify: `src/IndustrialSim.Persistence/Repositories/DeviceCatalogRepository.cs`
- Test: `tests/IndustrialSim.Application.Tests/DeviceLaunchDocumentTests.cs`
- Test: `tests/IndustrialSim.Persistence.Tests/CatalogPersistenceTests.cs`

1. Write failing round-trip tests covering device metadata, behavior, simulation options, both protocols, resolved mappings, and template provenance.
2. Add compatibility fixtures for current Quick Create JSON, current template-instance JSON, malformed JSON, and an unsupported future schema version.
3. Reopen an SQLite database created with the existing migration and prove the new repository reads/writes the same physical column without schema loss.
4. Implement the versioned serializer and conservative legacy readers. Legacy reservation-only bindings must not enable adapters.
5. Re-run Application/Persistence tests and commit `feat: persist complete device launch documents`.

### Task 5: Move device creation, replacement, and desired-state coordination into Application

**Files:**
- Modify: `src/IndustrialSim.Application/Devices/DeviceApplicationService.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/ControlPlaneServices.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Contracts.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/VisualModelingEndpoints.cs`
- Modify: `src/IndustrialSim.Web/Errors/IndustrialSimProblemDetails.cs`
- Test: `tests/IndustrialSim.Web.Tests/V1ApiContractTests.cs`
- Test: `tests/IndustrialSim.Web.Tests/VisualModelingApiTests.cs`

1. Write failing API tests for no-protocol Quick Create, default-mapped OPC UA, explicit Modbus mappings, exact template profile selection, and persistence of the complete launch document.
2. Add failing Problem Details tests for port conflict, unknown protocol, missing/invalid/overlapping mapping, missing profile, incompatible behavior, and listener-start failure.
3. Expand `DeviceApplicationService` to coordinate validation, registry mutation, launch-document persistence, replacement rollback, and desired-state intent through repository abstractions and `IUnitOfWork`.
4. Keep endpoints as HTTP translation only; Quick Create and Template Instantiate must call the same Application create path.
5. Persist `Running`/`Stopped` intent before lifecycle mutation according to the approved rule. Batch operations process and report each device independently.
6. Re-run Web tests and commit `feat: close web device launch contracts`.

### Task 6: Restore catalog devices during Web startup

**Files:**
- Create: `src/IndustrialSim.Application/Devices/DeviceCatalogRestoreService.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Modify: `src/IndustrialSim.Web/appsettings.json`
- Test: `tests/IndustrialSim.IntegrationTests/DeviceCatalogRestoreTests.cs`
- Test: `tests/IndustrialSim.Web.Tests/ConfigurationLoadingTests.cs`

1. Write failing tests that build an SQLite catalog with multiple launch documents, recreate the service/registry, and verify every valid device returns with its protocols and definition intact.
2. Add tests for safe-default desired-Running restoration, explicit auto-start opt-in, malformed document isolation, invalid mapping isolation, port-conflict isolation, and boot-YAML ID/port precedence.
3. Move database migration before restoration. Register/start the configured YAML boot host first, then restore catalog rows in stable ID order.
4. Return per-device restore results; log failures with stable codes and continue. A desired-Running adapter start failure leaves the reconstructed host registered but stopped.
5. Ensure application startup succeeds when at least one catalog row is invalid and when the catalog is empty.
6. Re-run focused tests and commit `feat: restore device catalog on web startup`.

### Task 7: Prove real Web-created protocol access

**Files:**
- Create: `tests/IndustrialSim.IntegrationTests/WebCreatedProtocolDeviceTests.cs`
- Modify: `tests/IndustrialSim.IntegrationTests/CrossProtocolRuntimeFlowTests.cs`
- Modify: `tests/IndustrialSim.Protocols.OpcUa.Tests/ProtocolContractTests.cs`
- Modify: `tests/IndustrialSim.Protocols.Modbus.Tests/ModbusWireBehaviorTests.cs`

1. Write a real OPC UA client test that creates a device through `/api/v1`, starts it, browses the object, reads/writes a datapoint, and calls a command.
2. Write a real TCP Modbus client test that creates a device with explicit mappings, starts it, reads and writes registers/coils, and verifies HTTP observes the same `StateStore` values.
3. Write a template instantiation test selecting a persisted mapping profile and accessing the resulting device through the real protocol.
4. Write a restart test that creates multiple API devices, disposes Web/registry, rebuilds from the same SQLite database, and repeats protocol access.
5. Preserve and re-run the original YAML Pump dual-protocol Scenario/Fault flow.
6. Run the focused integration tests and commit `test: verify web launch and restore protocols`.

### Task 8: Make the Vue workflow truthful and typed

**Files:**
- Modify: `src/IndustrialSim.Web/ClientApp/src/types.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/api.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/composables/useDeviceEditor.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/components/devices/DeviceCreateDialog.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/views/DeviceDetailsView.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/components/templates/TemplateEditor.vue`
- Test: `src/IndustrialSim.Web/ClientApp/src/composables/useDeviceEditor.test.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/composables/useTemplateEditor.test.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/components/VisualEditors.test.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/views/DeviceDetailsView.test.ts`

1. Write failing UI/composable tests for explicit no-protocol creation, OPC UA default mapping disclosure, required Modbus mappings, template profile selection, and field-level Problem Details.
2. Replace “reserved ports” authoring with typed protocol sections and mapping rows.
3. Show configured adapters and effective endpoints/mappings in device details; do not label a reservation as configured.
4. Keep existing behavior-profile authoring and template/scenario workflows unchanged outside these fields.
5. Run `npm test` and `npm run build`, then commit `feat: make device protocol launch explicit`.

### Task 9: Regression and release verification

1. Run the narrowest changed test classes after each task.
2. Run:

```powershell
dotnet test tests/IndustrialSim.Hosting.Tests/IndustrialSim.Hosting.Tests.csproj
dotnet test tests/IndustrialSim.Persistence.Tests/IndustrialSim.Persistence.Tests.csproj
dotnet test tests/IndustrialSim.Web.Tests/IndustrialSim.Web.Tests.csproj
dotnet test tests/IndustrialSim.Protocols.OpcUa.Tests/IndustrialSim.Protocols.OpcUa.Tests.csproj
dotnet test tests/IndustrialSim.Protocols.Modbus.Tests/IndustrialSim.Protocols.Modbus.Tests.csproj
dotnet test tests/IndustrialSim.IntegrationTests/IndustrialSim.IntegrationTests.csproj
dotnet test IndustrialSim.sln
```

3. If `tests/IndustrialSim.Hosting.Tests` does not exist at implementation time, keep Hosting-focused tests in the existing Integration project rather than creating a project solely to satisfy the command name.
4. Run in `src/IndustrialSim.Web/ClientApp`:

```powershell
npm test
npm run build
```

5. Run `docker compose config` and `git diff --check`.
6. Perform a manual smoke test only if automated real-client coverage cannot exercise a platform-specific condition. Record the exact endpoint/client/result; do not claim untested behavior.
7. Update `docs/PROTOFORGE_BASELINE_MATRIX.md` to `Verified` only after the API-created OPC UA, API-created Modbus, template mapping, multi-device restart, and failure-isolation evidence all passes.

## Non-goals

- No Wave 3 observability, metrics, testing platform, forwarding, Webhooks, recording/replay, or SDK work.
- No additional protocol, protocol marketplace, dynamic adapter discovery, or plugin loader.
- No custom script behavior or PLC runtime behavior.
- No SQLite live datapoint persistence or automatic implicit snapshot restore.
- No protocol addresses in Core, built-in behavior definitions, or Scenario actions.

## Review checkpoint

Implementation starts only after confirmation of these three decisions:

1. Use typed OPC UA/Modbus launch definitions rather than `RootConfiguration` or a generic Wave 4 factory system.
2. Keep the existing SQLite column and introduce a versioned launch-document format with conservative legacy readers.
3. Restore desired-Running devices stopped by default, with explicit `IndustrialSim:Restore:AutoStartDesiredRunning=true` required to reopen protocol listeners automatically.
