# Wave 3.1 Observability Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use the Code workflow to implement this plan task-by-task.

**Goal:** Add a bounded structured runtime event log, liveness/readiness health checks, Prometheus metrics, OpenTelemetry tracing, dropped-event accounting, and secret redaction without allowing observability to own state or block simulation ticks.

**Architecture:** `IndustrialSim.Observability` is a process-local observer over `SimulationRegistry` and `SimulationHost`. Runtime callbacks perform only immutable fact capture, sequence allocation, constant-time counters, and non-blocking bounded-channel `TryWrite`; a single background reader redacts and retains events, then offers them to bounded subscribers. Web composes the observer, exposes event/health/metrics surfaces, and registers batch OpenTelemetry export. StateStore remains the only live-state authority.

**Tech Stack:** .NET 10, `System.Threading.Channels`, ASP.NET Core health checks, `prometheus-net.AspNetCore` 8.2.1, OpenTelemetry 1.18.0, xUnit, `WebApplicationFactory`/TestServer.

---

## 1. Current-code review and Task 3.1 delta

The 2026-09-02 Task 3.1 is directionally correct but is too coarse for the
current repository:

- `SimulationHost` retains every runtime and fault event forever in an
  unbounded `ConcurrentQueue<object>` exposed through `Host.Events`.
- `RuntimeStreamBroker` already uses bounded per-subscriber channels and counts
  dropped datapoint events, but it owns a second sequence and only observes
  `DataPointChanged`.
- `/api/v1/devices/{deviceId}/events` and device details read the host queue,
  while SignalR reads the broker. They are not one event stream.
- `ScenarioRunner.ActionExecuted` exists, but the host does not relay it for
  platform observation. Tick and protocol lifecycle facts also lack read-only
  observer hooks.
- `Program.cs` starts the boot simulation before `RunAsync`; the event observer
  must be explicitly resolved and attached before `simulation.StartAsync` or
  it can miss `DeviceStarted`.
- Health checks, Prometheus, ActivitySource registration, and OTLP configuration
  do not exist.
- One implementation commit is too large. Wave 3.1 will use independently
  verified event, health, metrics, and tracing commits.

No part of this plan adds protocols, user testing, durable event persistence,
authentication behavior, Playwright, or continuous SQLite live-state storage.

## 2. Requirements and non-functional constraints

### Functional requirements

- Retain a configurable number of structured runtime observations in process.
- Query by device, event type, minimum sequence, and maximum result count.
- Subscribe to live filtered events with commit-ordered sequence numbers.
- Count ingress and subscriber drops explicitly.
- Expose unauthenticated `/health/live`, `/health/ready`, and `/metrics`.
- Emit the required Prometheus series with stable names.
- Produce correlated OpenTelemetry server and IndustrialSim operation spans.
- Attach `traceId` and `spanId` to events produced inside an active operation.
- Redact secrets before an event or custom tag becomes observable.

### Non-functional requirements

- Simulation ticks never wait for a channel, subscriber, logger, exporter,
  database, HTTP call, or disk write.
- Retention and subscriber memory are bounded by configuration with safe
  defaults and validated positive limits.
- Event order is a monotonic process-local sequence. Drops create visible gaps;
  restart resets the sequence because Wave 3.1 is not durable recording.
- Core and Runtime gain no package or project reference to Observability.
- Metrics have bounded label cardinality. Trace attributes may contain resource
  identifiers after redaction, but never request bodies or arbitrary metadata.
- SQLite stores no continuous live events. Database readiness checks are reads
  and do not make SQLite an event sink.

## 3. Module boundaries

```text
Core / Runtime / Scenarios / Faults / Protocols
                    |
                    v
                 Hosting
          read-only observation hooks
                    |
                    v
        IndustrialSim.Observability
        - bounded event pipeline
        - event query/subscription
        - redaction
        - metrics instruments
        - ActivitySource wrapper
                    |
                    v
                   Web
        - DI and startup attachment
        - event API / SignalR mapping
        - health / metrics endpoints
        - OpenTelemetry registration
```

Allowed new references:

- `IndustrialSim.Observability -> IndustrialSim.Hosting`
- `IndustrialSim.Web -> IndustrialSim.Observability`
- Observability tests -> Observability and existing domain/hosting projects

Forbidden new references:

- Core, Runtime, Application, Hosting, or protocols -> Observability
- Observability -> Web or Persistence
- Observability -> EF Core or SQLite

## 4. Event data flow and contracts

The canonical envelope is a record with:

```csharp
public sealed record RuntimeEventEnvelope(
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    TimeSpan SimulationTime,
    string DeviceId,
    string EventType,
    JsonElement Data,
    IReadOnlyDictionary<string, string> Metadata,
    string? TraceId,
    string? SpanId);
```

Supported Wave 3.1 sources are runtime events, fault lifecycle changes,
scenario action completion, and protocol lifecycle/error facts. Tick facts feed
metrics only and are not placed in retained logs.

Data path:

```text
StateStore / command / fault / scenario / protocol
    -> existing domain transition or lightweight host observer event
    -> snapshot immutable candidate + sequence + Activity context
    -> bounded ingress channel TryWrite
       -> failure: increment dropped(stage=ingress), return immediately
    -> single background reader
       -> secret redaction
       -> bounded retention ring (oldest normal record evicted)
       -> event-derived metrics
       -> subscriber filter + subscriber TryWrite
          -> failure: increment dropped(stage=subscriber), continue
    -> REST query and SignalR subscriptions
```

Retention eviction is normal bounded retention and is not counted as overload.
Ingress/subscriber rejection is overload and is counted. A subscriber cannot
execute user code on the event pump; it only receives a channel reader.

Default limits:

- ingress capacity: 2,048 candidates;
- retention: 1,000 envelopes;
- subscriber capacity: 256 envelopes;
- API query maximum: 1,000 envelopes.

Options use `IndustrialSim:Observability:*` and reject zero/negative values at
startup. Channels use `BoundedChannelFullMode.Wait` with `TryWrite` only; the
code never calls `WriteAsync` from runtime callbacks. This gives a reliable
false result for dropped-event accounting.

`SimulationHost.Events` remains temporarily for direct host compatibility but
becomes a bounded 1,000-entry snapshot. `/api/v1` and SignalR stop using it.

## 5. Metrics contract and label limits

The `/metrics` scrape must contain these exact series:

| Metric | Type | Allowed labels |
|---|---|---|
| `industrial_simulation_ticks_total` | counter | `mode=deterministic|realtime` |
| `industrial_device_state_changes_total` | counter | none |
| `industrial_scenario_actions_total` | counter | `action=set|ramp|command|wait|fault|unknown` |
| `industrial_faults_active` | gauge | `category=data|device|network` |
| `industrial_protocol_connections` | gauge | `protocol=opcua|modbus|other` |
| `industrial_protocol_errors_total` | counter | `protocol=opcua|modbus|other`, `operation=start|stop|read|write|command|other` |
| `industrial_stream_events_dropped_total` | counter | `stream=runtime-log|signalr`, `stage=ingress|subscriber` |

Device IDs, device types, datapoint names, scenario names, fault IDs, ports,
exception types/messages, user names, and arbitrary metadata are forbidden as
metric labels. Unknown future protocol/action values collapse to `other` or
`unknown` until the bounded vocabulary is deliberately extended.

Tick callbacks and event callbacks perform only in-memory metric increments;
scraping and exporter work occur on HTTP/request or exporter worker threads.

## 6. Health semantics

`GET /health/live` answers whether the Web process can execute requests. It is
healthy once the app is running and does not depend on SQLite, any device
running, protocol listeners, OTLP, Prometheus scraping, or event backlog. A
dependency outage must not cause a restart loop that kills healthy simulations.

`GET /health/ready` answers whether the control plane is ready for normal API
work. It is healthy only when:

- the boot registry exists and can be queried;
- the observability event pump is running and has not faulted;
- a scoped `IndustrialSimDbContext.Database.CanConnectAsync` succeeds.

Readiness does not require a device to be Running and does not fail for an
intentional Network Fault or an unavailable OTLP collector. Unhealthy readiness
returns HTTP 503 with a small JSON body naming checks but excluding exception
messages, connection strings, file paths, or secrets. Both endpoints are
outside authenticated route groups so container orchestrators can call them.

## 7. Tracing boundaries and correlation

Register `IndustrialSim.Observability` ActivitySource plus ASP.NET Core and HTTP
client instrumentation. Add optional OTLP export when
`OpenTelemetry:Otlp:Endpoint` is configured; use the SDK batch processor only.
With no endpoint configured, the app performs no external trace export.

Create custom spans for bounded control operations:

- `industrial.device.create|update|remove|lifecycle`;
- `industrial.state.write`;
- `industrial.scenario.start|stop`;
- `industrial.fault.activate|recover`;
- `industrial.protocol.start|stop` where the host owns the lifecycle call.

Do not create a span for every simulation tick or behavior-driven datapoint
change. That would add high-volume overhead and distort deterministic runtime
work. Tick counts are metrics.

Custom trace tags are allowlisted: operation, lifecycle action, result,
device ID, protocol category, and error code. Values pass through secret
redaction. Request/response bodies, YAML, connection strings, credentials,
event metadata, datapoint values, and exception messages are not trace tags.

The event ingress captures `Activity.Current.TraceId` and `SpanId` before the
operation scope ends. A state write performed by an HTTP request therefore has
the ASP.NET server trace as parent, an IndustrialSim operation span, and a
retained event envelope with the same trace ID. Events caused later by an
independent deterministic tick have no fabricated request correlation.

## 8. Secret redaction

`SecretRedactor` is applied before retention, subscribers, API serialization,
observability-owned log messages, and custom activity tags. It does not mutate
the original domain event.

Case-insensitive secret-like keys include `password`, `passwd`, `pwd`,
`secret`, `token`, `access_token`, `refresh_token`, `api_key`, `apikey`,
`authorization`, `cookie`, `connectionstring`, `clientsecret`, and
`privatekey`. Separators (`-`, `_`, `.`, `:`) are normalized for comparison.

Text redaction covers Bearer credentials, URI user-info, and common connection
string fields such as `Password=`/`Pwd=`. Replacements use `[REDACTED]`.
Datapoint values are redacted when the datapoint name itself is secret-like.
Unknown arbitrary object graphs are not retained; envelope factories serialize
only supported event shapes with bounded metadata counts/value lengths.

This scope protects IndustrialSim-owned observability surfaces. It does not
claim to rewrite every log produced internally by third-party libraries.

## 9. Implementation tasks and commits

### Task 1: Add the module, envelope, and redaction contract

**Files:**

- Create: `src/IndustrialSim.Observability/IndustrialSim.Observability.csproj`
- Create: `src/IndustrialSim.Observability/Events/RuntimeEventEnvelope.cs`
- Create: `src/IndustrialSim.Observability/Events/RuntimeEventEnvelopeFactory.cs`
- Create: `src/IndustrialSim.Observability/Security/SecretRedactor.cs`
- Create: `tests/IndustrialSim.Observability.Tests/IndustrialSim.Observability.Tests.csproj`
- Create: `tests/IndustrialSim.Observability.Tests/SecretRedactionTests.cs`
- Modify: `IndustrialSim.sln`
- Modify: `tests/IndustrialSim.Application.Tests/ArchitectureBoundaryTests.cs`

1. Write tests proving supported runtime/fault/scenario/protocol events become
   immutable envelopes and secret metadata/value/text is `[REDACTED]`.
2. Run the tests and confirm failure because the module does not exist.
3. Add the projects and minimum envelope/redactor implementation.
4. Assert Core/Runtime/Application/Hosting projects do not reference
   Observability, Web, EF Core, or exporters unexpectedly.
5. Run Observability and architecture tests.
6. Commit:

```powershell
git add IndustrialSim.sln src/IndustrialSim.Observability tests/IndustrialSim.Observability.Tests tests/IndustrialSim.Application.Tests
git commit -m "feat: define structured observability events"
```

### Task 2: Add bounded retention, filters, subscribers, and runtime isolation

**Files:**

- Create: `src/IndustrialSim.Observability/Events/RuntimeEventLog.cs`
- Create: `src/IndustrialSim.Observability/Events/RuntimeEventLogOptions.cs`
- Create: `src/IndustrialSim.Observability/Events/RuntimeEventQuery.cs`
- Create: `src/IndustrialSim.Observability/Events/RuntimeEventSubscription.cs`
- Create: `src/IndustrialSim.Hosting/RuntimeObservation.cs`
- Modify: `src/IndustrialSim.Hosting/SimulationHost.cs`
- Create: `tests/IndustrialSim.Observability.Tests/RuntimeEventLogTests.cs`
- Create: `tests/IndustrialSim.Observability.Tests/RuntimeIsolationTests.cs`
- Modify: `tests/IndustrialSim.IntegrationTests/RuntimeCompositionTests.cs`

1. Write failing retention/filter/subscriber tests covering oldest eviction,
   sequence order, device/type filters, since-sequence, maximum count,
   unsubscribe completion, and concurrent producers.
2. Write a failing dropped-event test with capacities of 1 and a deliberately
   unconsumed subscriber; assert ingress/subscriber counters increase.
3. Write a failing simulation/runtime isolation test whose subscriber never
   reads and whose event formatter is held behind a test gate; run many
   deterministic ticks and assert they finish without waiting for the gate.
4. Add host observation hooks for scenario action, tick, and protocol lifecycle
   facts. Relays must be invoked after the underlying runtime operation and
   must not expose mutation callbacks.
5. Implement the bounded ingress/background-reader/retention/subscriber flow.
6. Bound the compatibility `SimulationHost.Events` view to 1,000 entries.
7. Run Observability, Hosting-related integration, Runtime, Scenario, Fault,
   OPC UA, and Modbus tests.
8. Commit:

```powershell
git add src/IndustrialSim.Observability src/IndustrialSim.Hosting tests
git commit -m "feat: add bounded runtime event pipeline"
```

### Task 3: Make the event log the REST and SignalR observation source

**Files:**

- Modify: `src/IndustrialSim.Web/Api/V1/ControlPlaneServices.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`
- Modify: `src/IndustrialSim.Web/Hubs/RuntimeStreamBroker.cs`
- Modify: `src/IndustrialSim.Web/Hubs/RuntimeHub.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Modify: `tests/IndustrialSim.Web.Tests/RuntimeHubTests.cs`
- Modify: `tests/IndustrialSim.Web.Tests/V1ApiContractTests.cs`
- Modify: `tests/IndustrialSim.IntegrationTests/WebApplicationFactoryTests.cs`

1. Write failing API tests for retained filtering and maximum limits.
2. Rewrite SignalR broker tests to prove it reads the canonical event sequence,
   remains bounded, and contributes `stream=signalr,stage=subscriber` drops.
3. Register one event-log singleton/background service and explicitly attach it
   before the boot simulation starts.
4. Change event endpoints/device details and SignalR to query/subscribe to the
   event log; preserve the current datapoint live contract for Vue clients.
5. Run Web, WebApplicationFactory, and Vue SignalR/polling tests.
6. Commit:

```powershell
git add src/IndustrialSim.Web tests/IndustrialSim.Web.Tests tests/IndustrialSim.IntegrationTests
git commit -m "feat: unify runtime event api and signalr stream"
```

### Task 4: Add liveness and readiness health checks

**Files:**

- Create: `src/IndustrialSim.Web/Health/IndustrialSimReadinessHealthCheck.cs`
- Create: `src/IndustrialSim.Web/Health/HealthResponseWriter.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/ControlPlaneServices.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Create: `tests/IndustrialSim.IntegrationTests/HealthEndpointTests.cs`

1. Write failing TestServer tests for live=200, ready=200, SQLite unavailable
   ready=503 while live remains 200, no-running-device ready=200, and response
   secret exclusion.
2. Register tagged health checks and map `/health/live` and `/health/ready`
   outside authorization.
3. Use `Database.CanConnectAsync`, registry query, and event-pump status for
   readiness. Do not inspect live datapoints or require protocol connections.
4. Run focused health, persistence-lock/unavailability, Web, and integration
   tests.
5. Commit:

```powershell
git add src/IndustrialSim.Web tests/IndustrialSim.IntegrationTests
git commit -m "feat: add runtime health endpoints"
```

### Task 5: Add Prometheus metrics and dropped-event reporting

**Files:**

- Modify: `src/IndustrialSim.Observability/IndustrialSim.Observability.csproj`
- Create: `src/IndustrialSim.Observability/Metrics/IndustrialSimMetrics.cs`
- Create: `src/IndustrialSim.Observability/Metrics/MetricLabels.cs`
- Modify: `src/IndustrialSim.Observability/Events/RuntimeEventLog.cs`
- Modify: `src/IndustrialSim.Web/IndustrialSim.Web.csproj`
- Modify: `src/IndustrialSim.Web/Api/V1/ControlPlaneServices.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Create: `tests/IndustrialSim.Observability.Tests/IndustrialSimMetricsTests.cs`
- Create: `tests/IndustrialSim.IntegrationTests/PrometheusMetricsTests.cs`

1. Write failing collector tests for all exact metric names and bounded label
   normalization.
2. Write a failing `/metrics` scrape test that starts/ticks a device, changes
   state, executes a scenario action, activates/recovers a fault, and overloads
   a stream; assert valid text and expected counter/gauge changes.
3. Add `prometheus-net.AspNetCore` 8.2.1 and register a dedicated collector
   registry so tests do not share global metric state.
4. Update metrics from event/host observers using only constant-time in-memory
   operations. Map `/metrics` outside authorization.
5. Run metric, isolation, WebApplicationFactory, and protocol regression tests.
6. Commit:

```powershell
git add src/IndustrialSim.Observability src/IndustrialSim.Web tests
git commit -m "feat: expose prometheus runtime metrics"
```

### Task 6: Add OpenTelemetry tracing and event correlation

**Files:**

- Modify: `src/IndustrialSim.Observability/IndustrialSim.Observability.csproj`
- Create: `src/IndustrialSim.Observability/Tracing/IndustrialSimActivitySource.cs`
- Create: `src/IndustrialSim.Observability/Tracing/IndustrialSimOperation.cs`
- Modify: `src/IndustrialSim.Web/IndustrialSim.Web.csproj`
- Modify: `src/IndustrialSim.Web/Api/V1/ControlPlaneServices.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Modify: `src/IndustrialSim.Web/appsettings.json`
- Create: `tests/IndustrialSim.Observability.Tests/TraceRedactionTests.cs`
- Create: `tests/IndustrialSim.IntegrationTests/TraceCorrelationTests.cs`

1. Write a failing in-memory exporter test: an HTTP state write creates an
   ASP.NET server span and child `industrial.state.write` span, and the retained
   `DataPointChanged` envelope has the same trace ID.
2. Write failing tests that custom span tags never contain a bearer token,
   password, connection string, request body, datapoint value, or exception
   message.
3. Add OpenTelemetry 1.18.0 hosting, ASP.NET Core, HTTP, and OTLP packages.
4. Register ActivitySource and optional OTLP batch export. Do not export when
   no endpoint is configured.
5. Add operation scopes to the listed mutation/lifecycle boundaries without
   adding per-tick spans.
6. Run tracing, event, Web, authentication-secret, and isolation tests.
7. Commit:

```powershell
git add src/IndustrialSim.Observability src/IndustrialSim.Web tests
git commit -m "feat: trace correlated runtime operations"
```

### Task 7: Close the Wave 3.1 acceptance gate

**Files:**

- Modify: `docs/PROTOFORGE_BASELINE_MATRIX.md`
- Modify: `docs/PROTOFORGE_COMPARISON.md`
- Modify: `docs/IMPLEMENTATION_NOTES.md`
- Modify: `README.md`
- Modify: `tests/IndustrialSim.IntegrationTests/DocumentationContractTests.cs`

1. Run all focused acceptance tests:

```powershell
dotnet test tests/IndustrialSim.Observability.Tests/IndustrialSim.Observability.Tests.csproj --configuration Release
dotnet test tests/IndustrialSim.IntegrationTests/IndustrialSim.IntegrationTests.csproj --configuration Release --filter "HealthEndpointTests|PrometheusMetricsTests|TraceCorrelationTests" -p:SkipClientBuild=true
dotnet test tests/IndustrialSim.Web.Tests/IndustrialSim.Web.Tests.csproj --configuration Release -p:SkipClientBuild=true
```

2. Run the full .NET suite and Vue tests/build:

```powershell
dotnet test IndustrialSim.sln --configuration Release -p:SkipClientBuild=true
npm test --prefix src/IndustrialSim.Web/ClientApp
npm run build --prefix src/IndustrialSim.Web/ClientApp
```

3. Run the Web host manually with temporary ports/database. Verify actual
   response status/body for `/health/live`, `/health/ready`, and `/metrics`.
4. Build and run the Docker image; verify the same three endpoints without
   providing a SQLite connection string. Do not add Playwright.
5. Update evidence documents only with the results actually obtained. Mark the
   matrix metrics row Verified only when retention/filter/subscriber,
   dropped-event, health, scrape, trace correlation, secret redaction, and
   simulation/runtime isolation tests all pass.
6. Run `git diff --check`.
7. Commit:

```powershell
git add docs README.md tests/IndustrialSim.IntegrationTests/DocumentationContractTests.cs
git commit -m "docs: close wave 3.1 observability gate"
```

## 10. Acceptance criteria

Wave 3.1 is complete only when all of the following are evidenced:

- retention/filter/subscriber tests pass for the canonical structured log;
- ingress and subscriber dropped-event tests pass and scrape counts agree;
- liveness/readiness endpoint tests pass, including SQLite failure isolation;
- `/metrics` is valid Prometheus text with all required exact names;
- trace correlation connects an HTTP control operation to its runtime event;
- secret redaction tests prove raw credentials never reach event/API/subscriber
  payloads or custom trace tags;
- simulation/runtime isolation tests prove blocked/slow observation does not
  delay deterministic ticks or stop a real-time simulation;
- existing SignalR fallback, StateStore, scenario, fault, OPC UA, Modbus,
  WebApplicationFactory, Docker, and architecture tests remain green;
- SQLite contains no continuous live state or event table;
- no external exporter, logger, subscriber, or readiness dependency can mutate
  device state.

## 11. Known risks and mitigations

- **Sequence gaps under overload:** intentional and measured through dropped
  metrics; clients refresh authoritative state.
- **Observer attachment races:** resolve/attach the singleton before boot host
  start and subscribe to `SimulationAdded` before enumerating existing handles
  with duplicate-host suppression.
- **Host replacement subscriptions:** detach disposed/replaced hosts or ignore
  them through an attachment registration owned by the log.
- **Metric test leakage:** use an injected, per-service-provider collector
  registry rather than static global metrics.
- **Readiness leaking storage errors:** return check names/status only; log a
  redacted summary separately.
- **Trace volume:** no tick/datapoint behavior spans; batch OTLP export and
  configurable sampling.
- **Third-party logs:** Wave 3.1 redacts IndustrialSim-owned observability data,
  not every internal message from EF Core or protocol libraries. Broader log
  provider filtering requires separate evidence before making that claim.

Wave 3.1 stops after Task 7. User Testing begins only in Wave 3.2 after a
separate go-ahead.
