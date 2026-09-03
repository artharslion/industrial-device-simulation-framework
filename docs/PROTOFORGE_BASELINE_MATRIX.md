# ProtoForge Functional Baseline Acceptance Matrix

## Purpose

ProtoForge is the user-visible capability baseline for the post-v0.1 product.
This matrix does not authorize copying ProtoForge source, internal architecture,
or protocol-owned state. A row becomes `Verified` only when the acceptance
evidence exists and has been executed or manually recorded. Source files alone
are not acceptance evidence.

Statuses are `Not Started`, `In Progress`, `Verified`, and `Constrained`.

## Capability groups

| Capability | Owner module | Target wave | Acceptance evidence | Status |
|---|---|---:|---|---|
| devices: multi-device lifecycle and batch operations | Hosting / Application | 1 | `SimulationRegistryTests` and `V1ApiContractTests` | Verified |
| protocols: catalog, mappings, supervision, and status | Hosting / Protocols | 1, 4 | API tests plus per-protocol capability manifests and interoperability records | In Progress |
| templates: versioned definitions and mapping profiles | Templates | 2, 4 | `TemplateCatalogTests`, `TemplatePersistenceTests`, and `VisualModelingApiTests` | Verified |
| scenarios: persisted CRUD and runtime lifecycle | Application / Scenarios | 1, 2 | `ScenarioParserTests`, `VisualModelingApiTests`, and Vue editor tests | Verified |
| testing: user cases, suites, assertions, and reports | Testing | 3 | Domain, execution, API, and report tests | Not Started |
| forwarding: bounded delivery targets | Integrations | 3 | Outage, retry, backpressure, and runtime-isolation tests | Not Started |
| recording: semantic capture and replay | Integrations | 3 | Ordered deterministic replay and storage-failure tests | Not Started |
| webhooks: filtered event delivery | Integrations | 3 | CRUD, retry, dead-letter, and outage tests | Not Started |
| metrics: health, Prometheus, and tracing | Observability | 3 | Scrape, health, trace, and dropped-event tests | Not Started |
| authentication: optional identity and RBAC | Web / Persistence | 1 | `AuthenticationTests`: disabled/local modes, bootstrap, login, password, users, roles, and secret exclusion | Verified |
| settings: persisted non-secret control-plane settings | Application / Persistence | 1, 5 | Restart, validation, and secret-exclusion tests | In Progress |
| sdk: typed .NET client | Client | 3 | OpenAPI contract and in-memory-host tests | Not Started |
| Vue developer console | Web ClientApp | 1, 2 | Vitest, typecheck, production build, and desktop/mobile browser evidence | Verified |
| OpenAPI and RFC Problem Details | Web | 1 | `V1ApiContractTests` and `AuthenticationTests` with stable `errorCode` values | Verified |
| SignalR state and log streaming | Web / ClientApp | 1 | `RuntimeHubTests`, `useRuntimeSignalR.test.ts`, and polling-fallback tests | Verified |

## Protocol baseline

| Protocol | Owner module | Target wave | Acceptance evidence | Status |
|---|---|---:|---|---|
| Modbus TCP | Protocols.Modbus | v0.1 / 1 | Existing wire tests plus `/api/v1/protocols` status | Verified |
| Modbus RTU | Protocols.ModbusRtu | 4 | CRC/framing and external-client record | Not Started |
| OPC UA | Protocols.OpcUa | v0.1 / 1 | Existing external-client tests plus `/api/v1/protocols` status | Verified |
| MQTT | Protocols.Mqtt | 4 | Publish/subscribe, QoS, retained state, reconnect tests | Not Started |
| HTTP | Protocols.Http | 4 | OpenAPI-described external-client tests | Not Started |
| GB28181 | Protocols.Gb28181 | 4 | Feasibility record and verified SIP subset | Not Started |
| BACnet | Protocols.Bacnet | 4 | Discovery and property interoperability tests | Not Started |
| Siemens S7 | Protocols.S7 | 4 | DB memory typed read/write interoperability tests | Not Started |
| Mitsubishi MC | Protocols.Mc | 4 | Documented frame and device-memory tests | Not Started |
| Omron FINS | Protocols.Fins | 4 | Node and memory-area read/write tests | Not Started |
| EtherNet/IP | Protocols.EtherNetIp | 4 | Session and CIP service interoperability tests | Not Started |
| OPC DA | OpcDaHost / Protocols.OpcDa | 4 | Windows COM interoperability record | Constrained |
| FANUC FOCAS | Protocols.Fanuc | 4 | Licensed SDK feasibility and compatibility evidence | Constrained |
| MTConnect | Protocols.MtConnect | 4 | Probe/current/sample schema tests | Not Started |
| Mettler-Toledo | Protocols.Toledo | 4 | Declared command/response profile tests | Not Started |

## Wave 0 and Wave 1 release gate

Wave 0 is accepted when the normative scope, Vue architecture correction,
matrix, and ADRs are committed and documentation tests pass. Wave 1 is accepted
when automated tests cover multi-host isolation, SQLite restart and explicit
snapshots, `/api/v1`, Problem Details, OpenAPI, SignalR ordering/reconnect/
backpressure, optional authentication, user management, RBAC, secret exclusion,
and Vue fallback behavior while all v0.1 protocol and deterministic tests remain
green.

## Wave 2 visual modeling release gate

Wave 2 visual modeling is accepted when immutable local template versions and
separate mapping profiles persist across restart; templates instantiate normal
SimulationHosts; scenario YAML and editor metadata round trip independently;
and the Vue console exposes addressable Overview, Devices, Templates,
Scenarios, Protocols, Events, Users, and Settings pages. Desktop and 390px
browser inspection must show no rendering errors or horizontal page overflow.

This gate does not include a template marketplace, automated test-report
platform, forwarding, Webhooks, recording/replay, or additional protocols.
