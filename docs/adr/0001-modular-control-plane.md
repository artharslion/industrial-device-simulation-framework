# ADR 0001: Modular monolith control plane

- Status: Accepted
- Date: 2026-09-03

## Context

The accepted v0.1 runtime hosts one simulation. Platform work needs many
runtimes, persistence, APIs, users, and later catalogs without moving those
concerns into Core or `SimulationHost`.

## Decision

Use a modular monolith. References flow `Web -> Application -> Hosting ->
Runtime/Core` and `Web -> Persistence -> Application contracts`. Protocols
depend on protocol abstractions and runtime contracts. Core and Runtime must not
reference Web, Persistence, Application, or concrete protocols.

## Alternatives

Expanding `SimulationHost` directly was rejected because it mixes data-plane
and control-plane responsibilities. Microservices were rejected because remote
consistency and deployment complexity are not yet justified.

## Consequences

One deployable process remains simple, while projects and architecture tests
enforce boundaries. Cross-module changes require explicit application use cases.

## Failure modes

A service-locator Web host or repository calls from Runtime would erode the
boundary. Architecture tests and code review reject those references.

## Reversal conditions

Split processes only after measured isolation or scaling needs cannot be met by
in-process supervision and documented contracts.
