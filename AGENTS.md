# AGENTS.md

## Project

This repository is the Industrial Device Simulation Framework. It is a
developer-first .NET runtime for defining, simulating, exposing, testing, and
intentionally failing virtual industrial devices.

The v0.1 MVP includes:

- Device model and runtime state
- YAML configuration
- Pump, Motor, and Sensor device models
- Scenario Engine
- Data, Device, and Network Faults
- OPC UA and Modbus TCP adapters
- CLI, Docker, and a developer Web UI
- Unit, protocol, and end-to-end tests

## Source Of Truth

Read these documents before making architectural changes:

1. `docs/PROJECT_SPEC.md` is the normative technical specification.
2. `docs/Industrial Device Simulation Framework.md` provides product context,
   positioning, and roadmap rationale.
3. This file defines AI-assisted development rules.

When documents conflict, update the documents deliberately and follow
`docs/PROJECT_SPEC.md` after the conflict is resolved. Do not silently invent
new scope.

## Architecture Invariants

- The Core domain must not depend on OPC UA, Modbus, Web UI, or transport code.
- Protocol adapters expose the runtime; they do not own device state or
  business behavior.
- Scenario actions operate on devices, datapoints, commands, and faults, never
  on protocol addresses.
- `StateStore` is the single source of truth for runtime state.
- State changes pass through validation, transition, event publication, and
  observer notification.
- Device definitions and runtime state must remain conceptually separate.
- Simulation time must be injectable. Avoid direct use of `DateTime.Now`,
  global random generators, and untestable wall-clock delays in domain logic.
- Network faults belong to the protocol or transport boundary and must not
  stop the device simulation automatically.

## Development Rules

- Keep changes narrow and aligned with the current milestone.
- Prefer existing .NET libraries and repository patterns over new abstractions.
- Keep configuration validation fail-fast and produce actionable errors.
- Treat YAML syntax and protocol mappings as public contracts; update examples
  and tests when they change.
- Define Modbus address ranges, data types, and byte/word order explicitly.
- Keep the Web UI focused on developer operations: state inspection, scenario
  control, fault control, and runtime events/logs. It is not a factory
  management platform.
- Do not add authentication, multi-user management, 3D visualization, PLC
  runtime behavior, AAS implementation, or additional protocols to v0.1.

## AI-Assisted Workflow

For every non-trivial task, the AI agent should:

1. Inspect the relevant files and current working-tree state.
2. Identify the applicable requirement in `docs/PROJECT_SPEC.md`.
3. State assumptions and affected modules before editing.
4. Implement the smallest coherent change.
5. Add or update focused tests in the same change.
6. Run the narrowest relevant verification, then broader checks when risk
   warrants it.
7. Report changed files, verification results, and remaining risks.

Do not claim that a protocol, scenario, fault, or UI behavior works without a
test or an explicit manual verification result.

## Review Checklist

- Does the change preserve protocol independence of the Core?
- Is runtime state updated through `StateStore`?
- Are Scenario and Fault transitions deterministic and observable?
- Are concurrent reads and writes safe and ordered?
- Are invalid references, mappings, and configurations rejected clearly?
- Are OPC UA and Modbus observing the same logical state?
- Does the Web UI reflect the same state as the runtime and adapters?
- Are Docker ports, configuration examples, and tests updated?
- Is new scope justified by the current milestone?
