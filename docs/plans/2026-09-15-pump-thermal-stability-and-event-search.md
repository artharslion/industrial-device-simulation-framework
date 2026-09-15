# Pump Thermal Stability and Event Search Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Keep the default Pump within a safe normal operating temperature and make retained runtime alarms and other events searchable across devices.

**Architecture:** Extend the protocol-independent Pump behavior profile with a backward-compatible `normalOperatingTemperature` parameter. Extend the bounded in-memory runtime event query with free-text matching, expose a global filtered `/api/v1/events` endpoint, and let the Vue Events page query that endpoint without making the UI a state authority.

**Tech Stack:** .NET 10, xUnit, ASP.NET Core minimal APIs, Vue 3, TypeScript, Vitest.

**Status (2026-09-15):** Completed in `95384f8`. A fresh verification at that
commit passed 231 Release .NET tests, 30 Vue tests across 16 files, and the Vue
production build. The evidence covers the Pump normal-temperature cap,
threshold validation, bounded retained-event search, the global event API, and
the Events page filters. No new container or third-party protocol
interoperability run is claimed for this follow-up.

---

### Task 1: Stabilize the Pump thermal model

**Files:**
- Modify: `tests/IndustrialSim.Runtime.Tests/PumpTests.cs`
- Modify: `src/IndustrialSim.Devices/Pump/Pump.cs`
- Modify: `src/IndustrialSim.Devices/BuiltInDeviceProfiles.cs`
- Modify: `src/IndustrialSim.Hosting/SimulationHost.cs`

1. Add a failing Pump test that advances a normal running pump beyond the time needed to reach 70°C and asserts that temperature remains 70°C with no alarm.
2. Run `dotnet test tests/IndustrialSim.Runtime.Tests/IndustrialSim.Runtime.Tests.csproj --filter FullyQualifiedName~PumpTests` and verify the new assertion fails.
3. Add `normalOperatingTemperature` to `PumpParameters` and the built-in profile, then cap only normal upward heating at that value.
4. Bind the new parameter in `SimulationHost` and reject a normal operating temperature that is not lower than the overheat threshold.
5. Re-run the focused runtime tests and verify they pass.

### Task 2: Add retained runtime-event search

**Files:**
- Modify: `tests/IndustrialSim.Observability.Tests/RuntimeEventLogTests.cs`
- Modify: `src/IndustrialSim.Observability/Events/RuntimeEventQuery.cs`
- Modify: `src/IndustrialSim.Observability/Events/RuntimeEventLogOptions.cs`
- Modify: `src/IndustrialSim.Web/Program.cs`
- Modify: `tests/IndustrialSim.Web.Tests/V1ApiContractTests.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`

1. Add failing tests for case-insensitive matching across device id, event type, event data, and metadata.
2. Run the focused observability tests and verify the search test fails.
3. Add a `Search` criterion to `RuntimeEventQuery`, evaluated before `TakeLast(limit)`.
4. Raise the bounded default retention capacity from 1000 to 10000 so a few minutes of high-frequency datapoint changes remain searchable.
5. Add `GET /api/v1/events` with optional `deviceId`, `eventType`, `q`, `afterSequence`, and `limit`; extend the existing device endpoint with `q`.
6. Add and run a focused Web API contract test.

### Task 3: Add Event page filters

**Files:**
- Modify: `src/IndustrialSim.Web/ClientApp/src/types.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/api.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/views/EventsView.vue`
- Create: `src/IndustrialSim.Web/ClientApp/src/views/EventsView.test.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/styles.css`

1. Add a failing Vitest test that searches for `alarm`, filters by device/type, and verifies the API query and result rendering.
2. Run `npm test -- --run src/views/EventsView.test.ts` from `src/IndustrialSim.Web/ClientApp` and verify it fails.
3. Add a typed `platformApi.runtimeEvents` call and an Event page toolbar with device, event type, and free-text filters.
4. Show the device id, event type, payload, and result count; provide Search and Clear actions.
5. Re-run the focused UI test and the ClientApp test suite.

### Task 4: Synchronize public contracts and verify

**Files:**
- Modify: `examples/devices/pump.yaml`
- Modify: `examples/devices/pump-shared-opcua.yaml`
- Modify: `docs/PROJECT_SPEC.md`
- Modify: `docs/USER_MANUAL.md`
- Modify: `docs/USER_MANUAL.zh-CN.md`

1. Document `normalOperatingTemperature: 70` and the bounded normal heating semantics.
2. Document that runtime event search is bounded by configured in-memory retention.
3. Run focused .NET tests, all ClientApp tests, then `dotnet test IndustrialSim.sln --no-restore` when the focused checks pass.
4. Review `git diff --check` and `git status --short` and report remaining risks.
