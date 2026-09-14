# ADR 0007: Asynchronous observability boundary

- Status: Accepted
- Date: 2026-09-14

## Context

Wave 3.1 requires a bounded structured runtime event log, health checks,
Prometheus metrics, OpenTelemetry tracing, dropped-event accounting, and secret
redaction. The current implementation has an unbounded event queue on each
`SimulationHost` and a separate bounded SignalR datapoint stream. Attaching
external exporters or slow subscribers directly to synchronous runtime events
would allow observability work to delay simulation ticks.

`StateStore` is the only live-state authority. SQLite is a control-plane store,
not an event or continuous live-state store. Core and Runtime must remain free
of Web, exporter, and observability dependencies.

## Decision

Create `IndustrialSim.Observability` as an observer module. It references
Hosting and domain event types, while Web references Observability for
composition and endpoint mapping. Core, Runtime, Application, Hosting, and
protocol projects do not reference Observability.

The module owns one process-local structured event pipeline:

1. Lightweight host observers copy immutable event facts and call `TryWrite`
   on a bounded ingress channel.
2. A single background reader performs secret redaction, appends to a bounded
   retention ring, updates event-derived metrics, and offers events to bounded
   subscriber channels without waiting.
3. The event REST API and SignalR broker read from this pipeline. They do not
   maintain independent unbounded histories.

Observability must not own or mutate device state. It never writes through
`StateStore`, never restores state from logs, and never persists continuous
events to SQLite. Slow subscribers, full queues, unavailable OTLP collectors,
and Prometheus scrapes must not block the simulation tick.

OpenTelemetry uses batch export only. Prometheus metrics use fixed metric names
and bounded label vocabularies; device IDs, datapoint names, scenario names,
fault IDs, exception messages, and user metadata are not metric labels. Health
endpoints report process liveness separately from control-plane readiness.

## Alternatives

Putting telemetry directly in Core or Runtime was rejected because it reverses
the dependency boundary and couples deterministic logic to exporters.

Keeping the current host event queue, REST event history, and SignalR broker as
separate stores was rejected because retention, filtering, sequence, and drop
semantics would diverge.

Persisting runtime events to SQLite was rejected because storage locks would
couple observation to simulation and would turn SQLite into a continuous live
state/event store.

Using the prerelease OpenTelemetry Prometheus ASP.NET exporter was rejected for
Wave 3.1. The stable `prometheus-net.AspNetCore` package exposes metrics, while
stable OpenTelemetry packages provide tracing and optional OTLP batch export.

## Consequences

The Web host gains a single observable event sequence and explicit overload
evidence. Event retention is process-local and intentionally lossy under
pressure. Consumers recover by querying retained history or refreshing runtime
state from the authoritative APIs.

Hosting gains only protocol-independent observation hooks for tick, scenario,
and adapter lifecycle facts. These hooks cannot control execution. The existing
`SimulationHost.Events` compatibility view becomes bounded and is no longer the
Web event API source.

## Failure modes

- A full ingress or subscriber channel increments a dropped-event counter and
  discards that observation without waiting.
- A failed background event pump makes readiness unhealthy but does not stop an
  existing simulation.
- SQLite unavailability makes readiness unhealthy and rejects control-plane
  work, but liveness remains healthy and existing simulations continue.
- An OTLP collector outage is handled by the OpenTelemetry batch processor and
  does not run exporter I/O on simulation or request threads.
- Secret-like keys and known credential text patterns are replaced with
  `[REDACTED]` before retention, subscription, API serialization, or custom
  trace tagging.

## Reversal conditions

A durable event store, remote log backend, or different metrics exporter
requires a separate design with measured load, retention, privacy, and failure
isolation evidence. It may replace the observer implementation but may not
change `StateStore` authority or introduce synchronous external I/O into ticks.
