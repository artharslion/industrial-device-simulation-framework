# ProtoForge Baseline Expansion Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Expand the completed IndustrialSim v0.1 runtime into a product platform that meets or exceeds ProtoForge's user-visible functional baseline while preserving unified runtime state, protocol-independent Core, deterministic execution, and first-class fault injection.

**Architecture:** Build a modular monolith around the existing runtime. A new application/control plane manages many `SimulationHost` instances, SQLite-backed catalogs, templates, tests, integrations, identity, API, SignalR, and Razor Components; every protocol remains an isolated adapter over the same runtime contract.

**Tech Stack:** .NET 10, C#, ASP.NET Core Minimal APIs and Razor Components, SignalR, ASP.NET Core Identity, EF Core SQLite, OpenAPI, OpenTelemetry/Prometheus, xUnit, Testcontainers where appropriate, Docker, and maintained protocol libraries selected through explicit feasibility gates.

---

## Inputs and execution rules

- Read `AGENTS.md`, `docs/PROJECT_SPEC.md`,
  `docs/plans/2026-09-02-protoforge-baseline-design.md`, and the relevant
  protocol specification before each task.
- Treat `docs/plans/2026-08-28-industrial-device-simulation-mvp.md` as complete.
- Keep the current v0.1 behavior and tests green throughout the work.
- Write a failing focused test before production code.
- Run the focused test to prove the expected failure.
- Implement the smallest coherent behavior.
- Run focused tests, then the affected integration tests.
- Commit after every task with the listed commit message.
- Do not copy ProtoForge source or template content without verifying its
  license and preserving required attribution.
- Do not claim protocol support until its capability manifest and external
  interoperability evidence are complete.

## Global completion criteria

- The baseline matrix covers all ProtoForge user-visible capability groups and
  all 15 marketed protocols.
- At least 49 versioned templates can be searched, viewed, instantiated, and
  validated.
- Devices, scenarios, templates, tests, reports, users, and settings survive a
  process restart.
- Multiple devices and scenarios can run concurrently without sharing mutable
  state accidentally.
- The Web UI contains dashboard, device, protocol, template/marketplace,
  scenario/editor, testing, integration, logs, and settings experiences.
- `/api/v1` provides documented endpoints and a typed .NET SDK.
- Test cases, suites, reports, diagnostics, forwarding, Webhooks, recording,
  replay, metrics, and authentication work end to end.
- Every protocol publishes a truthful compatibility manifest and has automated
  external-client coverage or an explicit manual verification record.
- `dotnet test IndustrialSim.sln --configuration Release` remains green and the
  original deterministic dual-protocol Pump demonstration still passes.

## Wave 0: Deliberate scope and architecture expansion

### Task 0.1: Update the normative roadmap and baseline matrix

**Files:**
- Modify: `docs/PROJECT_SPEC.md`
- Modify: `docs/PROTOFORGE_COMPARISON.md`
- Create: `docs/PROTOFORGE_BASELINE_MATRIX.md`
- Test: `tests/IndustrialSim.IntegrationTests/DocumentationContractTests.cs`

**Step 1: Write the failing documentation contract test**

Add a test that loads `docs/PROTOFORGE_BASELINE_MATRIX.md` and asserts it lists
the capability groups `devices`, `protocols`, `templates`, `scenarios`,
`testing`, `forwarding`, `recording`, `webhooks`, `metrics`, `authentication`,
`settings`, `sdk`, plus all 15 protocol names.

**Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test tests/IndustrialSim.IntegrationTests/IndustrialSim.IntegrationTests.csproj --filter DocumentationContractTests
```

Expected: FAIL because the baseline matrix does not exist.

**Step 3: Update the documents**

- Mark v0.1 as accepted/completed without rewriting its historical definition.
- Add a post-v0.1 platform roadmap that supersedes the old v0.2-v1.0 sequence.
- State that authentication, persistence, multi-user control, additional
  protocols, templates, and integrations are now authorized next-phase scope.
- Give every matrix row an owner module, target wave, acceptance evidence, and
  status (`Not Started`, `In Progress`, `Verified`, `Constrained`).

**Step 4: Run the focused test and documentation checks**

Run the focused test and `git diff --check`.

Expected: PASS and no whitespace errors.

**Step 5: Commit**

```powershell
git add docs tests/IndustrialSim.IntegrationTests/DocumentationContractTests.cs
git commit -m "docs: define protoforge baseline roadmap"
```

### Task 0.2: Record the control-plane architecture decisions

**Files:**
- Create: `docs/adr/0001-modular-control-plane.md`
- Create: `docs/adr/0002-live-state-and-persistence.md`
- Create: `docs/adr/0003-web-ui-and-live-streaming.md`
- Create: `docs/adr/0004-template-and-mapping-separation.md`
- Create: `docs/adr/0005-protocol-compatibility-gates.md`
- Create: `docs/adr/0006-authentication-modes.md`

**Step 1: Write ADRs in Accepted state**

Each ADR must include context, decision, alternatives, consequences, failure
modes, and reversal conditions. Use the decisions in the design document; do
not introduce microservices or protocol-owned live state.

**Step 2: Review project-reference consequences**

Document the allowed direction:

```text
Web -> Application -> Hosting -> Runtime/Core
Web -> Persistence -> Application contracts
Application -> Templates/Testing/Integrations
Protocols -> Protocol Abstractions + Runtime contract
Core/Runtime -X-> Web/Persistence/concrete protocols
```

**Step 3: Verify links and commit**

Run `git diff --check`, then commit:

```powershell
git add docs/adr
git commit -m "docs: record platform architecture decisions"
```

## Wave 1: Platform foundation

### Task 1.1: Introduce the application and persistence project boundaries

**Files:**
- Create: `src/IndustrialSim.Application/IndustrialSim.Application.csproj`
- Create: `src/IndustrialSim.Application/Abstractions/*.cs`
- Create: `src/IndustrialSim.Persistence/IndustrialSim.Persistence.csproj`
- Create: `src/IndustrialSim.Persistence/IndustrialSimDbContext.cs`
- Create: `tests/IndustrialSim.Application.Tests/IndustrialSim.Application.Tests.csproj`
- Create: `tests/IndustrialSim.Persistence.Tests/IndustrialSim.Persistence.Tests.csproj`
- Modify: `IndustrialSim.sln`

**Step 1: Write failing architecture tests**

Use reflection over project output assemblies to assert Core and Runtime do not
reference Application, Persistence, Web, or concrete protocol assemblies.
Assert Application does not reference EF Core or ASP.NET Core.

**Step 2: Run and verify failure**

Run the new Application and Persistence test projects. Expected: FAIL because
the projects/contracts do not exist.

**Step 3: Add minimal contracts and EF Core SQLite infrastructure**

Start with these application contracts:

```csharp
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IRepository<TAggregate, in TId>
{
    Task<TAggregate?> FindAsync(TId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TAggregate>> ListAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);
    void Remove(TAggregate aggregate);
}
```

Add EF Core SQLite packages only to Persistence. Register all projects in the
solution and keep migrations out until concrete entities exist.

**Step 4: Run architecture tests and full build**

Expected: PASS; `dotnet build IndustrialSim.sln` succeeds.

**Step 5: Commit**

```powershell
git add IndustrialSim.sln src/IndustrialSim.Application src/IndustrialSim.Persistence tests
git commit -m "feat: add application and persistence boundaries"
```

### Task 1.2: Add a multi-runtime simulation registry

**Files:**
- Create: `src/IndustrialSim.Hosting/SimulationRegistry.cs`
- Create: `src/IndustrialSim.Application/Devices/DeviceApplicationService.cs`
- Create: `src/IndustrialSim.Application/Devices/DeviceRecords.cs`
- Test: `tests/IndustrialSim.IntegrationTests/SimulationRegistryTests.cs`

**Step 1: Write failing lifecycle and isolation tests**

Cover create, start, stop, remove, list, duplicate IDs, batch start/stop/remove,
independent deterministic clocks, and state isolation between two devices.

**Step 2: Run and verify failure**

Expected: FAIL because only individual `SimulationHost` instances exist.

**Step 3: Implement the registry**

Use a thread-safe dictionary and per-entry async lifecycle gate. Expose:

```csharp
public interface ISimulationRegistry
{
    Task<SimulationHandle> CreateAsync(DeviceLaunchDefinition definition, CancellationToken cancellationToken = default);
    Task StartAsync(string deviceId, CancellationToken cancellationToken = default);
    Task StopAsync(string deviceId, CancellationToken cancellationToken = default);
    Task RemoveAsync(string deviceId, CancellationToken cancellationToken = default);
    IReadOnlyList<SimulationSummary> List();
}
```

Do not put catalogs, EF entities, HTTP models, or authorization in Hosting.

**Step 4: Run focused and existing integration tests**

Expected: registry tests and the original Pump flow pass.

**Step 5: Commit**

```powershell
git add src/IndustrialSim.Hosting src/IndustrialSim.Application tests/IndustrialSim.IntegrationTests
git commit -m "feat: manage multiple simulation runtimes"
```

### Task 1.3: Persist devices, scenarios, settings, and runtime snapshots

**Files:**
- Create: `src/IndustrialSim.Application/Catalogs/*.cs`
- Create: `src/IndustrialSim.Persistence/Entities/*.cs`
- Create: `src/IndustrialSim.Persistence/Repositories/*.cs`
- Create: `src/IndustrialSim.Persistence/Migrations/*`
- Create: `src/IndustrialSim.Hosting/Snapshots/*.cs`
- Test: `tests/IndustrialSim.Persistence.Tests/CatalogPersistenceTests.cs`
- Test: `tests/IndustrialSim.IntegrationTests/SnapshotRestoreTests.cs`

**Step 1: Write failing restart and snapshot tests**

Create catalogs, close the SQLite context, reopen it, and assert data survives.
Take a deterministic runtime snapshot, mutate state, restore it, and assert
state/time/random-seed compatibility checks.

**Step 2: Run and verify failure**

Expected: FAIL because no persistent catalogs or snapshots exist.

**Step 3: Implement persistence without replacing `StateStore`**

Persist definitions and snapshots as versioned documents with indexed metadata.
Add optimistic concurrency tokens. Snapshot restore must enter state through a
validated runtime restoration service, never through protocol caches.

**Step 4: Run focused tests and migration smoke test**

Create a temporary SQLite database, apply migrations, reopen, and verify schema
version. Expected: PASS.

**Step 5: Commit**

```powershell
git add src/IndustrialSim.Application src/IndustrialSim.Persistence src/IndustrialSim.Hosting tests
git commit -m "feat: persist catalogs and runtime snapshots"
```

### Task 1.4: Add `/api/v1`, Problem Details, OpenAPI, and SignalR streams

**Files:**
- Create: `src/IndustrialSim.Web/Api/V1/*.cs`
- Create: `src/IndustrialSim.Web/Hubs/RuntimeHub.cs`
- Create: `src/IndustrialSim.Web/Errors/IndustrialSimProblemDetails.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Modify: `src/IndustrialSim.Web/IndustrialSimApi.cs`
- Test: `tests/IndustrialSim.Web.Tests/V1ApiContractTests.cs`
- Test: `tests/IndustrialSim.Web.Tests/RuntimeHubTests.cs`

**Step 1: Write failing API contract tests**

Cover protocol status, device CRUD/batch lifecycle, datapoint reads/writes,
scenario CRUD/lifecycle, validation errors, duplicate conflicts, OpenAPI, and a
SignalR subscription receiving an ordered datapoint change.

**Step 2: Run and verify failure**

Expected: FAIL because current APIs are single-runtime and unversioned.

**Step 3: Implement application-backed endpoints**

Map stable resources under `/api/v1`. Return RFC 9457 Problem Details with
stable `errorCode` extensions. Keep existing endpoints as one-release
compatibility shims and add `Deprecation`/`Sunset` headers.

Use bounded channels between runtime observers and SignalR. Coalesce repeated
datapoint updates by device/datapoint when clients lag.

**Step 4: Run Web and integration tests**

Expected: all API and live-stream tests pass.

**Step 5: Commit**

```powershell
git add src/IndustrialSim.Web tests/IndustrialSim.Web.Tests
git commit -m "feat: add versioned control api and live streams"
```

### Task 1.5: Add optional authentication, users, and RBAC

**Files:**
- Create: `src/IndustrialSim.Web/Identity/*.cs`
- Create: `src/IndustrialSim.Application/Security/IndustrialRoles.cs`
- Modify: `src/IndustrialSim.Persistence/IndustrialSimDbContext.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Modify: `src/IndustrialSim.Web/appsettings.json`
- Test: `tests/IndustrialSim.Web.Tests/AuthenticationTests.cs`

**Step 1: Write failing security tests**

Cover disabled local mode, one-time bootstrap, login/token issuance, password
change, user listing/deletion, invalid credentials, role policies, and the
absence of secrets in logs and exported data.

**Step 2: Run and verify failure**

Expected: FAIL because the app has no identity boundary.

**Step 3: Implement Identity and policies**

Use ASP.NET Core Identity with roles:

```text
Viewer   -> read state, logs, templates, reports
Operator -> Viewer + runtime/scenario/fault/test operations
Admin    -> Operator + users, settings, protocols, integrations
```

Support `Auth:Mode=Disabled|LocalIdentity`. Do not ship a fixed password.

**Step 4: Run security and API tests**

Expected: enabled mode fails closed; disabled mode preserves local developer
experience.

**Step 5: Commit**

```powershell
git add src/IndustrialSim.Web src/IndustrialSim.Application src/IndustrialSim.Persistence tests
git commit -m "feat: add optional identity and role authorization"
```

## Wave 2: Templates, scenarios, and Web console

### Task 2.1: Add versioned device templates and mapping profiles

**Files:**
- Create: `src/IndustrialSim.Templates/IndustrialSim.Templates.csproj`
- Create: `src/IndustrialSim.Templates/Models/*.cs`
- Create: `src/IndustrialSim.Templates/TemplateCatalog.cs`
- Create: `src/IndustrialSim.Templates/Validation/*.cs`
- Create: `templates/schema/*.json`
- Create: `tests/IndustrialSim.Templates.Tests/*`
- Modify: `IndustrialSim.sln`

**Step 1: Write failing template tests**

Cover version immutability, search, protocol/tag filters, device/mapping
separation, instantiation, invalid mappings, incompatible versions, import,
export, and attribution metadata.

**Step 2: Run and verify failure**

Expected: FAIL because no template module exists.

**Step 3: Implement the template contracts**

Use immutable records similar to:

```csharp
public sealed record DeviceTemplate(
    string Id,
    SemanticVersion Version,
    string DisplayName,
    IReadOnlyList<string> Tags,
    DeviceDefinition Definition,
    BehaviorProfile Behavior,
    Attribution Attribution);

public sealed record ProtocolMappingProfile(
    string TemplateId,
    SemanticVersion TemplateVersion,
    string Protocol,
    JsonDocument Mapping);
```

Persist catalog metadata through Application/Persistence, not inside protocol
projects.

**Step 4: Run template and architecture tests**

Expected: PASS; Core remains unaware of template/mapping JSON.

**Step 5: Commit**

```powershell
git add IndustrialSim.sln src/IndustrialSim.Templates tests/IndustrialSim.Templates.Tests templates
git commit -m "feat: add versioned device template catalog"
```

### Task 2.2: Add scenario catalog, import/export, snapshots, and editor model

**Files:**
- Create: `src/IndustrialSim.Application/Scenarios/*.cs`
- Create: `src/IndustrialSim.Scenarios/Editing/*.cs`
- Create: `src/IndustrialSim.Web/Api/V1/ScenarioEndpoints.cs`
- Test: `tests/IndustrialSim.Scenarios.Tests/ScenarioDocumentTests.cs`
- Test: `tests/IndustrialSim.Web.Tests/ScenarioApiTests.cs`

**Step 1: Write failing tests**

Cover CRUD, start/stop, YAML import/export, version conflicts, graph-to-AST and
AST-to-graph round trips, and a runtime snapshot returned for an active
scenario.

**Step 2: Run and verify failure**

Expected: FAIL because scenarios are currently submitted directly to one host.

**Step 3: Implement a versioned scenario document**

Keep the executable Scenario AST unchanged. Add editor-only node positions and
links in a separate document so layout does not affect deterministic execution.

**Step 4: Run scenario and API tests**

Expected: existing parser/scheduler tests remain green.

**Step 5: Commit**

```powershell
git add src/IndustrialSim.Application src/IndustrialSim.Scenarios src/IndustrialSim.Web tests
git commit -m "feat: manage and edit versioned scenarios"
```

### Task 2.3: Replace the embedded console with Razor Components navigation

**Files:**
- Create: `src/IndustrialSim.Web/Components/App.razor`
- Create: `src/IndustrialSim.Web/Components/Layout/*.razor`
- Create: `src/IndustrialSim.Web/Components/Pages/Dashboard.razor`
- Create: `src/IndustrialSim.Web/Components/Pages/Devices.razor`
- Create: `src/IndustrialSim.Web/Components/Pages/Protocols.razor`
- Create: `src/IndustrialSim.Web/Components/Pages/Templates.razor`
- Create: `src/IndustrialSim.Web/Components/Pages/Scenarios.razor`
- Create: `src/IndustrialSim.Web/Components/Pages/ScenarioEditor.razor`
- Create: `src/IndustrialSim.Web/Components/Pages/Testing.razor`
- Create: `src/IndustrialSim.Web/Components/Pages/Integrations.razor`
- Create: `src/IndustrialSim.Web/Components/Pages/Logs.razor`
- Create: `src/IndustrialSim.Web/Components/Pages/Settings.razor`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Retire after compatibility period: `src/IndustrialSim.Web/DeveloperConsolePage.cs`
- Test: `tests/IndustrialSim.Web.Tests/NavigationComponentTests.cs`

**Step 1: Write failing component/navigation tests**

Assert every baseline page renders, handles loading/empty/error states, honors
roles, and reconnects to SignalR after a transient disconnect.

**Step 2: Run and verify failure**

Expected: FAIL because only the embedded page exists.

**Step 3: Implement the shell and read-only pages first**

Use reusable status, table, event-stream, error-boundary, and confirmation
components. Keep server state authoritative; the browser stores only UI state.

**Step 4: Add mutation flows and run browser smoke tests**

Use Playwright from a new Web E2E test project for create/start/stop/write,
scenario execution, fault recovery, and authorization visibility.

**Step 5: Commit**

```powershell
git add src/IndustrialSim.Web tests
git commit -m "feat: add full developer web console"
```

### Task 2.4: Add visual scenario editing and template marketplace flows

**Files:**
- Create: `src/IndustrialSim.Web/Components/ScenarioEditor/*.razor`
- Create: `src/IndustrialSim.Web/Components/Templates/*.razor`
- Create: `src/IndustrialSim.Web/wwwroot/js/scenario-editor.js`
- Test: `tests/IndustrialSim.Web.E2ETests/ScenarioEditorTests.cs`
- Test: `tests/IndustrialSim.Web.E2ETests/TemplateMarketplaceTests.cs`

**Step 1: Write failing browser tests**

Create a threshold/command/fault graph, save it, reload it, run it, and verify
deterministic state. Search/filter a template, inspect mappings, instantiate a
device, and start selected protocols.

**Step 2: Run and verify failure**

Expected: FAIL because the interactive flows do not exist.

**Step 3: Implement keyboard-accessible editing**

Use Razor Components for forms/state and a small JS/SVG canvas only for drag,
pan, zoom, and edges. Preserve a form/list editing mode for accessibility and
test reliability.

**Step 4: Run E2E and scenario round-trip tests**

Expected: PASS; graph layout changes do not alter Scenario AST semantics.

**Step 5: Commit**

```powershell
git add src/IndustrialSim.Web tests/IndustrialSim.Web.E2ETests
git commit -m "feat: add scenario editor and template marketplace"
```

## Wave 3: Testing, observability, and integrations

### Task 3.1: Add structured event log, health, metrics, and tracing

**Files:**
- Create: `src/IndustrialSim.Observability/IndustrialSim.Observability.csproj`
- Create: `src/IndustrialSim.Observability/RuntimeEventLog.cs`
- Create: `src/IndustrialSim.Observability/IndustrialSimMetrics.cs`
- Create: `src/IndustrialSim.Observability/IndustrialSimActivitySource.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Create: `tests/IndustrialSim.Observability.Tests/*`
- Modify: `docker-compose.yml`

**Step 1: Write failing tests**

Cover bounded log retention/filtering, live subscribers, dropped-event counts,
health status, Prometheus names, activity correlation, and secret redaction.

**Step 2: Run and verify failure**

Expected: FAIL because events are currently an in-memory queue without the
baseline observability surfaces.

**Step 3: Implement standard instrumentation**

Expose at minimum:

```text
industrial_simulation_ticks_total
industrial_device_state_changes_total
industrial_scenario_actions_total
industrial_faults_active
industrial_protocol_connections
industrial_protocol_errors_total
industrial_stream_events_dropped_total
```

Add liveness/readiness checks and OpenTelemetry traces around application and
adapter operations.

**Step 4: Run tests and Prometheus scrape smoke test**

Expected: PASS and valid metrics text.

**Step 5: Commit**

```powershell
git add IndustrialSim.sln src/IndustrialSim.Observability tests docker-compose.yml
git commit -m "feat: add logs metrics health and tracing"
```

### Task 3.2: Add user-defined test cases, suites, assertions, and reports

**Files:**
- Create: `src/IndustrialSim.Testing/IndustrialSim.Testing.csproj`
- Create: `src/IndustrialSim.Testing/Models/*.cs`
- Create: `src/IndustrialSim.Testing/Execution/*.cs`
- Create: `src/IndustrialSim.Testing/Assertions/*.cs`
- Create: `src/IndustrialSim.Testing/Reports/*.cs`
- Create: `src/IndustrialSim.Web/Api/V1/TestEndpoints.cs`
- Create: `tests/IndustrialSim.Testing.Tests/*`

**Step 1: Write failing domain and execution tests**

Cover case/suite CRUD, delays, skip policies, timeouts, variable extraction,
equality/range/regex/status/latency assertions, runtime/API/protocol actions,
report persistence, HTML export, and trend aggregation.

**Step 2: Run and verify failure**

Expected: FAIL because there is no user-facing test engine.

**Step 3: Implement a deterministic test runner**

Define actions behind interfaces so tests can use in-process adapters or real
network clients. Persist definitions and immutable reports. Never execute
arbitrary source code from a test case.

**Step 4: Run Testing, Persistence, and Integration tests**

Expected: PASS; failures contain step-level diagnostics and captured evidence.

**Step 5: Commit**

```powershell
git add IndustrialSim.sln src/IndustrialSim.Testing src/IndustrialSim.Web tests
git commit -m "feat: add simulation test cases suites and reports"
```

### Task 3.3: Add quick-test generation and diagnostics

**Files:**
- Create: `src/IndustrialSim.Testing/Generation/*.cs`
- Create: `src/IndustrialSim.Testing/Diagnostics/*.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/TestEndpoints.cs`
- Test: `tests/IndustrialSim.Testing.Tests/TestGenerationTests.cs`

**Step 1: Write failing generation tests**

Given a device definition and mappings, expect generated read, write, command,
access, boundary, cross-protocol consistency, scenario, and fault cases. Cover
diagnostic suggestions for unreachable endpoints, type mismatch, illegal
address, access denial, timeout, and divergent state.

**Step 2: Run and verify failure**

Expected: FAIL because no generator exists.

**Step 3: Implement rule-based generation**

Keep generation deterministic and explainable. Return the rule/evidence behind
every suggestion; do not label opaque heuristics as AI diagnostics.

**Step 4: Run generated cases against the Pump demo**

Expected: generated suite passes, then a deliberately broken mapping produces
the expected diagnostic.

**Step 5: Commit**

```powershell
git add src/IndustrialSim.Testing src/IndustrialSim.Web tests
git commit -m "feat: generate protocol tests and diagnostics"
```

### Task 3.4: Add forwarding targets and Webhooks

**Files:**
- Create: `src/IndustrialSim.Integrations/IndustrialSim.Integrations.csproj`
- Create: `src/IndustrialSim.Integrations/Forwarding/*.cs`
- Create: `src/IndustrialSim.Integrations/Webhooks/*.cs`
- Create: `src/IndustrialSim.Web/Api/V1/IntegrationEndpoints.cs`
- Create: `tests/IndustrialSim.Integrations.Tests/*`

**Step 1: Write failing integration tests**

Cover file, HTTP, and InfluxDB line-protocol forwarding; start/stop/stats;
Webhook CRUD/test; event filters; retry/backoff; dead-letter diagnostics;
timeouts; and simulation continuing while targets fail.

**Step 2: Run and verify failure**

Expected: FAIL because the integrations module does not exist.

**Step 3: Implement bounded asynchronous delivery**

Observers enqueue immutable event envelopes into bounded channels. Delivery
workers own retry policy and cancellation. They must never run on the
simulation tick thread.

**Step 4: Run focused tests with temporary HTTP/Influx-compatible fixtures**

Expected: PASS; failure metrics increase without stopping runtime progress.

**Step 5: Commit**

```powershell
git add IndustrialSim.sln src/IndustrialSim.Integrations src/IndustrialSim.Web tests
git commit -m "feat: add forwarding and webhook integrations"
```

### Task 3.5: Add semantic recording and replay, then protocol wire capture

**Files:**
- Create: `src/IndustrialSim.Integrations/Recording/*.cs`
- Modify: `src/IndustrialSim.Protocols.Abstractions/Contracts.cs`
- Modify: supported protocol adapters for optional wire observers
- Create: `src/IndustrialSim.Web/Api/V1/RecordingEndpoints.cs`
- Test: `tests/IndustrialSim.Integrations.Tests/RecordingReplayTests.cs`

**Step 1: Write failing replay tests**

Record state reads/writes, commands, scenario actions, faults, and protocol
identity. Replay into a clean deterministic runtime and assert the same ordered
state/event result. Cover pause, speed, export, deletion, storage limits, and a
wire-capture capability flag.

**Step 2: Run and verify failure**

Expected: FAIL because recording/replay does not exist.

**Step 3: Implement semantic capture first**

Use a versioned envelope with simulation timestamp and sequence. Add optional
raw frame observers only where the protocol stack exposes frames legally and
reliably. Never promise raw replay for unsupported adapters.

**Step 4: Run deterministic replay and storage-failure tests**

Expected: semantic replay is exact; storage exhaustion stops only recording.

**Step 5: Commit**

```powershell
git add src/IndustrialSim.Integrations src/IndustrialSim.Protocols.Abstractions src/IndustrialSim.Protocols.* src/IndustrialSim.Web tests
git commit -m "feat: add deterministic recording and replay"
```

### Task 3.6: Add importers and the typed .NET SDK

**Files:**
- Create: `src/IndustrialSim.Integrations/Import/EdgeLiteImporter.cs`
- Create: `src/IndustrialSim.Integrations/Import/PyGbSentryImporter.cs`
- Create: `src/IndustrialSim.Client/IndustrialSim.Client.csproj`
- Create: `src/IndustrialSim.Client/*.cs`
- Create: `tests/IndustrialSim.Client.Tests/*`
- Create: `tests/IndustrialSim.Integrations.Tests/ImportTests.cs`
- Modify: `IndustrialSim.sln`

**Step 1: Write failing fixture and SDK tests**

Use representative checked-in fixtures for both import formats. Cover sync and
async SDK operations for devices, protocols, scenarios, templates, tests,
reports, logs, integrations, recordings, and authentication.

**Step 2: Run and verify failure**

Expected: FAIL because importers and SDK do not exist.

**Step 3: Implement import reports and typed clients**

Importers return warnings/errors per source path and never silently discard
unsupported fields. Build the SDK from stable API contracts and Problem Details
codes; do not expose Web implementation types.

**Step 4: Run contract tests against an in-memory Web host**

Expected: SDK serialization matches `/api/v1` OpenAPI behavior.

**Step 5: Commit**

```powershell
git add IndustrialSim.sln src/IndustrialSim.Integrations src/IndustrialSim.Client tests
git commit -m "feat: add config importers and dotnet sdk"
```

## Wave 4: Protocol and template baseline

### Task 4.1: Add protocol manifests, registration, and supervision

**Files:**
- Modify: `src/IndustrialSim.Protocols.Abstractions/Contracts.cs`
- Create: `src/IndustrialSim.Protocols.Abstractions/ProtocolManifest.cs`
- Create: `src/IndustrialSim.Hosting/ProtocolCatalog.cs`
- Create: `src/IndustrialSim.Hosting/ProtocolSupervisor.cs`
- Test: `tests/IndustrialSim.IntegrationTests/ProtocolCatalogTests.cs`

**Step 1: Write failing manifest/supervision tests**

Cover discovery, option schema, port/platform constraints, declared services,
data types, security modes, compatibility status, adapter-specific failure,
restart, and runtime survival.

**Step 2: Run and verify failure**

Expected: FAIL because current adapters expose only name/lifecycle.

**Step 3: Extend the protocol contract**

Add a read-only manifest without leaking concrete protocol types:

```csharp
public sealed record ProtocolManifest(
    string Name,
    string DisplayName,
    ProtocolCompatibility Compatibility,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Platforms,
    JsonDocument OptionsSchema,
    string EvidenceDocument);
```

Use explicit registration first. Dynamic assembly loading is deferred until a
separate signed-plugin design is approved.

**Step 4: Run all existing protocol and integration tests**

Expected: OPC UA and Modbus remain green.

**Step 5: Commit**

```powershell
git add src/IndustrialSim.Protocols.Abstractions src/IndustrialSim.Hosting tests
git commit -m "feat: add protocol capability catalog"
```

### Task 4.2: Implement open protocol batch — HTTP, MQTT, Modbus RTU, MTConnect

**Files:**
- Create: `src/IndustrialSim.Protocols.Http/*`
- Create: `src/IndustrialSim.Protocols.Mqtt/*`
- Create: `src/IndustrialSim.Protocols.ModbusRtu/*`
- Create: `src/IndustrialSim.Protocols.MtConnect/*`
- Create: matching projects under `tests/`
- Create: `docs/protocols/http.md`
- Create: `docs/protocols/mqtt.md`
- Create: `docs/protocols/modbus-rtu.md`
- Create: `docs/protocols/mtconnect.md`
- Modify: `IndustrialSim.sln`

**Step 1: Select maintained libraries and record licenses**

Document alternatives and versions in each protocol manifest. For Modbus RTU,
use injectable serial transport so tests do not require physical ports.

**Step 2: Write failing external-client tests**

- HTTP: OpenAPI-described datapoint/command/event surface.
- MQTT: retained state topics, command/write topics, QoS, reconnect.
- Modbus RTU: frame/CRC, unit IDs, supported functions, timing boundaries.
- MTConnect: probe/current/sample XML with schema validation.

**Step 3: Implement thin adapters**

All reads/writes/commands route through the host runtime. Network faults affect
only the selected adapter.

**Step 4: Run protocol tests and cross-protocol consistency tests**

Expected: the same Pump state is visible through the original and new adapters.

**Step 5: Commit each protocol separately**

Use commits `feat: add http simulation adapter`, `feat: add mqtt simulation
adapter`, `feat: add modbus rtu adapter`, and `feat: add mtconnect adapter`.

### Task 4.3: Implement controller/building batch — BACnet, S7, MC, FINS, EtherNet/IP

**Files:**
- Create: `src/IndustrialSim.Protocols.Bacnet/*`
- Create: `src/IndustrialSim.Protocols.S7/*`
- Create: `src/IndustrialSim.Protocols.Mc/*`
- Create: `src/IndustrialSim.Protocols.Fins/*`
- Create: `src/IndustrialSim.Protocols.EtherNetIp/*`
- Create: matching test projects and `docs/protocols/*.md`
- Modify: `IndustrialSim.sln`

**Step 1: Complete a feasibility gate per protocol**

Before production code, record specification access, library license,
server-side support, supported transport, external client, and the minimal
interoperable subset. Stop that protocol task if legal or technical evidence is
missing; mark it `Constrained` in the baseline matrix rather than adding a stub.

**Step 2: Write failing external-client/packet tests**

- BACnet/IP: device/object discovery and read/write properties.
- S7: rack/slot, DB memory areas, typed reads/writes.
- Mitsubishi MC: documented frame type and device memory access.
- Omron FINS: node addressing and memory-area read/write.
- EtherNet/IP: session registration and documented CIP tag services.

**Step 3: Implement isolated adapters and mappings**

Protocol addresses remain mapping profiles. Do not add DB/register/tag concepts
to Device definitions.

**Step 4: Run adapter failure isolation and cross-protocol state tests**

Expected: each supported subset is interoperable and one failed adapter does
not stop others.

**Step 5: Commit each verified protocol separately**

Use one buildable commit per adapter and its evidence document.

### Task 4.4: Implement specialized batch — GB28181, OPC DA, FANUC, Toledo

**Files:**
- Create: `src/IndustrialSim.Protocols.Gb28181/*`
- Create: `src/IndustrialSim.Protocols.OpcDa/*`
- Create: `src/IndustrialSim.Protocols.Fanuc/*`
- Create: `src/IndustrialSim.Protocols.Toledo/*`
- Create: matching test projects and `docs/protocols/*.md`
- Create: `src/IndustrialSim.OpcDaHost/IndustrialSim.OpcDaHost.csproj`
- Modify: `IndustrialSim.sln`

**Step 1: Complete legal/platform feasibility records**

- GB28181: document SIP/catalog/heartbeat/PTZ subset and media non-goals.
- OPC DA: Windows-only COM host, bitness, registration, and container limits.
- FANUC: document whether a licensed FOCAS SDK can be redistributed/tested; if
  not, define and label only the implemented compatibility profile.
- Toledo: document supported command/response framing and device profiles.

**Step 2: Write failing protocol-specific tests**

Use external tools where automatable and packet fixtures otherwise. OPC DA
tests run only on a labeled Windows CI worker and must report `Skipped` with an
actionable reason elsewhere.

**Step 3: Implement only approved subsets**

Keep specialized transport hosts outside Core and Runtime. Do not make the main
Linux container depend on Windows COM or proprietary SDKs.

**Step 4: Run platform matrix tests and document manual evidence**

Expected: manifests accurately match the verified platform and subset.

**Step 5: Commit each verified or constrained outcome separately**

A constrained result is a documentation/manifest commit, never a fake running
adapter.

### Task 4.5: Deliver the 49-template catalog

**Files:**
- Create: `templates/devices/**/*.yaml`
- Create: `templates/mappings/**/*.yaml`
- Create: `templates/catalog.json`
- Create: `docs/THIRD_PARTY_NOTICES.md`
- Test: `tests/IndustrialSim.Templates.Tests/BuiltInTemplateCatalogTests.cs`
- Test: `tests/IndustrialSim.IntegrationTests/TemplateInstantiationTests.cs`

**Step 1: Verify source license and create the failing catalog test**

Assert exactly or at least 49 unique template IDs, valid semantic versions,
attribution, tags, loadable device definitions, valid available-protocol
mappings, and no protocol addresses in device templates.

**Step 2: Run and verify failure**

Expected: FAIL because the catalog is not populated.

**Step 3: Port/recreate templates into separated artifacts**

Cover PLCs, sensors, CNC, cameras, HVAC/building, energy, security, and weighing
devices represented by the baseline. Recreate definitions where direct reuse
is unclear; preserve MIT attribution where content is derived.

**Step 4: Instantiate every template in tests**

For every mapping whose adapter is available, start the adapter and perform at
least one read. Templates for constrained protocols remain searchable but show
their unavailable/platform-constrained status.

**Step 5: Commit**

```powershell
git add templates docs/THIRD_PARTY_NOTICES.md tests
git commit -m "feat: add baseline device template catalog"
```

## Wave 5: Product parity release gate

### Task 5.1: Add demo setup, persisted settings, and operational packaging

**Files:**
- Create: `src/IndustrialSim.Application/Setup/DemoSetupService.cs`
- Create: `src/IndustrialSim.Web/Api/V1/SetupEndpoints.cs`
- Create: `src/IndustrialSim.Web/Api/V1/SettingsEndpoints.cs`
- Modify: `Dockerfile`
- Modify: `docker-compose.yml`
- Modify: `README.md`
- Test: `tests/IndustrialSim.IntegrationTests/DemoSetupTests.cs`
- Test: `tests/IndustrialSim.IntegrationTests/ContainerContractTests.cs`

**Step 1: Write failing demo/restart tests**

Provision representative devices, protocols, scenarios, tests, and integration
targets once; rerunning setup is idempotent. Update settings, restart the app,
and verify persistence plus validation of port conflicts.

**Step 2: Run and verify failure**

Expected: FAIL because baseline setup/settings are not complete.

**Step 3: Implement explicit demo and settings application**

Settings changes that require restart must say so; never rewrite source `.env`
or checked-in YAML files. Keep secrets in secret providers, not settings export.

**Step 4: Build and smoke-test container profiles**

Provide default Linux profile and an optional Windows documentation path for
OPC DA. Expected: Linux image runs without proprietary/Windows dependencies.

**Step 5: Commit**

```powershell
git add src Dockerfile docker-compose.yml README.md tests
git commit -m "build: add platform demo and operational settings"
```

### Task 5.2: Run performance, reliability, and security gates

**Files:**
- Create: `tests/IndustrialSim.LoadTests/*`
- Create: `tests/IndustrialSim.SecurityTests/*`
- Create: `docs/PERFORMANCE_BASELINE.md`
- Create: `docs/SECURITY.md`
- Create: `docs/OPERATIONS.md`
- Modify: CI workflow files

**Step 1: Add measurable tests**

Cover 100 running devices, 10,000 datapoints, API p95, bounded stream overload,
database lock/unavailability, adapter crash/restart, recording disk limit,
Webhook outage, auth brute-force controls, authorization, secret redaction,
path traversal in imports/exports, and malicious template/test payloads.

**Step 2: Run and capture the initial failures**

Do not loosen thresholds merely to obtain green status. Record environment and
measurements.

**Step 3: Fix bottlenecks and failure handling**

Prefer bounded queues, batched persistence, immutable snapshots, indexed
queries, cancellation, and adapter isolation. Do not cache live state outside
`StateStore`.

**Step 4: Run the full gate**

```powershell
dotnet restore IndustrialSim.sln
dotnet build IndustrialSim.sln --configuration Release --no-restore
dotnet test IndustrialSim.sln --configuration Release --no-build
docker build -t industrial-sim:baseline .
docker compose config
```

Expected: all supported-platform gates pass; constrained protocols are reported
truthfully.

**Step 5: Commit**

```powershell
git add tests docs .github
git commit -m "test: verify platform performance reliability and security"
```

### Task 5.3: Complete the ProtoForge baseline acceptance matrix

**Files:**
- Modify: `docs/PROTOFORGE_BASELINE_MATRIX.md`
- Modify: `docs/PROTOFORGE_COMPARISON.md`
- Modify: `README.md`
- Create: `docs/BASELINE_RELEASE_REPORT.md`

**Step 1: Collect evidence**

Link each matrix row to automated tests, interoperability records, UI/API
screens, performance results, or a documented constraint. No row becomes
`Verified` based only on source code existence.

**Step 2: Run the original and new demos**

Verify the deterministic Pump dual-protocol/fault demo, multi-device platform
demo, template instantiation, user test suite, forwarding, recording/replay,
authentication, and all supported protocol profiles.

**Step 3: Publish the report**

State clearly where IndustrialSim exceeds the baseline (unified cross-protocol
state, deterministic replay, first-class faults), matches it, or remains
constrained by platform/license/interoperability evidence.

**Step 4: Commit**

```powershell
git add docs README.md
git commit -m "docs: publish protoforge baseline verification"
```

## Recommended execution checkpoints

Stop for architecture and product review after:

1. Wave 0: scope and ADRs accepted.
2. Wave 1: multi-device persisted control plane and secured `/api/v1` work.
3. Wave 2: Web/template/scenario workflows pass browser tests.
4. Wave 3: tests, observability, integrations, recording, and SDK pass E2E.
5. Each protocol batch: manifests and interoperability evidence reviewed.
6. Wave 5: release report matches actual verification.

At every checkpoint report changed files, commit hashes, focused/full test
results, performance/security findings, protocol constraints, and any baseline
matrix row that cannot yet be verified.
