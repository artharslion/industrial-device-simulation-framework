# Industrial Device Simulation MVP Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build the v0.1 MVP defined by `docs/PROJECT_SPEC.md`: a Pump-based industrial device runtime with YAML configuration, deterministic-friendly simulation, Scenario Engine, Data/Device/Network Faults, OPC UA, Modbus TCP, CLI, Docker, Web UI, and automated tests.

**Architecture:** Keep the Core protocol-independent and make `StateStore` the only runtime state authority. The Runtime drives device behavior, scenarios, and faults through an injectable clock and ordered state-transition pipeline; OPC UA, Modbus, CLI, and Web UI consume the same runtime abstraction. Network faults remain at the adapter/transport boundary and must not stop simulation.

**Tech Stack:** .NET LTS, C#, ASP.NET Core Minimal API/Razor or Blazor for the developer Web UI, YamlDotNet for YAML, a maintained OPC UA .NET server library, a maintained Modbus TCP server library, xUnit, Docker, and standard .NET logging/DI.

---

## Source Documents and Rules

- Normative specification: `docs/PROJECT_SPEC.md`
- Product context and roadmap: `docs/Industrial Device Simulation Framework.md`
- Repository AI rules: `AGENTS.md`
- AI collaboration guidance: `docs/AI_DEVELOPMENT_GUIDE.md`

Resolve contradictions in favor of `docs/PROJECT_SPEC.md`, then update the
affected documentation deliberately. Do not add MQTT, S7, BACnet, EtherNet/IP,
AAS, dynamic plugins, authentication, multi-user management, 3D visualization,
PLC runtime behavior, or enterprise features to v0.1.

## Global Acceptance Criteria

The MVP is complete only when all of the following are true:

- A Pump can be loaded from YAML and its state changes over simulation time.
- Scenario actions support `at`, `after`, `every`, `when`, `set`, `ramp`,
  `command`, and `wait`.
- Data, Device, and Network Faults support scheduling, activation, and
  recovery; network faults do not stop the device runtime.
- The same StateStore value is visible through OPC UA, Modbus TCP, CLI/API
  operations, and the Web UI.
- The Web UI can inspect state, control the simulation, run scenarios, control
  faults, and display events/logs.
- Configuration and Modbus mappings fail fast with actionable errors.
- Unit, protocol, and end-to-end tests pass in a clean checkout.
- Docker exposes OPC UA `4840`, Modbus TCP `5020`, and Web UI `8080`.

## Execution Rules

- Work in the task order below; later tasks depend on earlier contracts.
- For each behavior, write a failing focused test, implement the smallest
  passing change, run the test, then commit.
- Keep public contracts explicit: YAML schemas, Scenario action models,
  fault lifecycle, protocol mappings, and Web UI endpoints.
- Do not leave placeholder interfaces or untested generated code.
- After each milestone, run `dotnet build` and the relevant test projects.

## Current Execution Status (2026-08-31)

Phases 0-5 are implemented. Phase 6 contracts and initial adapters, plus
basic CLI/Web controls, are present, but the review in
`docs/review-current-implementation.md` identified acceptance-critical gaps.
The next session must begin at **Phase 6R / Task 6R.1** below. Do not start
Phase 8 release work or claim OPC UA interoperability until every 6R task and
its tests pass. Preserve the existing untracked `docs/plans/` files.

For every 6R task: inspect the current implementation, write a failing test,
run it to prove the failure, implement the smallest coherent change, run the
focused tests, then commit. Keep Core protocol-independent and route all
logical state through `StateStore`.

---

## Phase 0: Repository and Toolchain

### Task 0.1: Verify the environment

**Files:** None

- Check `dotnet --info`, Docker availability, and repository status.
- Record the selected .NET SDK and target framework in the implementation notes.
- Confirm the repository is clean enough to distinguish agent changes from user
  changes.

**Exit criteria:** Toolchain is available or the blocker is documented before
code generation begins.

### Task 0.2: Create the solution skeleton

**Create:** `IndustrialSim.sln` and projects under `src/` and `tests/`

Create these v0.1 projects:

- `IndustrialSim.Core`
- `IndustrialSim.Runtime`
- `IndustrialSim.Scenarios`
- `IndustrialSim.Faults`
- `IndustrialSim.Configuration`
- `IndustrialSim.Protocols.Abstractions`
- `IndustrialSim.Protocols.OpcUa`
- `IndustrialSim.Protocols.Modbus`
- `IndustrialSim.Devices`
- `IndustrialSim.Web`
- `IndustrialSim.Cli`

Create matching focused test projects plus
`IndustrialSim.IntegrationTests`.

**Exit criteria:** `dotnet build IndustrialSim.sln` succeeds with no circular
project references. Core has no reference to protocol or Web projects.

### Task 0.3: Add repository build defaults

**Create/Modify:** `Directory.Build.props`, `.editorconfig`, `.gitignore`

Set nullable reference types, implicit usings, warnings policy, deterministic
build settings, and test conventions. Keep generated artifacts out of Git.

**Exit criteria:** A clean build uses the same compiler defaults in every
project.

---

## Phase 1: Core Domain Model

### Task 1.1: Define domain value types and enums

**Create:** files under `src/IndustrialSim.Core/Domain/`

Implement `DataType`, `DataPointAccess`, `SimulationTime`, identifiers, and
validated value conversion for the v0.1 scalar types in the specification.

**Test:** `tests/IndustrialSim.Core.Tests/DomainValueTests.cs`

Cover supported types, invalid conversions, access modes, and equality.

### Task 1.2: Define immutable device definitions

**Create:** `DeviceDefinition`, `DataPointDefinition`, `CommandDefinition`,
`EventDefinition`, and related interfaces.

Definitions must not contain protocol addresses or mutable runtime values.

**Test:** `DeviceDefinitionTests.cs`

Reject duplicate names, unsupported types, invalid initial values, and invalid
access declarations.

### Task 1.3: Define runtime events and state transitions

**Create:** event records and transition result types in Core.

Include timestamp, device ID, event type, previous/new values, and metadata.

**Test:** `StateTransitionTests.cs`

**Exit criteria:** Core compiles independently and contains no adapter/UI
references.

---

## Phase 2: Runtime and Pump Behavior

### Task 2.1: Implement `StateStore`

**Create:** under `src/IndustrialSim.Runtime/State/`

Implement thread-safe reads and ordered writes through one transition method.
Validate DataPoint existence, type, and access before mutation; publish a
`DataPointChanged` event after a successful transition.

**Test:** `StateStoreTests.cs`

Cover concurrent reads, rejected writes, event ordering, and unchanged-value
behavior.

### Task 2.2: Implement injectable clocks

**Create:** `ISimulationClock`, `RealTimeClock`, `DeterministicClock`

The deterministic clock advances only when explicitly ticked. Domain logic must
not call wall-clock APIs directly.

**Test:** `SimulationClockTests.cs`

### Task 2.3: Implement the simulation engine and scheduler

**Create:** `SimulationEngine`, scheduler abstractions, lifecycle state

Support start, stop, pause, reset, tick/update, and ordered callbacks. Ensure a
network adapter failure cannot terminate the engine loop.

**Test:** `SimulationEngineTests.cs`

### Task 2.4: Implement the Pump device

**Create:** `src/IndustrialSim.Devices/Pump/`

Implement start/stop commands and the intentionally simple deterministic model:
speed ramp, temperature increase/cooling, pressure following speed, and alarm
state. Keep model parameters explicit and testable.

**Test:** `PumpTests.cs`

**Exit criteria:** A test can start a Pump, advance deterministic time, and
observe predictable speed, pressure, temperature, and running state changes.

---

## Phase 3: YAML Configuration and Validation

### Task 3.1: Define configuration DTOs and schema

**Create:** `src/IndustrialSim.Configuration/Models/`

Represent the canonical `device`, `protocols`, `web`, and scenario file shapes
from `docs/PROJECT_SPEC.md`. Keep DTOs separate from domain objects.

### Task 3.2: Implement YAML loading and domain mapping

**Create:** `YamlConfigurationLoader`, mapping/validation services

Use YamlDotNet. Convert YAML into validated domain definitions and protocol
configuration. Fail fast with path-aware errors.

**Test:** `YamlConfigurationTests.cs`

Cover valid Pump YAML, missing fields, duplicate DataPoints, invalid types,
invalid commands, and malformed YAML.

### Task 3.3: Validate Modbus mappings

Implement explicit address range, register width, encoding, byte order/word
order, access, and overlap validation. Core must remain unaware of addresses.

**Test:** `ModbusMappingValidationTests.cs`

### Task 3.4: Add canonical examples

**Create:** `examples/devices/pump.yaml`, `examples/scenarios/startup.yaml`,
`examples/scenarios/overheating.yaml`, `examples/scenarios/network-timeout.yaml`

Examples must match the canonical YAML shape and be covered by loader tests.

---

## Phase 4: Scenario Engine

### Task 4.1: Define Scenario AST and parser

**Create:** under `src/IndustrialSim.Scenarios/`

Support triggers `at`, `after`, `every`, `when` and actions `set`, `ramp`,
`command`, `wait`, `fault`. Use a small typed model; do not create a general
scripting language.

**Test:** parser tests for every action/trigger and malformed references.

### Task 4.2: Implement scheduler integration

Schedule actions against `ISimulationClock`, preserve deterministic ordering,
and ensure repeated `every` actions do not drift or duplicate unexpectedly.

**Test:** `ScenarioSchedulerTests.cs`

### Task 4.3: Implement conditions

Support only the documented scalar comparisons (`>`, `<`, `==` and boolean
comparisons). Resolve values through StateStore, never through adapters.

**Test:** `ScenarioConditionTests.cs`

### Task 4.4: Add startup and overheating scenario tests

Verify command execution, ramp behavior, condition-triggered alarm, and event
publication using the deterministic clock.

**Exit criteria:** Scenario Engine can drive the Pump without any protocol
server running.

---

## Phase 5: Fault Injection

### Task 5.1: Define fault contracts and lifecycle

**Create:** under `src/IndustrialSim.Faults/`

Implement `Scheduled`, `Active`, and `Recovered` lifecycle events with target,
duration, timestamps, and fault metadata.

### Task 5.2: Implement Data Faults

Implement v0.1 `Stale`, `Freeze`, `OutOfRange`, `Noise`, and `Spike`. Define
whether each fault changes the exposed value, quality metadata, or behavior;
keep this semantics consistent across adapters and Web UI.

**Test:** activation, duration, recovery, and deterministic seeded behavior.

### Task 5.3: Implement Device Faults

Implement `SensorFailure`, `Overheat`, `PowerLoss`, and `EmergencyStop` with
explicit effects on state, behavior, commands, and events.

**Test:** each fault's state transition and recovery semantics.

### Task 5.4: Implement Network Faults at the adapter boundary

Implement `Disconnect`, `Timeout`, and `Latency` controls without mutating the
device model or stopping SimulationEngine. Define behavior for new requests,
existing connections, and recovery.

**Test:** adapter requests fail or delay as configured while the deterministic
device clock continues to advance.

**Exit criteria:** All three fault categories are observable and independently
testable.

---

## Phase 6: Protocol Adapters

### Task 6.1: Define protocol-neutral adapter contracts

**Create:** `src/IndustrialSim.Protocols.Abstractions/`

Define adapter lifecycle, runtime access, mapping, command invocation, event
observation, options, and transport-fault controls. Do not expose concrete
OPC UA or Modbus types to Core.

### Task 6.2: Implement OPC UA server adapter

Map Device to Object, DataPoints to Variables, Commands to Methods, and runtime
events to supported OPC UA events/notifications. Reads and writes must route
through StateStore.

**Test:** server startup, node mapping, read/write access, command invocation,
and state-change notification.

### Task 6.3: Implement Modbus TCP server adapter

Map coils, discrete inputs, input registers, and holding registers using the
validated explicit mapping. Implement configured float32 and integer encoding.

**Test:** server startup, reads, writes, address overlap rejection, encoding,
and shared-state observation.

### Task 6.4: Add shared-state integration test

Start both adapters against one runtime, change state through a Scenario, and
assert both protocol clients observe the same value.

**Exit criteria:** The first end-to-end protocol loop passes without duplicate
state copies.

### Phase 6R: Post-Review Protocol and Host Gap Closure

This remediation phase is deliberately placed before Phase 7/8 completion.
It closes the gaps documented in `docs/review-current-implementation.md`
without expanding the v0.1 protocol or product scope.

### Task 6R.1: Implement a real OPC UA server host

**Files:**
- Modify: `src/IndustrialSim.Protocols.OpcUa/OpcUaAdapter.cs` and its project file
- Create/modify: OPC UA server/node-manager support files under
  `src/IndustrialSim.Protocols.OpcUa/`
- Test: `tests/IndustrialSim.Protocols.OpcUa.Tests/`

**Step 1: Write failing interoperability tests.** Start an ephemeral endpoint,
connect with an OPC UA client library, browse the configured Device/DataPoint
nodes, read a value, write a writable value, invoke a command Method, and
subscribe for a StateStore data-change notification. Also assert clean stop
releases the port and invalid access is rejected.

**Step 2: Run the focused test and verify it fails** because the current raw
TCP listener is not an OPC UA binary server.

**Step 3: Implement the minimal OPC Foundation server.** Add application
configuration/certificate handling suitable for local tests, a standard
server and custom node manager, runtime-backed Variables and Methods, and
StateStore-to-monitored-node notifications. Keep the in-process adapter API
only as a convenience; `IsStandardOpcUaServer` must be truthful.

**Step 4: Run the focused OPC UA tests and verify they pass.**

**Step 5: Commit:** `feat: implement interoperable opc ua server`

### Task 6R.2: Complete Modbus TCP wire behavior

**Files:**
- Modify: `src/IndustrialSim.Protocols.Modbus/ModbusAdapter.cs` and mapping
  validation/configuration files as needed
- Test: `tests/IndustrialSim.Protocols.Modbus.Tests/`

**Step 1: Write failing real-TCP-client tests** for functions 1/2/3/4/5/6/16,
read-only/write access, illegal addresses, all v0.1 integer/float widths,
configured byte order and word order, and multi-register writes.

**Step 2: Run them to verify failures.**

**Step 3: Implement only the documented YAML mapping combinations**, including
  deterministic encoding/decoding, access enforcement at the wire boundary,
  and protocol exception responses. Do not add new mapping kinds or protocols.

**Step 4: Run the focused Modbus tests and verify they pass.**

**Step 5: Commit:** `fix: complete modbus tcp contract`

### Task 6R.3: Make CLI and Web YAML-driven compositions

**Files:**
- Modify: `src/IndustrialSim.Cli/CliRunner.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Create/modify: shared composition/bootstrap code in Runtime or a host project
- Test: CLI/Web tests and configuration integration tests

**Step 1: Write failing tests** proving both hosts load the same device and
protocol sections from YAML, create one StateStore/runtime, start and stop the
configured adapters, and attach ScenarioRunner/FaultManager. Include missing
file/invalid protocol configuration and cancellation/lifecycle cases.

**Step 2: Run focused tests to verify failures** (Web currently uses a fallback
Pump and does not start configured protocol services; `scenario run` only parses).

**Step 3: Implement a shared, protocol-neutral bootstrap** used by CLI and Web.
Keep Web fallback behavior only for development when no config path is set;
never duplicate state or silently ignore configured protocols.

**Step 4: Run focused CLI/Web tests and verify they pass.**

**Step 5: Commit:** `feat: wire yaml runtime composition into hosts`

### Task 6R.4: Cross-protocol and fault end-to-end coverage

**Files:**
- Create/modify: `tests/IndustrialSim.IntegrationTests/`

**Step 1: Write a failing test** that starts one runtime with real OPC UA and
Modbus servers, runs a Scenario, mutates state through each protocol, and
activates/recoveries a data/device/network fault. Assert both clients and the
Web/API surface observe the same StateStore value and lifecycle events.

**Step 2: Run the integration test to verify it fails** on any remaining host,
encoding, notification, or lifecycle gap.

**Step 3: Fix only defects exposed by the test.**

**Step 4: Run the complete integration project and then the full solution test.**

**Step 5: Commit:** `test: cover cross-protocol runtime flow`

**Phase 6R checkpoint:** Run `dotnet test IndustrialSim.sln --configuration
Release` and `dotnet build IndustrialSim.sln --configuration Release`. Stop and
report changed files, test output, commit hashes, and residual risks for review
before continuing to Phase 7/8.

---

## Phase 7: CLI and Developer Web UI

### Task 7.1: Implement CLI commands

**Create:** `src/IndustrialSim.Cli/`

Implement:

- `industrial-sim validate <file>`
- `industrial-sim run <file>`
- `industrial-sim scenario run <file>`
- `--deterministic` and `--seed` options where supported

Return non-zero exit codes for configuration, scenario, protocol, and runtime
errors. Keep logging structured and human-readable.

**Test:** command parsing, validation failures, and lifecycle behavior.

### Task 7.2: Implement the Web UI host and runtime endpoints

**Create:** `src/IndustrialSim.Web/`

Provide a developer console on port `8080` with endpoints/pages for:

- Device and DataPoint state
- Start, stop, pause, reset
- Load/run/stop Scenario
- Activate/recover Fault
- Runtime events and logs

All mutations must call the same runtime command/state paths as CLI and
protocol adapters. Do not add authentication or multi-user features.

**Test:** endpoint/component tests and one browser-level smoke test if the test
environment supports it.

### Task 7.3: Add UI error and lifecycle states

Show loading, running, paused, stopped, fault-active, fault-recovered, and
validation-error states without duplicating runtime state in the browser.

**Exit criteria:** A developer can run the Pump scenario and inject/recover a
fault from the Web UI while OPC UA/Modbus clients see the same state.

---

## Phase 8: Docker, Documentation, and Release Verification

### Task 8.1: Add container build

**Create:** `Dockerfile`, `.dockerignore`

Build the CLI/Web host and expose ports `4840`, `5020`, and `8080`.

### Task 8.2: Add Docker Compose example

**Create:** `docker-compose.yml`

Mount device/scenario configuration and make the documented Pump demo work
with one command.

### Task 8.3: Add integration test harness

Start the runtime in-process or in a test container, connect protocol clients,
run a Scenario, inject a fault, recover it, and assert state through both
protocols and the Web UI/API surface.

### Task 8.4: Update project documentation

Update README and relevant docs with the canonical demo, ports, YAML shape,
Scenario/Fault examples, test commands, and current v0.1 boundaries.

### Task 8.5: Run release gate

Run:

```powershell
dotnet restore IndustrialSim.sln
dotnet build IndustrialSim.sln --configuration Release
dotnet test IndustrialSim.sln --configuration Release --no-build
docker build -t industrial-sim:local .
docker compose config
```

Then manually verify:

1. Web UI opens at `http://localhost:8080`.
2. OPC UA is reachable at `opc.tcp://localhost:4840`.
3. Modbus TCP is reachable at `localhost:5020`.
4. The Pump Scenario changes state.
5. Data/Device/Network Faults activate and recover.

**Exit criteria:** All global acceptance criteria pass and known gaps are
documented.

---

## Suggested Commit Sequence

Use small commits that leave the repository buildable:

1. `chore: scaffold industrial sim solution`
2. `feat: add core device model`
3. `feat: add runtime state store and clock`
4. `feat: add deterministic pump behavior`
5. `feat: add yaml configuration and validation`
6. `feat: add scenario engine`
7. `feat: add fault injection`
8. `feat: add opc ua adapter`
9. `feat: add modbus tcp adapter`
10. `feat: add cli and developer web ui`
11. `build: add docker runtime`
12. `test: add end to end mvp coverage`

Do not squash away intermediate verification evidence until the MVP has passed
the release gate.

## Continuation Commit Sequence

The repository already contains the earlier implementation commits through
`d78262d fix: correct protocol contract gaps`. The next session should create
these additional commits in order, one per completed task:

1. `feat: implement interoperable opc ua server`
2. `fix: complete modbus tcp contract`
3. `feat: wire yaml runtime composition into hosts`
4. `test: cover cross-protocol runtime flow`
5. Continue with the Phase 7 and Phase 8 commits only after the Phase 6R
   checkpoint is reviewed and accepted.

**Handoff command:** open a new session in this repository, read
`AGENTS.md`, `docs/PROJECT_SPEC.md`, `docs/review-current-implementation.md`,
and this plan, then begin at **Task 6R.1 Step 1**.
