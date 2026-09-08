# Console Usability and Device Management Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Complete a readable theme system and the end-to-end Vue workflows for devices, overview, users, and non-secret settings.

**Architecture:** Extend the existing Vue control plane and `/api/v1` endpoints while keeping Core protocol-independent and StateStore authoritative for live state. Add a stopped-only transactional registry replacement operation so runtime and persisted definitions either both change or both remain unchanged.

**Tech Stack:** .NET 10, ASP.NET Core Identity, EF Core SQLite, Vue 3, TypeScript, Pinia, Vue Router, SignalR, Vite, Vitest.

---

### Task 1: Readability and theme system

**Files:**
- Create: `src/IndustrialSim.Web/ClientApp/src/stores/theme.ts`
- Create: `src/IndustrialSim.Web/ClientApp/src/stores/theme.test.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/index.html`
- Modify: `src/IndustrialSim.Web/ClientApp/src/layouts/ConsoleLayout.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/styles.css`

1. Write failing tests for Light, Dark, and System selection, localStorage, and system-media updates.
2. Add the pre-mount theme bootstrap and verify the root attribute is applied before the module script.
3. Implement the Pinia theme store and accessible header selector.
4. Raise typography/control sizes and add light tokens and responsive/focus assertions.
5. Run focused Vitest, typecheck, build, and commit `feat: add readable console themes`.

### Task 2: Direct and template-backed device creation

**Files:**
- Create: `src/IndustrialSim.Web/ClientApp/src/components/devices/DeviceDefinitionEditor.vue`
- Create: `src/IndustrialSim.Web/ClientApp/src/components/devices/DeviceCreateDialog.vue`
- Create: `src/IndustrialSim.Web/ClientApp/src/composables/useDeviceEditor.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/views/DevicesView.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/api.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/types.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/components/devices/*.test.ts`
- Test: `tests/IndustrialSim.Web.Tests/V1ApiContractTests.cs`

1. Write failing tests for dynamic datapoints/bindings, scalar conversion, validation, duplicate ID, port conflict, and Viewer denial.
2. Implement Quick create with actionable field and Problem Details errors.
3. Link Create from template to the existing Templates workflow and instantiation API.
4. Refresh, select, and navigate after successful creation.
5. Run focused frontend/Web tests and commit `feat: add device creation workflow`.

### Task 3: Device details and atomic definition replacement

**Files:**
- Create: `src/IndustrialSim.Web/ClientApp/src/views/DeviceDetailsView.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/router/index.ts`
- Modify: `src/IndustrialSim.Hosting/SimulationRegistry.cs`
- Modify: `src/IndustrialSim.Application/Devices/DeviceApplicationService.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Contracts.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`
- Test: `tests/IndustrialSim.Hosting.Tests/SimulationRegistryTests.cs`
- Test: `tests/IndustrialSim.Web.Tests/V1ApiContractTests.cs`
- Test: `src/IndustrialSim.Web/ClientApp/src/views/DeviceDetailsView.test.ts`

1. Write failing tests for details, writable datapoints, lifecycle/tick, running edit rejection, successful replacement, rollback, deletion, RBAC, and live/poll continuity.
2. Add details and PUT contracts without changing existing routes.
3. Implement candidate construction, atomic registry swap, persistence commit, rollback, and old-host disposal.
4. Implement the tabbed detail page with State, Definition, Protocols, Scenarios, Faults, and Events.
5. Run Hosting/Web/Vitest tests and commit `feat: add safe device details and editing`.

### Task 4: Platform overview

**Files:**
- Modify: `src/IndustrialSim.Web/ClientApp/src/views/OverviewView.vue`
- Test: `src/IndustrialSim.Web/ClientApp/src/views/OverviewView.test.ts`

1. Write failing tests for counts, protocol health, selected summary, events/errors, shortcuts, and first-use empty state.
2. Replace the single-device expert workbench with observation and navigation sections.
3. Keep live data through SignalR and polling fallback; remove raw YAML/fault editors.
4. Run focused Vitest and commit `feat: refocus platform overview`.

### Task 5: Identity management

**Files:**
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`
- Modify: `src/IndustrialSim.Web/ClientApp/src/views/UsersView.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/api.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/types.ts`
- Test: `tests/IndustrialSim.Web.Tests/AuthenticationTests.cs`
- Test: `src/IndustrialSim.Web/ClientApp/src/views/UsersView.test.ts`

1. Write failing tests for disabled-mode guidance, bootstrap, create, role update, delete, self-delete prevention, password update, and role boundaries.
2. Add minimal Admin management endpoints and current-user password endpoint using Identity managers.
3. Implement disabled and enabled Vue states without exposing passwords/tokens.
4. Run focused tests and commit `feat: complete local user management`.

### Task 6: Typed non-secret settings

**Files:**
- Modify: `src/IndustrialSim.Web/Api/V1/V1Contracts.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`
- Modify: `src/IndustrialSim.Web/ClientApp/src/views/SettingsView.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/api.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/types.ts`
- Test: `tests/IndustrialSim.Web.Tests/V1ApiContractTests.cs`
- Test: `src/IndustrialSim.Web/ClientApp/src/views/SettingsView.test.ts`

1. Write failing tests for add/edit, four value types, versions, conflicts, validation, and sensitive keys.
2. Add typed upsert API validation and effective-versus-persisted metadata.
3. Implement accessible typed editors, success/conflict/error states, and useful empty guidance.
4. Run focused tests and commit `feat: add typed control plane settings`.

### Task 7: Release verification

1. Run `npm ci`, `npm test`, `npm run typecheck`, and `npm run build`.
2. Run `dotnet build IndustrialSim.sln --configuration Release` and `dotnet test IndustrialSim.sln --configuration Release --no-build`.
3. Run `docker compose config`, proxy-assisted build, detached runtime, persistence checks, and HTTP checks.
4. Inspect desktop and 390px layouts in a real browser, including console output and theme switching.
5. Record exact results, commits, and remaining risks.
