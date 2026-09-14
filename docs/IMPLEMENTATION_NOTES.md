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
