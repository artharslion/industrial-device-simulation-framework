# CI and Release Evidence Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use the Code workflow to implement this plan task-by-task.

**Goal:** Add free public-repository CI, real server integration coverage, and Docker Hub publication for tags and explicit manual releases.

**Architecture:** GitHub Actions runs validation on standard Ubuntu runners. Pull requests never receive or require Docker Hub credentials. A separate release workflow authenticates only for tag or manual dispatch, builds the existing multi-stage Dockerfile, and publishes immutable image tags. Server integration tests use `WebApplicationFactory<WebApplicationMarker>` with temporary YAML and SQLite configuration so the production `Program` composition root is exercised without Playwright or fixed protocol ports.

**Tech Stack:** GitHub Actions, .NET 10, Node.js 22, npm/Vitest/Vite, Docker Buildx, Docker Hub, xUnit, `Microsoft.AspNetCore.Mvc.Testing`.

---

## Completion record

Completed on 2026-09-14 at commit `9d480d2`.

- GitHub Actions CI run
  [`34801843690`](https://github.com/artharslion/industrial-device-simulation-framework/actions/runs/34801843690)
  passed `Application and client checks` and `Container build and smoke`.
- The validation covered 178 .NET tests, 28 Vue tests, the Vue production
  build, `docker compose config`, Docker image construction, and an
  `/api/runtime` container smoke request.
- `WebApplicationFactoryTests` exercised the production `Program` composition
  root through TestServer and covered device/state APIs, OpenAPI, security
  headers, and RFC Problem Details.
- Manual release run
  [`34802093256`](https://github.com/artharslion/industrial-device-simulation-framework/actions/runs/34802093256)
  passed verification and publication, producing Docker Hub tag `ci-smoke` at
  digest
  `sha256:3b90a83c8631e7e39828a47b26cd996c095c650a69a45bc648fada4ec113b790`.
- The published image was manually rerun without an explicit SQLite connection
  string. `/` and `/api/runtime` returned HTTP 200, and the non-root `app` user
  created `/app/data/industrial-sim.db`.

This closes the Wave 2.5 gate. It records an explicit manual publication, not
a semantic-version release tag or additional protocol interoperability.

---

### Task 1: Lock the release evidence contract in documentation tests

**Files:**
- Modify: `tests/IndustrialSim.IntegrationTests/DocumentationContractTests.cs`

1. Assert the specification contains Wave 2.5 and the baseline matrix requires GitHub Actions, `WebApplicationFactory`, Docker smoke, and Docker Hub publication.
2. Run the focused documentation test and confirm it passes against the updated documents.

### Task 2: Add production-composition server integration tests

**Files:**
- Modify: `tests/IndustrialSim.IntegrationTests/IndustrialSim.IntegrationTests.csproj`
- Create: `tests/IndustrialSim.IntegrationTests/WebApplicationFactoryTests.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`

1. Add `Microsoft.AspNetCore.Mvc.Testing` matching the .NET 10 target.
2. Allow the production entry point to read `IndustrialSim:DeviceConfig` from normal ASP.NET configuration while preserving `INDUSTRIALSIM_DEVICE_CONFIG` compatibility.
3. Configure `WebApplicationFactory<WebApplicationMarker>` with a temporary no-protocol YAML device, temporary SQLite database, disabled authentication, and Development environment.
4. Verify `/api/v1/devices`, `/api/v1/devices/{id}/state`, `/openapi/v1.json`, security headers, and RFC Problem Details through TestServer.
5. Dispose the factory and remove temporary files.

### Task 3: Add pull-request CI

**Files:**
- Create: `.github/workflows/ci.yml`

1. Trigger on pull requests, pushes to `master`, and manual dispatch.
2. Use `ubuntu-latest`, read-only repository permissions, concurrency cancellation, and bounded timeouts.
3. Restore/build/test .NET in Release mode.
4. Run `npm ci`, Vitest, typecheck/production build.
5. Run `docker compose config` and `docker build` without publishing.
6. Do not upload large artifacts on successful runs.

### Task 4: Add Docker Hub release publication

**Files:**
- Create: `.github/workflows/docker-publish.yml`
- Modify: `README.md`

1. Trigger on `v*` tags and `workflow_dispatch` with an optional `image_tag` input.
2. Run the complete .NET and Vue verification in a job that does not receive Docker Hub credentials.
3. Authenticate the dependent publish job with `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` repository secrets.
4. Publish to `${DOCKERHUB_USERNAME}/industrial-device-simulation-framework`.
5. For semantic version tags, publish the exact tag plus semver major/minor aliases and `latest`; for manual dispatch, publish the provided tag or `manual-<short-sha>` without changing `latest`.
6. Generate OCI labels and publish Buildx provenance metadata.
7. Document required secrets and release behavior.

### Task 5: Verify the complete change

1. Run the focused `WebApplicationFactoryTests` and documentation tests.
2. Run `dotnet test IndustrialSim.sln --configuration Release`.
3. Run `npm test` and `npm run build` in `src/IndustrialSim.Web/ClientApp`.
4. Run `docker compose config`, `docker build`, and `git diff --check`.
5. Inspect workflow YAML for pull-request secret isolation and immutable tag behavior.

## Assumptions

- The public GitHub repository default branch is `master`.
- Docker Hub repository name is `industrial-device-simulation-framework` under the account stored in `DOCKERHUB_USERNAME`.
- Docker Hub publication is not attempted from pull requests.
- Playwright and browser E2E remain deferred.
