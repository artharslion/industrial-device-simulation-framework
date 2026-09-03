# ProtoForge Baseline Expansion Design

## Status and assumption

The v0.1 implementation plan in
`docs/plans/2026-08-28-industrial-device-simulation-mvp.md` is treated as
accepted and complete for this next-phase design. This document does not reopen
that milestone. Any regression discovered while implementing the next phase is
handled as stabilization work rather than as a reason to redesign the v0.1
runtime.

“ProtoForge is the baseline” means feature-capability parity from a user's
perspective. It does not mean copying ProtoForge's Python/Vue implementation or
adopting its protocol-owned device-state model. IndustrialSim keeps its existing
architecture invariants:

- Core remains protocol-independent.
- `StateStore` remains the only live runtime-state authority.
- A logical device can be exposed through multiple protocols.
- Scenario and Fault logic targets devices and datapoints, not addresses.
- Deterministic time and seeded behavior remain first-class capabilities.

## Requirements summary

### Functional baseline

The next product line must cover the user-visible capability groups present in
ProtoForge:

1. Multi-device lifecycle and batch operations.
2. Versioned REST API, OpenAPI, live device/log streaming, and a client SDK.
3. Persistent devices, scenarios, templates, tests, users, and settings.
4. A full Web console with dashboard, devices, protocols, templates, scenarios,
   visual scenario editing, tests, integrations, logs, and settings.
5. Template search, tagging, instantiation, import/export, and a catalog of at
   least 49 validated templates.
6. Scenario CRUD, import/export, snapshots, and visual editing.
7. User-facing test cases, suites, assertions, variable extraction, generated
   tests, diagnostics, reports, and trends.
8. Structured logs, Prometheus metrics, OpenTelemetry, health checks, Webhooks,
   forwarding, and recording/replay.
9. Authentication, user management, and role-based authorization.
10. The 15 marketed protocol capabilities: Modbus TCP, Modbus RTU, OPC UA,
    MQTT, HTTP, GB28181, BACnet, Siemens S7, Mitsubishi MC, Omron FINS,
    EtherNet/IP for Rockwell AB, OPC DA, FANUC FOCAS compatibility, MTConnect,
    and Mettler-Toledo compatibility.

IndustrialSim additionally retains deterministic replay, unified cross-protocol
state, and first-class Data/Device/Network Faults as differentiators.

### Non-functional baseline

- A single-node deployment supports at least 100 concurrently running devices
  and 10,000 datapoints on a developer workstation.
- Read-only control-plane API requests target p95 below 200 ms at 50 requests
  per second, excluding protocol/network-fault delays deliberately injected by
  a scenario.
- Live update streams are bounded; overload increments a dropped-event metric
  rather than growing memory without limit.
- A protocol adapter failure cannot stop other adapters or its device runtime.
- Database unavailability cannot corrupt live `StateStore` data. Persistent
  mutations fail explicitly and running simulations continue where safe.
- Snapshot/restore and seeded replay have deterministic integration tests.
- Authentication can be disabled for local trusted development, but when
  enabled every mutating API has a documented role requirement.
- Secrets and password hashes never appear in logs, recordings, snapshots, or
  exported templates.
- Every protocol claim identifies its compatibility level and is backed by an
  external-client test or an explicit manual interoperability record.

## Approaches considered

### Option A: Expand the existing Web/Hosting projects directly

This is initially fast because it adds endpoints and HTML to existing files.
It becomes difficult to maintain once persistence, users, templates, tests,
recording, and many concurrent runtimes are added. `SimulationHost` would turn
into a platform service locator, and the risk of control-plane concerns leaking
into Runtime and protocol projects would be high.

### Option B: Modular monolith with a separate control plane — recommended

Keep the current runtime as the data plane. Add application, persistence,
testing, integration, and client modules around a multi-runtime registry. The
Web process hosts the modules together for simple deployment, while project
references enforce boundaries. This gives ProtoForge-level product breadth
without introducing distributed-system operations prematurely.

### Option C: Split protocols, control plane, and runtime into microservices

This would isolate failures and allow independent deployment, but it introduces
service discovery, remote consistency, distributed tracing, deployment
orchestration, and cross-service versioning before there is evidence that one
process is insufficient. It is rejected for the next phase.

## Recommended architecture

```text
Browser / CLI / IndustrialSim.Client SDK
                  |
                  v
IndustrialSim.Web
  - Razor Components UI
  - /api/v1 REST + OpenAPI
  - SignalR live streams
  - optional Identity/JWT boundary
                  |
                  v
IndustrialSim.Application
  - device/scenario/template/test use cases
  - authorization-independent application contracts
  - orchestration and validation
         |                     |
         v                     v
IndustrialSim.Persistence   IndustrialSim.Testing
  - EF Core SQLite           - test runner/assertions
  - catalogs/snapshots       - reports/diagnostics
         |                     |
         +----------+----------+
                    v
IndustrialSim.Hosting
  - SimulationRegistry (many SimulationHost instances)
  - lifecycle and protocol supervision
  - snapshot/replay coordination
                    |
                    v
Existing Runtime / StateStore / Scenario / Fault / Device Behavior
                    |
        +-----------+-----------------------------------+
        v           v                                   v
  OPC UA/Modbus   New protocol adapters          Integrations/recorders
```

### Project responsibilities

- `IndustrialSim.Core`, `Runtime`, `Scenarios`, and `Faults` keep their current
  responsibilities and must not reference the new control-plane modules.
- `IndustrialSim.Hosting` gains a `SimulationRegistry` that supervises multiple
  `SimulationHost` instances. A host remains the unit of live runtime state.
- `IndustrialSim.Application` owns use cases and catalog contracts. It can
  coordinate Hosting, Testing, Templates, and Integrations, but contains no EF
  Core, HTTP, Razor, or concrete protocol types.
- `IndustrialSim.Persistence` implements application repositories with EF Core
  and SQLite. Persistence stores definitions, configuration, metadata,
  snapshots, reports, and settings; it does not become the live datapoint store.
- `IndustrialSim.Templates` owns versioned, protocol-neutral device templates
  and separate protocol-mapping profiles.
- `IndustrialSim.Testing` owns user-defined test cases, suites, assertion
  execution, variable extraction, reports, trends, and test generation.
- `IndustrialSim.Integrations` owns forwarders, Webhooks, importers, and
  recording/replay storage abstractions.
- Each additional protocol stays in its own `IndustrialSim.Protocols.*`
  project and implements the existing protocol-neutral adapter contract.
- `IndustrialSim.Web` becomes the composition root, versioned API, Identity
  boundary, SignalR host, and Razor Components UI.
- `IndustrialSim.Client` is a typed .NET SDK generated or maintained against
  `/api/v1` OpenAPI contracts.

## Core data-flow decisions

### Device creation and restoration

1. The API receives or imports a device definition.
2. Application validation resolves its template and protocol mapping profiles.
3. Persistence commits the definition and desired lifecycle state.
4. `SimulationRegistry` creates a `SimulationHost` with one `StateStore`.
5. Requested adapters attach to that host.
6. Runtime events are published to logs, metrics, SignalR, Webhooks, recorders,
   and forwarding targets through bounded observer pipelines.

On restart, persisted definitions are restored first. Live state is restored
only when a compatible explicit snapshot exists; otherwise initial values are
used. Silent restoration from stale protocol-side values is forbidden.

### Templates

ProtoForge templates bind a device to a protocol. IndustrialSim templates must
instead use two composable artifacts:

```text
DeviceTemplate
  - device type, datapoints, commands, events, behavior defaults

ProtocolMappingProfile
  - protocol, addresses/nodes/topics/tags, encoding, access
```

This preserves one-device/many-protocol semantics. Template versions are
immutable after publication. Instantiation records the source version so later
template edits do not silently mutate running devices.

### Recording and replay

Recording supports two explicit levels:

- Semantic: runtime reads, writes, commands, events, scenario actions, faults,
  and protocol identity. This works for every adapter and is deterministic.
- Wire: raw frames where the protocol library and license permit capture and
  replay. It is optional and protocol-specific.

Replay never writes directly into an adapter-owned cache. Semantic replay goes
through the same application/runtime transition paths as live activity.

## Web and API design

The current single-page embedded console is replaced incrementally with ASP.NET
Core Razor Components. This avoids a second build toolchain and lets the Web
project reuse .NET contracts. SignalR replaces polling for datapoint and log
updates, with polling retained as a fallback.

The API is rooted at `/api/v1` and includes resource groups for protocols,
devices, scenarios, templates, tests, logs, metrics, authentication, forwarding,
recordings, Webhooks, setup, and settings. Existing v0.1 endpoints remain as
temporary compatibility shims for one release and emit deprecation metadata.

Mutating operations use application services rather than reaching directly
into `SimulationHost`. API errors use RFC 9457 Problem Details with stable error
codes for validation, conflict, not found, protocol, scenario, fault,
persistence, and authorization failures.

Identity uses ASP.NET Core Identity with SQLite storage and roles `Admin`,
`Operator`, and `Viewer`. Local development can set `Auth:Mode=Disabled`.
Enabled mode has no fixed default password: the first-run setup endpoint issues
or accepts one bootstrap credential, then closes permanently.

## Protocol expansion policy

Feature parity is not accepted as a folder or menu item. Every protocol gets a
capability manifest:

```text
protocol name
transport and platform constraints
implemented services/function codes
supported datatypes and mappings
security modes
known compatibility limits
external client/tool and tested version
manual or automated interoperability evidence
```

Protocols are delivered in batches:

1. Open and broadly testable: HTTP, MQTT, Modbus RTU, MTConnect.
2. Industrial controller/building protocols: BACnet, S7, MC, FINS,
   EtherNet/IP.
3. Specialized/platform-constrained: GB28181, OPC DA, FANUC FOCAS,
   Mettler-Toledo.

OPC DA is a Windows-only optional host. FANUC FOCAS and other proprietary
interfaces require a license/SDK feasibility decision before implementation.
If full interoperability is not legally or technically available, the product
must call the feature a documented compatibility profile rather than a full
server implementation.

## Failure modes and mitigations

| Failure | Required behavior |
|---|---|
| One protocol cannot bind its port | Mark only that adapter failed; keep its runtime and other adapters alive |
| SQLite is unavailable or locked | Return a persistence error for catalog writes; do not corrupt or replace live state |
| SignalR consumer is slow | Drop/coalesce bounded updates, expose counters, retain latest state snapshot |
| Forward target or Webhook is down | Retry with capped exponential backoff and dead-letter diagnostics; never block simulation ticks |
| Recording storage fills | Stop that recording, emit an event/metric, keep simulation running |
| Template version is incompatible | Reject before runtime creation with actionable validation details |
| Snapshot schema is old | Run an explicit migrator or reject; never partially restore |
| Test runner action times out | Mark the step failed and continue/stop according to the case policy |
| Authentication provider fails | Existing local runtimes continue; protected control operations fail closed |
| Protocol implementation is partial | Surface declared capability limits in API, UI, and documentation |

## Architectural decisions to record

Implementation begins by adding these ADRs:

1. Modular monolith control plane instead of expanding `SimulationHost` or
   introducing microservices.
2. SQLite persists control-plane data; `StateStore` remains live-state
   authority.
3. Razor Components and SignalR replace the embedded static console and polling
   as the primary UI model.
4. Templates separate device definitions from protocol mapping profiles.
5. Protocol capability manifests and interoperability evidence gate release.
6. Authentication is optional for trusted local mode and role-enforced when
   enabled.

## Release strategy

The implementation plan is organized as dependency-ordered waves rather than
one large “feature parity” release:

- Wave 1: platform foundation, persistence, multi-device registry, API, live
  streams, and identity.
- Wave 2: templates, scenario management, and full Web console.
- Wave 3: user-facing test automation, observability, integrations, recording,
  replay, and SDK.
- Wave 4: protocol batches and 49-template catalog.
- Wave 5: parity release gate, performance/security verification, migration,
  Docker packaging, and documentation.

Each wave must leave the existing v0.1 deterministic Pump demo working. New
features are not allowed to bypass `StateStore`, weaken deterministic tests, or
claim protocol support without evidence.
