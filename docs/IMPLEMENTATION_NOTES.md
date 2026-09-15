# Implementation Notes

## Phase 0, Task 0.1: Environment Verification

- Verified on 2026-08-28 (Asia/Shanghai).
- Selected .NET SDK: `10.0.111` (MSBuild `18.0.11`).
- Selected target framework: `net10.0` (current installed .NET LTS SDK).
- Docker: `29.2.1`.
- Docker Compose: `v5.0.2`.
- Repository state: `master` tracking `origin/master`; no source changes were
  present. The pre-existing untracked `docs/plans/` directory is preserved.

## Phase 6R-8 verification environment

- Reverified on 2026-09-01 through 2026-09-02 (Asia/Shanghai).
- .NET SDK and target framework remain `10.0.111` / `net10.0`.
- Docker CLI `29.2.1` and Docker Compose `v5.0.2` are installed.
- The Docker Desktop Linux daemon was unavailable during the Phase 8 release
  gate. `com.docker.service` was stopped and the current process could not
  start it, so `docker build` and live container endpoint checks could not run.
  `docker compose config` and the repository container contract tests passed.

## Wave 2.5 release evidence environment

- Reverified on 2026-09-14 (Asia/Shanghai) at commit `9d480d2`.
- Docker Desktop Linux daemon is available: Docker Engine `29.2.1` on Docker
  Desktop `4.63.0`.
- GitHub Actions CI run `34801843690` passed both application/client and
  container build/smoke jobs.
- Manual Docker Hub release run `34802093256` published
  `industrialdevicesimulation/industrial-device-simulation-framework:ci-smoke`
  with digest
  `sha256:3b90a83c8631e7e39828a47b26cd996c095c650a69a45bc648fada4ec113b790`.
- A local run of that published image, without an explicit SQLite connection
  string, returned HTTP 200 for `/` and `/api/runtime`. The container ran as
  `uid=1654(app)` and created `/app/data/industrial-sim.db` owned by `app:app`.
- The earlier Phase 8 daemon failure remains recorded above as historical
  context; it is no longer the current release-gate state.

## Wave 3.1 observability verification environment

- Reverified on 2026-09-14 (Asia/Shanghai) through commit `5fe021e`.
- The full Release test run passed 208 .NET tests. The Vue suite passed 28 tests
  across 15 files, and `vue-tsc --noEmit && vite build` completed successfully.
- `RuntimeEventLogTests` and `RuntimeIsolationTests` verify bounded retention,
  filters, subscribers, ingress/subscriber drop accounting, and that blocked
  observation does not delay deterministic ticks or stop real-time simulation.
- `/health/live` is process-only. `/health/ready` checks the registry, running
  event pump, and SQLite connectivity; tests prove SQLite unavailability makes
  readiness return 503 while liveness remains 200, and stopped devices remain
  ready.
- Prometheus uses an injected custom registry and fixed label vocabularies. The
  exact exposed metric families are `industrial_simulation_ticks_total`,
  `industrial_device_state_changes_total`, `industrial_scenario_actions_total`,
  `industrial_faults_active`, `industrial_protocol_connections`,
  `industrial_protocol_errors_total`, and
  `industrial_stream_events_dropped_total`.
- OpenTelemetry registers ASP.NET Core, HTTP client, and
  `IndustrialSim.Observability` sources. OTLP export is absent unless
  `OpenTelemetry:Otlp:Endpoint` is configured, and configured export uses the
  batch processor. No per-tick spans are created.
- A source-host manual run returned HTTP 200 with healthy JSON for
  `/health/live` and `/health/ready`; `/metrics` returned 200 and all seven
  metric families. Its temporary SQLite database was created successfully.
- Docker Engine `29.2.1` built local image `industrial-sim:wave31` with image ID
  `sha256:a6ed5bc949dbb09f9a55cd85e9eab55688f9b7abf412e77c7ec1618b6b0c2e69`.
  Without an explicit SQLite connection string, the container returned HTTP
  200 for `/health/live`, `/health/ready`, and `/metrics`, ran as UID 1654, had
  writable `/app/data`, and created `/app/data/industrial-sim.db`.
- Observability remains an in-process observer. It does not own or mutate
  device state, and SQLite does not store the continuous event log or live
  datapoint stream. Secret redaction is evidenced for IndustrialSim-owned event
  and custom tracing surfaces, not every third-party library log.

## Shared OPC UA endpoint hosting verification environment

- Verified on 2026-09-15 (Asia/Shanghai) through implementation commit
  `bf079b7` plus the final documentation worktree.
- `dotnet restore IndustrialSim.sln` completed successfully. The Release build
  completed with 0 warnings and 0 errors, and the full solution run passed 227
  .NET tests across 13 test projects with no failures or skips.
- Focused public-contract verification passed all 15 Configuration tests and
  13 filtered Documentation/Container contract tests.
- A separate repository OPC Foundation client acceptance rerun passed three
  protocol tests covering one shared listener, isolated browse/read/write/
  command routing, target-only Network Fault behavior, recovery from the
  latest `StateStore` value, and last-member port release. The Web/Registry
  end-to-end shared-endpoint test also passed and covered two API-created
  devices, independent writes/stops, continued access to the remaining member,
  and final port rebinding.
- `docker compose config` and `git diff --check` passed. Docker Engine `29.2.1`
  built `industrial-sim:shared-opcua` with image ID
  `sha256:3137d0d28da9c4f66c2f50bf91de2c563595a4b23e14a9cb66be88850aa58d45`.
  Compose continues to publish one OPC UA port (`4840`) for the process.
- The repository client uses the OPC Foundation SDK and is acceptance evidence
  for the implemented server behavior. No separate third-party GUI or client
  was run, so broader third-party interoperability is not claimed.
- Shared hosting does not change state ownership: every service routes to the
  target runtime and its `StateStore`; SQLite still does not persist continuous
  live state. OPC UA disconnect/timeout/latency behavior is device-scoped and
  never stops the shared listener or simulation ticks.
