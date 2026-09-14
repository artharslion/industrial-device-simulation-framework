# ProtoForge Functional Baseline Acceptance Matrix

## Purpose

ProtoForge is the user-visible capability baseline for the post-v0.1 product.
This matrix does not authorize copying ProtoForge source, internal architecture,
or protocol-owned state. A row becomes `Verified` only when the acceptance
evidence exists and has been executed or manually recorded. Source files alone
are not acceptance evidence.

Statuses are `Not Started`, `In Progress`, `Verified`, and `Constrained`.

## Current evidence snapshot

The 2026-09-14 release evidence gate is verified at IndustrialSim commit
`9d480d2`. GitHub Actions CI run
[`34801843690`](https://github.com/artharslion/industrial-device-simulation-framework/actions/runs/34801843690)
passed its application/client and container build/smoke jobs, including 178
.NET tests, 28 Vue tests, the production client build, Compose validation,
image construction, and `/api/runtime` smoke. Manual release run
[`34802093256`](https://github.com/artharslion/industrial-device-simulation-framework/actions/runs/34802093256)
published Docker Hub tag `ci-smoke` with digest
`sha256:3b90a83c8631e7e39828a47b26cd996c095c650a69a45bc648fada4ec113b790`.
Wave 3.1 observability is verified through IndustrialSim commit `5fe021e`.
The local acceptance run passed 208 .NET tests and 28 Vue tests plus the Vue
production build. Source-host and newly built Docker image checks returned HTTP
200 for `/health/live`, `/health/ready`, and `/metrics`; the Docker check omitted
an explicit SQLite connection string and created the writable default database
as UID 1654. The scrape exposed all seven contracted metric families.
ProtoForge remains at public commit `14b4e35`; its protocol breadth is treated
as a public/static baseline, not as equivalent interoperability evidence.

## Capability groups

| Capability | Owner module | Target wave | Acceptance evidence | Status |
|---|---|---:|---|---|
| devices: multi-device lifecycle, launch, restore, and batch operations | Hosting / Application | 1 | `SimulationRegistryTests`, `V1ApiContractTests`, and catalog restore tests | Verified |
| protocols: catalog, mappings, supervision, and status | Hosting / Protocols | 1, 4 | API tests plus per-protocol capability manifests and interoperability records | In Progress |
| templates: versioned definitions and executable mapping profiles | Templates | 2, 4 | `TemplateCatalogTests`, `TemplatePersistenceTests`, `VisualModelingApiTests`, and real-client template-instance access | Verified |
| scenarios: persisted CRUD and runtime lifecycle | Application / Scenarios | 1, 2 | `ScenarioParserTests`, `VisualModelingApiTests`, and Vue editor tests | Verified |
| testing: user cases, suites, assertions, and reports | Testing | 3 | Domain, execution, API, and report tests | Not Started |
| forwarding: bounded delivery targets | Integrations | 3 | Outage, retry, backpressure, and runtime-isolation tests | Not Started |
| recording: semantic capture and replay | Integrations | 3 | Ordered deterministic replay and storage-failure tests | Not Started |
| webhooks: filtered event delivery | Integrations | 3 | CRUD, retry, dead-letter, and outage tests | Not Started |
| metrics: health, Prometheus, and tracing | Observability | 3 | `RuntimeEventLogTests`, `RuntimeIsolationTests`, `IndustrialSimMetricsTests`, `HealthEndpointTests`, `PrometheusMetricsTests`, `TraceCorrelationTests`, and `TraceRedactionTests`; source and Docker endpoint checks | Verified |
| authentication: optional identity and RBAC | Web / Persistence | 1 | `AuthenticationTests`: disabled/local modes, bootstrap, login, password, users, roles, and secret exclusion | Verified |
| settings: persisted non-secret control-plane settings | Application / Persistence | 1, 5 | Restart, validation, and secret-exclusion tests | In Progress |
| sdk: typed .NET client | Client | 3 | OpenAPI contract and in-memory-host tests | Not Started |
| Vue developer console | Web ClientApp | 1, 2 | Vitest, typecheck, production build, and desktop/mobile browser evidence | Verified |
| OpenAPI and RFC Problem Details | Web | 1 | `V1ApiContractTests` and `AuthenticationTests` with stable `errorCode` values | Verified |
| SignalR state and log streaming | Web / ClientApp | 1 | `RuntimeHubTests`, `useRuntimeSignalR.test.ts`, and polling-fallback tests | Verified |
| release evidence: CI, server integration, container smoke, and image publication | Delivery | 2.5 | GitHub Actions run `34801843690`, `WebApplicationFactoryTests`, container smoke, and Docker Hub run `34802093256` / `ci-smoke` digest | Verified |

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

## Wave 2.5 release evidence gate

Wave 2.5 is accepted when pull requests run the complete .NET and Vue checks on
standard GitHub-hosted Ubuntu runners; server integration tests execute the
real `Program` composition root through `Microsoft.AspNetCore.Mvc.Testing`;
Docker configuration and image construction succeed; and tags or an explicit
manual dispatch can publish an immutable image tag to Docker Hub. CI must not
require Docker Hub credentials for pull requests, and publication credentials
must be read only from GitHub Actions secrets.

Accepted on 2026-09-14 at `9d480d2`. The repository has pull-request/push CI on
`ubuntu-latest`; the successful push run executed the same two configured jobs.
`WebApplicationFactoryTests` exercised the production composition root, the CI
container smoke passed, and manual dispatch published the immutable `ci-smoke`
digest recorded above. A formal semantic-version tag has not yet been cut and
is not claimed as evidence here.

## Wave 3.1 observability release gate

Wave 3.1 is accepted when runtime observations use bounded non-blocking ingress,
retention, filtering, and subscriber delivery; overload is counted; health and
Prometheus endpoints have distinct tested semantics; HTTP control operations
correlate to retained events through OpenTelemetry trace IDs; secrets are
redacted before retained or custom tracing surfaces; and blocked observation
cannot delay deterministic ticks or stop real-time simulation.

Accepted on 2026-09-14 through `5fe021e`. `RuntimeEventLogTests` cover ordered
retention, filtering, subscribers, concurrency, and drops. `RuntimeIsolationTests`
cover blocked formatting and unread subscribers. `HealthEndpointTests` prove
SQLite failure makes readiness return 503 while liveness remains 200 and a
stopped device remains ready. `PrometheusMetricsTests` and
`IndustrialSimMetricsTests` cover the exact seven metric families, fixed label
vocabularies, tick collection, state/scenario/fault changes, and runtime-log and
SignalR drops. `TraceCorrelationTests` links the ASP.NET server span, the
`industrial.state.write` span, and its retained event; `TraceRedactionTests`
excludes raw credentials from custom tags.

The full local acceptance run passed 208 .NET tests, 28 Vue tests, and the Vue
production build. A source-host run and local image `industrial-sim:wave31`
(`sha256:a6ed5bc949dbb09f9a55cd85e9eab55688f9b7abf412e77c7ec1618b6b0c2e69`)
returned 200 for `/health/live`, `/health/ready`, and `/metrics`. The container
was run without `ConnectionStrings__IndustrialSim`, used UID 1654, had a
writable `/app/data`, and created `/app/data/industrial-sim.db`. This gate does
not claim new protocol interoperability or a published Wave 3.1 release image.
