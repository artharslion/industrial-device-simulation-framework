# AI Development Guide

This document describes how to use AI coding assistants effectively while
building the Industrial Device Simulation Framework. It concerns development
collaboration only; it does not define AI features of the simulator.

## Working Context

Always provide the assistant with:

- The relevant section of `docs/PROJECT_SPEC.md`.
- The intended milestone and acceptance criteria.
- The affected files or subsystem.
- Existing test or client behavior that must remain compatible.

The assistant should inspect the repository before proposing edits. If the
repository has no implementation yet, it should establish a thin vertical
slice instead of generating every planned subsystem at once.

## Preferred Task Format

Use requests with this structure:

```text
Goal:
  <one concrete outcome>

Scope:
  <modules or files that may change>

Constraints:
  <architecture, API, compatibility, or milestone constraints>

Acceptance:
  <observable behavior and tests>
```

Example:

```text
Goal:
  Add `after` and `ramp` actions to the Scenario Engine.

Scope:
  Scenario parser, scheduler, runtime tests, YAML example.

Constraints:
  Scenario must update StateStore and remain protocol-independent.

Acceptance:
  A deterministic test observes speed changing from 0 to 1450 over 10s.
```

## Expected AI Behavior

The assistant should:

- Ask only when an ambiguity changes architecture or public behavior.
- Prefer a small working implementation over speculative framework code.
- Explain tradeoffs when choosing libraries or abstractions.
- Preserve unrelated user changes in a dirty working tree.
- Use tests to pin down state transitions, timing, mappings, and fault
  lifecycle behavior.
- Call out unverified assumptions, especially around industrial protocol
  semantics and transport-level failures.

The assistant should not:

- Add protocol-specific concepts to the Core domain.
- Hide state mutation inside adapters or UI code.
- Introduce a scripting language when the small Scenario DSL is sufficient.
- Treat generated code as correct without compiling and testing it.
- Expand v0.1 into a full digital twin, PLC runtime, or enterprise platform.

## Review Prompts

Useful review requests include:

- “Review this change for state ownership and concurrency bugs.”
- “Check whether this Scenario action is deterministic and replayable.”
- “Verify that OPC UA and Modbus expose the same StateStore value.”
- “Review this Modbus mapping for address overlap and encoding ambiguity.”
- “Check whether this Web UI operation uses the runtime command path.”
- “Find missing tests for Fault activation, recovery, and network isolation.”

## Completion Report

Every implementation task should end with:

1. A concise summary of behavior changed.
2. Links to the changed files.
3. Tests or commands run and their result.
4. Known gaps, assumptions, or follow-up work.
