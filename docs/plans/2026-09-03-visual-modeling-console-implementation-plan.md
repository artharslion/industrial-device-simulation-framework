# Visual Modeling Console Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Deliver visual device-template creation, graphical scenario editing/import/export, and a complete multi-page Vue developer console without implementing the deferred ProtoForge features.

**Architecture:** Add a protocol-independent Templates domain, persist immutable template versions and editor metadata through Application/Persistence, expose versioned Web APIs, and build URL-addressable Vue pages over those APIs. StateStore remains authoritative for live runtime state and SignalR remains the primary live channel.

**Tech Stack:** .NET 10, EF Core SQLite, ASP.NET Core minimal APIs, Vue 3, TypeScript, Vue Router, Pinia, Vite, Vitest.

---

### Task 2.0: Lock the narrowed Wave 2 scope

**Files:**
- Modify: `docs/PROJECT_SPEC.md`
- Modify: `docs/plans/2026-09-02-protoforge-baseline-implementation-plan.md`
- Test: `tests/IndustrialSim.IntegrationTests/DocumentationContractTests.cs`

1. Add a failing documentation contract asserting that Wave 2 says local
   template catalog rather than marketplace and lists the three selected UI
   workflows.
2. Run the focused integration test and verify failure.
3. Update the specification and baseline plan.
4. Run the focused test and commit.

### Task 2.1: Add immutable device templates and mapping profiles

**Files:**
- Create: `src/IndustrialSim.Templates/IndustrialSim.Templates.csproj`
- Create: `src/IndustrialSim.Templates/Models/*.cs`
- Create: `src/IndustrialSim.Templates/TemplateCatalogService.cs`
- Create: `src/IndustrialSim.Application/Templates/*.cs`
- Create: `src/IndustrialSim.Persistence/Entities/DeviceTemplateEntity.cs`
- Create: `src/IndustrialSim.Persistence/Entities/MappingProfileEntity.cs`
- Create: `src/IndustrialSim.Persistence/Repositories/TemplateCatalogRepository.cs`
- Create: `src/IndustrialSim.Persistence/Migrations/202609030003_VisualModeling.cs`
- Test: `tests/IndustrialSim.Templates.Tests/*`
- Test: `tests/IndustrialSim.Persistence.Tests/TemplatePersistenceTests.cs`

1. Write failing tests for immutable versions, duplicate datapoints, mapping
   references, search/filter, import/export, and instantiation.
2. Run the focused tests and verify failure.
3. Implement the smallest protocol-independent records and validators.
4. Add SQLite persistence with a composite template ID/version key.
5. Run focused tests, architecture tests, and commit.

### Task 2.2: Add template and scenario management APIs

**Files:**
- Create: `src/IndustrialSim.Web/Api/V1/TemplateEndpoints.cs`
- Create: `src/IndustrialSim.Web/Api/V1/ScenarioEndpoints.cs`
- Modify: `src/IndustrialSim.Web/Api/V1/V1Endpoints.cs`
- Modify: `src/IndustrialSim.Application/Catalogs/CatalogRecords.cs`
- Modify: `src/IndustrialSim.Persistence/Entities/ScenarioCatalogEntity.cs`
- Test: `tests/IndustrialSim.Web.Tests/TemplateApiTests.cs`
- Test: `tests/IndustrialSim.Web.Tests/ScenarioApiTests.cs`

1. Write failing API tests for template CRUD/versioning, import/export,
   instantiation, scenario CRUD/import/export, conflicts, and authorization.
2. Run and verify failure.
3. Implement endpoints using Application repository contracts and existing
   device/scenario runtime services.
4. Return RFC Problem Details and stable error codes for invalid documents,
   duplicate versions, missing references, and concurrency conflicts.
5. Run Web and persistence tests and commit.

### Task 2.3: Introduce the multi-page Vue shell

**Files:**
- Modify: `src/IndustrialSim.Web/ClientApp/package.json`
- Modify: `src/IndustrialSim.Web/ClientApp/package-lock.json`
- Create: `src/IndustrialSim.Web/ClientApp/src/router/index.ts`
- Create: `src/IndustrialSim.Web/ClientApp/src/stores/workspace.ts`
- Create: `src/IndustrialSim.Web/ClientApp/src/layouts/ConsoleLayout.vue`
- Create: `src/IndustrialSim.Web/ClientApp/src/views/*.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/App.vue`
- Modify: `src/IndustrialSim.Web/ClientApp/src/main.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/router/router.test.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/App.test.ts`

1. Write failing route and shell tests for Overview, Devices, Templates,
   Scenarios, Protocols, Events, Users, and Settings.
2. Install Vue Router and Pinia and update the lockfile.
3. Implement the responsive shell, route metadata, loading/empty/error states,
   role visibility, and reusable page components.
4. Preserve the current Overview operations and SignalR/polling behavior.
5. Run Vitest, typecheck, build, and commit.

### Task 2.4: Add visual template and scenario editors

**Files:**
- Create: `src/IndustrialSim.Web/ClientApp/src/components/templates/*.vue`
- Create: `src/IndustrialSim.Web/ClientApp/src/components/scenarios/*.vue`
- Create: `src/IndustrialSim.Web/ClientApp/src/composables/useTemplateEditor.ts`
- Create: `src/IndustrialSim.Web/ClientApp/src/composables/useScenarioEditor.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/api.ts`
- Modify: `src/IndustrialSim.Web/ClientApp/src/types.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/composables/*.test.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/views/*.test.ts`

1. Write failing tests for adding/removing datapoints and mappings, immutable
   version saves, template instantiation, scenario step ordering, YAML
   import/export, save/reload, run, and validation failures.
2. Implement accessible form/table editors with a visual scenario flow rail.
3. Keep all imported content in text bindings and keep runtime state outside
   Pinia.
4. Run focused Vitest and Web contract tests, visually inspect desktop and
   narrow layouts, fix defects, and commit.

### Task 2.5: Final regression gate

1. Run `npm ci`, `npm test`, `npm run typecheck`, and `npm run build` in
   `src/IndustrialSim.Web/ClientApp`.
2. Run `dotnet build IndustrialSim.sln --configuration Release` and
   `dotnet test IndustrialSim.sln --configuration Release --no-build`.
3. Run dependency vulnerability checks and Docker validation when available.
4. Confirm the worktree is clean and report exact results and remaining Wave 3+
   scope.
