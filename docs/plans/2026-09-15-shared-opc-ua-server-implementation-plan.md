# Shared OPC UA Server Hosting Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Status:** Completed and verified on 2026-09-15. All five implementation
tasks were committed separately; the final solution run passed 227 .NET tests,
and the Docker image plus repository OPC Foundation client acceptance checks
completed successfully. No separate third-party client run is claimed.

**Goal:** Allow multiple simulations with compatible normalized OPC UA endpoints to share one process-level server/listener while preserving per-device `StateStore` authority, routing, subscriptions, lifecycle rollback, and Network Fault isolation.

**Architecture:** Add an endpoint-keyed server manager to `IndustrialSim.Protocols.OpcUa`, convert the OPC UA server NodeManager from one fixed runtime to dynamic device projections, inject the manager into `SimulationHost`, and teach `SimulationRegistry` that one compatible OPC UA endpoint is one listener owner. Keep the existing YAML and launch-document contracts; all reads, writes, commands, and notifications continue to route through each device's existing runtime.

**Tech Stack:** .NET 10, C#, OPCFoundation.NetStandard.Opc.Ua.Server 1.5.378.134, xUnit, real OPC UA client integration tests, ASP.NET Core integration tests, Docker.

---

## Inputs and execution rules

- Read `AGENTS.md`, `docs/PROJECT_SPEC.md`, and
  `docs/plans/2026-09-15-shared-opc-ua-server-design.md` before implementation.
- Check `git status --short --branch` before each task.
- Preserve unrelated user changes and stop if a task overlaps them.
- Write and execute the named failing test before production code.
- Keep `IndustrialSim.Core`, `IndustrialSim.Runtime`, and `StateStore` free of
  concrete OPC UA types.
- Commit after every completed task using the listed message.
- Do not mark third-party OPC UA interoperability verified without an explicit
  external-client run beyond the repository's OPC Foundation client tests.

## Task 1: Add normalized endpoint identity and shared server lifecycle

**Files:**

- Create: `src/IndustrialSim.Protocols.OpcUa/OpcUaEndpointDescriptor.cs`
- Create: `src/IndustrialSim.Protocols.OpcUa/OpcUaEndpointHostManager.cs`
- Modify: `src/IndustrialSim.Protocols.OpcUa/OpcUaAdapter.cs`
- Modify: `src/IndustrialSim.Protocols.OpcUa/IndustrialOpcUaServer.cs`
- Modify: `tests/IndustrialSim.Protocols.OpcUa.Tests/ProtocolContractTests.cs`

### Step 1: Write endpoint normalization tests

Add tests proving:

```csharp
Assert.Equal(
    OpcUaEndpointDescriptor.Parse("opc.tcp://LOCALHOST:4840/"),
    OpcUaEndpointDescriptor.Parse("opc.tcp://localhost:4840"));
Assert.NotEqual(
    OpcUaEndpointDescriptor.Parse("opc.tcp://localhost:4840"),
    OpcUaEndpointDescriptor.Parse("opc.tcp://127.0.0.1:4840"));
```

Reject query, fragment, user info, invalid scheme, missing host, invalid port,
and a second different descriptor that attempts to own the same port.

### Step 2: Write failing manager lifecycle tests

Use fake/in-process device projections where possible and a real free TCP port
for listener assertions. Cover:

- first registration creates one running endpoint host;
- second compatible registration reuses the same host;
- two endpoints on different ports create two hosts;
- removing one member leaves the host running;
- removing the final member stops it and releases the port;
- duplicate start/stop is idempotent;
- concurrent register/unregister does not duplicate or prematurely stop the
  listener;
- failed registration rolls back membership and a newly created listener;
- a different descriptor on an owned port fails with an actionable exception.

### Step 3: Run tests to verify failure

```powershell
dotnet test tests/IndustrialSim.Protocols.OpcUa.Tests/IndustrialSim.Protocols.OpcUa.Tests.csproj --configuration Release --filter "Endpoint_descriptor|Shared_server_lifecycle"
```

Expected: FAIL because the descriptor and manager do not exist.

### Step 4: Implement `OpcUaEndpointDescriptor`

Implement an immutable record with:

```csharp
public sealed record OpcUaEndpointDescriptor(
    string Scheme,
    string Host,
    int Port,
    string Path,
    string Endpoint)
{
    public static OpcUaEndpointDescriptor Parse(string endpoint);
}
```

Normalize scheme/host casing and path trailing slash. Do not resolve DNS or
merge `localhost`, loopback, and wildcard hosts.

### Step 5: Implement the manager and registration handle

Implement `OpcUaEndpointHostManager : IAsyncDisposable` with a manager gate,
endpoint-host dictionary, and port-owner dictionary. Its public entry point is
similar to:

```csharp
public Task<OpcUaDeviceRegistration> RegisterAsync(
    string endpoint,
    string simulationKey,
    IDeviceRuntime runtime,
    IReadOnlyDictionary<string, string> dataPointNodeIds,
    OpcUaTransportFaultController fault,
    CancellationToken cancellationToken);
```

`OpcUaDeviceRegistration.DisposeAsync()` unregisters exactly once. The manager
starts one `ApplicationInstance` for the first member and stops it after the
last member. Expose read-only diagnostics (`HostCount`, member count, running)
for focused tests only; do not expose mutable server internals.

### Step 6: Convert adapter startup to registration

Allow `OpcUaAdapter` to receive a manager. Keep a default constructor that
creates/owns a private manager for compatibility. `StartAsync` registers its
device projection; `StopAsync` disposes its registration. Remove adapter-owned
certificate/application/listener fields and make `disconnect` no longer call
`StopWireServerAsync`.

At this task boundary the server may still expose one device; the next task
adds dynamic multi-device address space.

### Step 7: Run focused tests

```powershell
dotnet test tests/IndustrialSim.Protocols.OpcUa.Tests/IndustrialSim.Protocols.OpcUa.Tests.csproj --configuration Release
git diff --check
```

Expected: PASS. Existing single-device standard-server tests remain green.

### Step 8: Commit

```powershell
git add src/IndustrialSim.Protocols.OpcUa tests/IndustrialSim.Protocols.OpcUa.Tests
git commit -m "feat: add shared opc ua server lifecycle"
```

Rollback: reverting this commit restores per-adapter listeners because no
hosting/configuration contract has changed yet.

## Task 2: Add dynamic multi-device address space and runtime routing

**Files:**

- Modify: `src/IndustrialSim.Protocols.OpcUa/IndustrialOpcUaServer.cs`
- Modify: `src/IndustrialSim.Protocols.OpcUa/OpcUaEndpointHostManager.cs`
- Modify: `src/IndustrialSim.Protocols.OpcUa/OpcUaAdapter.cs`
- Modify: `tests/IndustrialSim.Protocols.OpcUa.Tests/ProtocolContractTests.cs`

### Step 1: Write failing real-client multi-device tests

Start two adapters with the same manager/endpoint and two runtimes that both
define `speed`. With one OPC Foundation client session, assert:

```csharp
await session.ReadValueAsync(new NodeId("device-a/speed", 2));
await session.ReadValueAsync(new NodeId("device-b/speed", 2));
```

Browse `Objects/IndustrialSim/Devices` and find both device objects. Write
`device-a/speed` and assert only runtime A changes. Invoke `device-a/start` and
assert only runtime A records a command. Subscribe to both values, update both
StateStores, and assert notifications retain the correct NodeId/value pairing.

Add tests for custom NodeId collision, device removal, remaining-device access,
stable re-registration NodeIds, and concurrent updates without cross-routing.

### Step 2: Run tests to verify failure

```powershell
dotnet test tests/IndustrialSim.Protocols.OpcUa.Tests/IndustrialSim.Protocols.OpcUa.Tests.csproj --configuration Release --filter "Shared_endpoint_exposes|Shared_endpoint_routes|Shared_endpoint_subscriptions|Shared_endpoint_removes"
```

Expected: FAIL because the server NodeManager is bound to one runtime.

### Step 3: Introduce device projection records

Create an internal immutable projection containing simulation key, runtime,
NodeId mappings, and transport fault controller. Create a NodeManager-owned
device node set containing all folder, variable, method, routing, and event
subscription resources for one projection.

### Step 4: Build the shared hierarchy

Create framework roots once:

```text
Objects/IndustrialSim/Devices/{deviceId}/Metadata
Objects/IndustrialSim/Devices/{deviceId}/State
Objects/IndustrialSim/Devices/{deviceId}/Datapoints
Objects/IndustrialSim/Devices/{deviceId}/Commands
Objects/IndustrialSim/Devices/{deviceId}/Faults
```

Keep datapoint and command NodeIds compatible with the existing contract.
Folder NodeIds use reserved `$`-prefixed suffixes. Validate all NodeIds against
the shared namespace before adding the device.

### Step 5: Route services to the owning runtime

Read callbacks call the projection runtime's `Read`; write callbacks call
`Write`; method callbacks call `InvokeCommandAsync`. Convert state transition
errors to OPC UA status without mutating any protocol cache as authority.
Runtime events update only the matching variable and call `ClearChangeMasks`.

### Step 6: Implement safe dynamic removal

Serialize add/remove under the NodeManager lock. Detach the runtime event
handler before deleting the subtree and routing entries. Ensure stale callbacks
become no-ops. Remove monitored nodes through SDK-supported address-space
deletion and verify old NodeIds return a bad/unknown status.

### Step 7: Run focused tests

```powershell
dotnet test tests/IndustrialSim.Protocols.OpcUa.Tests/IndustrialSim.Protocols.OpcUa.Tests.csproj --configuration Release
git diff --check
```

Expected: PASS, including the existing scalar datatype, method, event, and
single-device tests.

### Step 8: Commit

```powershell
git add src/IndustrialSim.Protocols.OpcUa tests/IndustrialSim.Protocols.OpcUa.Tests
git commit -m "feat: expose multiple devices through shared opc ua address space"
```

Rollback: the lifecycle manager from Task 1 can still be reverted with this
commit as a pair; no Core or YAML changes are involved.

## Task 3: Integrate SimulationHost and SimulationRegistry endpoint ownership

**Files:**

- Modify: `src/IndustrialSim.Hosting/SimulationHost.cs`
- Modify: `src/IndustrialSim.Hosting/SimulationRegistry.cs`
- Modify: `src/IndustrialSim.Hosting/DeviceLaunchDefinition.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Modify: `tests/IndustrialSim.IntegrationTests/SimulationRegistryTests.cs`
- Modify: `tests/IndustrialSim.IntegrationTests/RuntimeCompositionTests.cs`
- Modify: `tests/IndustrialSim.IntegrationTests/WebCreatedProtocolDeviceTests.cs`

### Step 1: Write failing registry reservation tests

Add tests that create two launch definitions using the exact same normalized
OPC UA endpoint and assert both are accepted. Also assert:

- OPC UA versus Modbus on the same port is rejected;
- two OPC UA descriptors with the same port but different host/path are
  rejected;
- stopping one started member leaves the other running and port unavailable to
  Modbus;
- removing the final reserved member allows a new protocol owner;
- replacement and failed control-plane commits restore endpoint membership.

### Step 2: Write failing end-to-end host tests

Through `SimulationRegistry`, start two devices on one free OPC UA endpoint.
Connect one real client, browse/read/write both, stop device A, and verify
device B remains available. Verify a device whose later Modbus startup fails
unregisters only its own OPC UA projection and leaves an existing shared member
accessible.

### Step 3: Run tests to verify failure

```powershell
dotnet test tests/IndustrialSim.IntegrationTests/IndustrialSim.IntegrationTests.csproj --configuration Release --filter "SimulationRegistryTests|Shared_opcua_endpoint"
```

Expected: FAIL because duplicate ports are owned by one simulation and each
host constructs an independent adapter/manager.

### Step 4: Inject the endpoint manager into hosts

Add an internal/public factory overload that accepts an
`OpcUaEndpointHostManager`. `SimulationHost` creates `OpcUaAdapter(manager)`.
Direct `Create`/`LoadAsync` overloads allocate a private manager owned by the
host. Registry-created and registry-added hosts use the registry's shared
manager; the Web composition root gets the same behavior through its registry.

Ensure host disposal releases an owned private manager only after its adapter
has stopped. A registry owns and disposes its process-level manager after all
simulations are removed.

### Step 5: Replace port reservations with listener-owner reservations

Extend `ProtocolPortBinding` or add an OPC UA-specific listener key so registry
validation can distinguish:

```text
modbus:{simulationId}
opcua:{normalized endpoint descriptor}
```

Keep a port reservation entry with protocol and member set. Compatible OPC UA
members join it; all other collisions fail. Normalize bindings once and use
the same logic in create, add, replace rollback, remove, and disposal.

### Step 6: Preserve partial-start rollback

Keep protocol start order deterministic. When a later adapter fails, stop all
adapters started by that host in reverse order. OPC UA stop unregisters only
that device. The shared endpoint remains running while another member exists.

### Step 7: Run focused and regression tests

```powershell
dotnet test tests/IndustrialSim.IntegrationTests/IndustrialSim.IntegrationTests.csproj --configuration Release --filter "SimulationRegistryTests|RuntimeCompositionTests|WebCreatedProtocolDeviceTests|CrossProtocolRuntimeFlowTests"
dotnet test tests/IndustrialSim.Web.Tests/IndustrialSim.Web.Tests.csproj --configuration Release --filter "V1ApiContractTests|ConfigurationLoadingTests"
git diff --check
```

Expected: PASS. The existing single-device boot host, real OPC UA client, and
Modbus conflict behavior remain green.

### Step 8: Commit

```powershell
git add src/IndustrialSim.Hosting src/IndustrialSim.Web tests/IndustrialSim.IntegrationTests tests/IndustrialSim.Web.Tests
git commit -m "refactor: integrate simulation hosts with shared opc ua server"
```

Rollback: reverting this commit returns registry ownership to per-simulation
ports; the protocol project remains independently testable.

## Task 4: Isolate OPC UA Network Faults on shared endpoints

**Files:**

- Modify: `src/IndustrialSim.Protocols.OpcUa/IndustrialOpcUaServer.cs`
- Modify: `src/IndustrialSim.Protocols.OpcUa/OpcUaAdapter.cs`
- Modify: `tests/IndustrialSim.Protocols.OpcUa.Tests/ProtocolContractTests.cs`
- Modify: `tests/IndustrialSim.IntegrationTests/RuntimeCompositionTests.cs`
- Modify: `tests/IndustrialSim.IntegrationTests/WebCreatedProtocolDeviceTests.cs`

### Step 1: Write failing fault-isolation tests

With two devices on one endpoint and one client session:

- apply `disconnect` to device A and assert A reads/writes/methods return
  `BadNotConnected` while B remains good;
- apply `timeout`/`latency` to A and assert only A is delayed/rejected;
- keep ticking A and B and assert runtime state advances;
- subscribe to both, fault A, update both, and assert B notifications continue;
- recover A and assert its latest StateStore value is readable/notified;
- unregister/stop A while faulted and assert B remains connected.

### Step 2: Run tests to verify failure

```powershell
dotnet test tests/IndustrialSim.Protocols.OpcUa.Tests/IndustrialSim.Protocols.OpcUa.Tests.csproj --configuration Release --filter "Shared_network_fault"
```

Expected: FAIL until all service and notification paths use the per-device
controller.

### Step 3: Complete per-device service gating

Ensure every datapoint read/write and command method resolves its projection
before calling `BeforeService`. Do not sleep or return bad status for another
projection. Remove all listener-stop/restart behavior from disconnect/recovery.

### Step 4: Gate notifications and publish recovery state

Suppress change-mask notification for the target projection while disconnect
or timeout is active. On recovery, re-read each target datapoint from the
runtime, update the OPC UA node notification value/status/timestamp, and clear
change masks once. Do not enqueue work on the simulation tick or hold a runtime
state lock during network delay.

### Step 5: Run focused and affected fault tests

```powershell
dotnet test tests/IndustrialSim.Protocols.OpcUa.Tests/IndustrialSim.Protocols.OpcUa.Tests.csproj --configuration Release
dotnet test tests/IndustrialSim.IntegrationTests/IndustrialSim.IntegrationTests.csproj --configuration Release --filter "RuntimeCompositionTests|ProtocolSharedStateTests|CrossProtocolRuntimeFlowTests|WebCreatedProtocolDeviceTests"
dotnet test tests/IndustrialSim.Faults.Tests/IndustrialSim.Faults.Tests.csproj --configuration Release
git diff --check
```

Expected: PASS. Network Fault never stops a simulation tick or another shared
device.

### Step 6: Commit

```powershell
git add src/IndustrialSim.Protocols.OpcUa tests/IndustrialSim.Protocols.OpcUa.Tests tests/IndustrialSim.IntegrationTests
git commit -m "fix: isolate opc ua faults on shared endpoints"
```

Rollback: reverting this commit is unsafe while shared endpoints are enabled
because the old disconnect semantic closes the listener. If rollback is
required, revert Tasks 2-4 together or fail shared disconnect configurations.

## Task 5: Update public contracts, examples, and acceptance evidence

**Files:**

- Modify: `docs/PROJECT_SPEC.md`
- Modify: `README.md`
- Modify: `docs/STARTUP_GUIDE.md`
- Modify: `docs/STARTUP_GUIDE.zh-CN.md`
- Modify: `docs/USER_MANUAL.md`
- Modify: `docs/USER_MANUAL.zh-CN.md`
- Modify: `docs/IMPLEMENTATION_NOTES.md`
- Modify: `docs/PROTOFORGE_COMPARISON.md`
- Modify: `docs/PROTOFORGE_BASELINE_MATRIX.md`
- Modify: `docs/plans/2026-09-15-shared-opc-ua-server-design.md`
- Modify: `docs/plans/2026-09-15-shared-opc-ua-server-implementation-plan.md`
- Create: `examples/devices/pump-shared-opcua.yaml`
- Create: `examples/devices/sensor-shared-opcua.yaml`
- Modify: `tests/IndustrialSim.Configuration.Tests/YamlConfigurationTests.cs`
- Modify: `tests/IndustrialSim.IntegrationTests/DocumentationContractTests.cs`
- Modify: `tests/IndustrialSim.IntegrationTests/ContainerContractTests.cs`
- Modify: `docker-compose.yml` only if a second example device is wired into the
  default container workflow without changing its single-process model.

### Step 1: Write failing documentation/configuration contracts

Assert the specification and guides state:

- compatible equal normalized endpoints share one server/listener;
- the final browse hierarchy;
- NodeId compatibility and collision behavior;
- server-level certificate/ApplicationUri ownership;
- same-port different-path/host incompatibility;
- device-scoped Network Fault status behavior;
- one published Docker OPC UA port can expose several devices;
- interoperability evidence is limited to repository automated/manual results.

Load both new YAML examples and assert they retain the existing public schema
and resolve to the same normalized endpoint.

### Step 2: Run tests to verify failure

```powershell
dotnet test tests/IndustrialSim.Configuration.Tests/IndustrialSim.Configuration.Tests.csproj --configuration Release
dotnet test tests/IndustrialSim.IntegrationTests/IndustrialSim.IntegrationTests.csproj --configuration Release --filter "DocumentationContractTests|ContainerContractTests"
```

Expected: FAIL because public documentation still says ports must be unique.

### Step 3: Update specification and user documentation

Add a post-v0.1 shared OPC UA hosting subsection to `PROJECT_SPEC.md`. Update
startup/manual text and examples without changing the YAML schema. Document
that custom NodeIds must be globally unique within one endpoint and that a
different host/path on the same port is rejected by this implementation.

### Step 4: Record evidence truthfully

Update implementation notes with exact commands, test counts, Docker result,
and manual OPC Foundation client steps. Update ProtoForge documents only for
capabilities proven by executed tests. Do not call third-party interoperability
verified unless an external GUI/client was actually used.

### Step 5: Run complete automated verification

```powershell
dotnet restore IndustrialSim.sln
dotnet build IndustrialSim.sln --configuration Release --no-restore
dotnet test IndustrialSim.sln --configuration Release --no-build
docker compose config
git diff --check
```

If `docker info` succeeds, also run:

```powershell
docker build -t industrial-sim:shared-opcua .
```

Expected: all supported checks pass. Record an unavailable daemon as an
unexecuted verification with the exact reason.

### Step 6: Execute manual acceptance

Use two registry-created simulations sharing
`opc.tcp://127.0.0.1:<free-port>` and the repository OPC Foundation client:

1. confirm one TCP listener;
2. browse both devices;
3. read both same-named datapoints;
4. write device A and confirm B is unchanged;
5. fault A and confirm B remains accessible;
6. stop A and confirm B remains accessible;
7. stop B and confirm the port can be rebound.

If a trusted third-party OPC UA client is available, repeat browse/read/write
and record its name/version. Otherwise state explicitly that it was not run.

### Step 7: Mark design/plan completed and commit

```powershell
git add docs README.md examples tests/IndustrialSim.Configuration.Tests tests/IndustrialSim.IntegrationTests docker-compose.yml
git commit -m "docs: document shared opc ua endpoint hosting"
```

Rollback: YAML remains backward compatible. Operational rollback requires
returning to distinct device endpoints/ports before reverting Tasks 2-4.

## Final delivery checklist

- List all new commits and hashes with purpose.
- List changed files grouped by commit.
- Describe the endpoint-host ownership change and final address space.
- State that YAML remains compatible and enumerate new validation failures.
- Report focused and full automated results with exact counts.
- Report Docker build and manual acceptance results separately.
- State final shared-endpoint Network Fault semantics.
- State whether third-party OPC UA interoperability was actually verified.
- Identify remaining concurrency, SDK, certificate, or operational risks.
- Recommend merge only if the worktree is clean and all required supported
  checks are green.
