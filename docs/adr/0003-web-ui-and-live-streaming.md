# ADR 0003: Vue SPA and SignalR live streaming

- Status: Accepted
- Date: 2026-09-03

## Context

The developer console has already migrated to Vue 3, TypeScript, Vite, and
Vitest. Earlier baseline drafts assumed Razor Components and are obsolete.

## Decision

Keep the Vue SPA in `src/IndustrialSim.Web/ClientApp`. ASP.NET Core serves its
compiled static assets and owns `/api/v1`, OpenAPI, authentication, and SignalR.
SignalR is the primary state/event/log channel; polling remains a disconnect
fallback. Vue owns presentation and interaction state only.

## Alternatives

Razor Components and Blazor were rejected because they would reverse the
accepted frontend migration. An independent frontend server was rejected for
the current single-process distribution model.

## Consequences

`@microsoft/signalr` is a justified client dependency. Vue Router is reserved
for the Wave 2 multi-page console. Pinia remains optional until shared UI state
requires it. Lockfile and Vitest coverage are mandatory for dependency changes.

## Failure modes

Slow consumers receive bounded/coalesced updates and can refresh a snapshot.
Disconnects trigger reconnect attempts and then polling. API text is rendered
through safe Vue bindings, never raw HTML.

## Reversal conditions

Any frontend framework change requires a separate accepted design, migration
plan, accessibility evidence, and no loss of the current console behavior.
