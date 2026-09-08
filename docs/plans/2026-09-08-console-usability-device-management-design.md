# Console Usability and Device Management Design

## Scope and decisions

This increment closes the developer workflow from first launch through device
creation, inspection, operation, safe definition editing, identity management,
and non-secret settings management. It extends the existing Vue 3 console and
versioned control API; it does not replace the accepted runtime, visual template
catalog, scenario engine, or protocol adapters.

The visual system keeps the compact Linear-inspired character but raises body
copy to 15–16px, controls and navigation to 13–14px, and supporting text to at
least 12px. Theme preference has Light, Dark, and System values, defaults to
System, is stored in localStorage, and is applied by an inline bootstrap script
before Vue mounts. Light mode uses a pale gray canvas, white surfaces, fine gray
borders, and the existing violet accent. Both themes retain visible keyboard
focus, safe Vue text rendering, responsive layouts, and AA-oriented contrast.

## Device workflow and consistency

The Devices page exposes Quick create and Create from template as distinct
paths. Quick create edits a protocol-independent device definition plus a
separate list of protocol port reservations. Dynamic datapoint and binding rows
perform client validation, while the API remains authoritative for duplicate
IDs, port conflicts, types, access modes, and authorization. Successful creation
refreshes the fleet, selects the device, and navigates to its detail route.

`/devices/:deviceId` presents Overview, State, Definition, Protocols, Scenarios,
Faults, and Events. Runtime reads and writes always target the selected host.
Structural edits are rejected while running. Replacement uses a registry-level
operation that validates and constructs a candidate host while retaining the
current handle. Under the registry catalog gate it verifies bindings, swaps the
handle, persists the new definition, and disposes the old host only after the
commit succeeds. If persistence fails, the registry restores the old handle and
disposes the candidate. This prevents half-updated runtime/catalog state.

## Overview, users, and settings

Overview becomes an observer and navigation surface: fleet counts, active
faults, protocol health, recent errors/events, selected-device summary, and
task-oriented shortcuts. First-use empty states explain what each workflow is
for. Raw scenario YAML stays on Scenarios; complex fault controls move to device
details.

Authentication-disabled mode shows an explanatory card and the effective
`local-developer` Admin, with no unusable form. Enabled mode adds bootstrap,
list/create/role/delete/password endpoints backed by ASP.NET Core Identity and
prevents self-deletion. Settings add typed String, Number, Boolean, and JSON
editing with optimistic versions. Secret-like keys (`password`, `token`,
`secret`, and equivalents) are rejected by API validation; no secret store is
introduced.

## Verification

Vitest covers theme persistence/bootstrap, responsive landmarks, device forms,
details, RBAC, overview, users, settings, and polling fallback. Hosting and Web
tests cover atomic replacement, failure rollback, Problem Details, identity
operations, setting type/version validation, and sensitive-key rejection. The
release gate includes frontend tests/typecheck/build, .NET build/test, Docker
configuration/build/runtime checks, and browser inspection at desktop and
390px widths.
