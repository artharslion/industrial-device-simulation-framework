# ADR 0002: StateStore authority and SQLite persistence

- Status: Accepted
- Date: 2026-09-03

## Context

The platform must restore definitions and explicit snapshots while preserving
deterministic, concurrent live simulation behavior.

## Decision

`StateStore` remains the only live datapoint authority. EF Core SQLite persists
device/scenario definitions, settings, users, metadata, and explicit versioned
snapshots. Restore passes through a validated hosting service. Protocol caches
and database rows never silently seed live state.

## Alternatives

Persisting every live write synchronously was rejected because database locks
would couple simulation ticks to the control plane. Adapter-owned persistence
was rejected because it breaks one-device/many-protocol state.

## Consequences

Running simulations can continue when SQLite is locked. Persistent mutations
fail explicitly. Restart uses initial state unless a compatible snapshot is
selected.

## Failure modes

Locked/unavailable storage returns a persistence error; incompatible snapshot
schema, device definition, time mode, or seed is rejected atomically.

## Reversal conditions

A different database provider may replace SQLite behind Application contracts;
live-state authority does not change without a new normative architecture ADR.
