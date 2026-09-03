# Vue Developer Console Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the inline developer console with a Vue 3, Vite, and TypeScript application while preserving its Linear-inspired design and operational behavior.

**Architecture:** Keep the existing ASP.NET Core API as the runtime boundary and serve the compiled single-page application as static files. The Vue client owns polling, typed API access, presentation state, and user interactions; it does not duplicate or own simulation state.

**Tech Stack:** Vue 3, Vite, TypeScript, Vitest, CSS custom properties, ASP.NET Core static files, npm.

---

### Task 1: Create the frontend workspace

**Files:**
- Create: `src/IndustrialSim.Web/ClientApp/package.json`
- Create: `src/IndustrialSim.Web/ClientApp/package-lock.json`
- Create: `src/IndustrialSim.Web/ClientApp/tsconfig.json`
- Create: `src/IndustrialSim.Web/ClientApp/vite.config.ts`
- Create: `src/IndustrialSim.Web/ClientApp/index.html`

1. Add Vue, Vite, TypeScript, Vue Test Utils, Vitest, and jsdom dependencies.
2. Configure Vite to emit stable production assets into the Web project's `wwwroot` with a `/assets/` base.
3. Add `typecheck`, `test`, and `build` scripts.
4. Run `npm install` and confirm the lockfile is generated.

### Task 2: Add typed runtime access

**Files:**
- Create: `src/IndustrialSim.Web/ClientApp/src/api.ts`
- Create: `src/IndustrialSim.Web/ClientApp/src/types.ts`
- Create: `src/IndustrialSim.Web/ClientApp/src/composables/useDeveloperConsole.ts`
- Test: `src/IndustrialSim.Web/ClientApp/src/composables/useDeveloperConsole.test.ts`

1. Write focused tests for initial refresh, action refresh, error reporting, and polling cleanup.
2. Run the tests and confirm they fail before the implementation exists.
3. Implement a same-origin API client with actionable HTTP errors.
4. Implement reactive runtime state and lifecycle, scenario, and fault actions.
5. Run the focused tests and confirm they pass.

### Task 3: Migrate the Linear-inspired interface

**Files:**
- Create: `src/IndustrialSim.Web/ClientApp/src/main.ts`
- Create: `src/IndustrialSim.Web/ClientApp/src/App.vue`
- Create: `src/IndustrialSim.Web/ClientApp/src/components/ConsolePanel.vue`
- Create: `src/IndustrialSim.Web/ClientApp/src/components/StatusCard.vue`
- Create: `src/IndustrialSim.Web/ClientApp/src/styles.css`
- Test: `src/IndustrialSim.Web/ClientApp/src/App.test.ts`

1. Add component tests for the operational landmarks and actions.
2. Implement semantic Vue templates for overview, StateStore, runtime events, scenario controls, and fault controls.
3. Preserve responsive behavior, accessible live regions, focus states, reduced motion, and safe text rendering.
4. Keep the established low-contrast dark palette and dense operational layout.
5. Run component tests, type checking, and the production build.

### Task 4: Integrate the SPA with ASP.NET Core and Docker

**Files:**
- Modify: `src/IndustrialSim.Web/Program.cs`
- Modify: `src/IndustrialSim.Web/DeveloperConsolePage.cs`
- Modify: `src/IndustrialSim.Web/IndustrialSim.Web.csproj`
- Modify: `Dockerfile`

1. Replace the inline HTML endpoint with default-file, static-file, and SPA fallback routing.
2. Add an MSBuild target that restores and builds the client before .NET build/publish and copies the Vite output into `wwwroot`.
3. Add a Node build stage to Docker and copy the compiled client into the .NET publish output.
4. Verify the root document and hashed asset requests return successfully.

### Task 5: Update contracts and verify end to end

**Files:**
- Modify: `tests/IndustrialSim.Web.Tests/DeveloperConsoleTests.cs`
- Modify: `docs/plans/2026-09-03-linear-console-design.md`

1. Replace inline-script assertions with SPA shell and static-asset contract assertions.
2. Document the Vue/Vite architecture decision without changing the v0.1 product scope.
3. Run `npm test`, `npm run typecheck`, and `npm run build`.
4. Run the focused Web tests, then the full .NET test suite if focused verification passes.
5. Launch the Web host and inspect desktop and narrow layouts in a browser.
