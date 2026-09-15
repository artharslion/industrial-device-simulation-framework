# Device Command Controls Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use executing-plans to implement this plan task-by-task.

**Goal:** Let operators explicitly invoke built-in device commands from the Web API and device details UI so starting a Pump behavior and advancing simulation time produces observable datapoint-change events.

**Architecture:** Keep `SimulationHost` lifecycle operations separate from logical device commands. Add an operator-authorized command endpoint that delegates to the existing `IDeviceRuntime.InvokeCommandAsync` path, preserving `StateStore` ownership and runtime event publication; expose the same operation through the Vue device details view. Deterministic devices continue to require explicit ticks, while real-time devices advance through the existing host loop.

**Tech Stack:** .NET 10 minimal APIs, IndustrialSim runtime and observability pipeline, Vue 3, TypeScript, xUnit, Vitest.

**Status (2026-09-15):** Completed in `6493829` after the plan commit
`b79ed37`. The implementation passed 228 Release .NET tests, 29 Vue tests, the
Vue production build, and documentation contracts. A fresh local Docker image
was exercised through Pump creation, runtime start, `commands/start`, one
deterministic tick, state reads, and retained event reads; `running`, `speed`,
`temperature`, and `pressure` changes were observed. No Docker Hub publication
was performed for this follow-up.

---

### Task 1: Add a tested device-command API

**Files:**

- Modify: `tests/IndustrialSim.Web.Tests/V1ApiContractTests.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`
- Modify: `src/IndustrialSim.Observability/Tracing/IndustrialSimOperation.cs`

1. Add a failing Web contract test that creates a deterministic Pump, starts its host, invokes `POST /api/v1/devices/{deviceId}/commands/start`, advances one second, and waits for `DataPointChanged` envelopes for `running`, `speed`, `temperature`, and `pressure`.
2. In the same test, prove an unknown command returns Problem Details with a stable `commandNotFound` error code and a command against a stopped host returns `deviceNotRunning`.
3. Run the focused test and confirm the command route is absent.
4. Map `POST /devices/{deviceId}/commands/{command}` under the existing Operator policy.
5. Reject stopped hosts without mutating state, validate the command against the device definition, and delegate successful execution to `host.Runtime.InvokeCommandAsync`.
6. Trace the bounded operation as `industrial.command.invoke`; do not add spans for Pump ticks or behavior-driven datapoint changes.
7. Run the focused Web contract tests.

### Task 2: Expose commands in the device details UI

**Files:**

- Modify: `src/IndustrialSim.Web/ClientApp/src/api.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/views/DeviceDetailsView.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/views/DeviceDetailsView.test.ts`

1. Add a failing component test proving the Pump `start` and `stop` commands are rendered separately from runtime lifecycle controls and invoke the command API.
2. Add `platformApi.invokeCommand(deviceId, command)`.
3. Add a Device commands card to the overview. Disable commands while the host is stopped, label lifecycle controls as runtime controls, and explain that deterministic devices require `Advance 1s` after a behavior command.
4. Refresh device details after command completion so state and retained events are visible immediately.
5. Run the focused Vitest file, the full Vue test suite, and the production Vue build.

### Task 3: Verify and commit the coherent fix

1. Run focused Pump/runtime composition tests and Web API tests.
2. Run the full .NET solution test suite because the route and tracing allowlist are public control-plane behavior.
3. Run `git diff --check`.
4. Commit the implementation separately from this plan with:

```powershell
git commit -m "fix: expose built-in device commands"
```

## Acceptance criteria

- Runtime `start` and Pump command `start` remain distinct operations.
- A stopped host rejects device commands without changing state.
- A running deterministic Pump produces `running`, `speed`, `temperature`, and `pressure` runtime events after command invocation and a tick.
- A running real-time Pump uses the existing background simulation loop.
- Commands use the existing runtime handler and `StateStore`; Web and observability own no device state.
- Unknown commands return stable, actionable Problem Details.
- The Vue UI clearly exposes device commands and deterministic time semantics.
