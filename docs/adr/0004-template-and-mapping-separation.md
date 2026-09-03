# ADR 0004: Separate device templates and protocol mappings

- Status: Accepted
- Date: 2026-09-03

## Context

Baseline templates must not reintroduce the model where a device belongs to one
protocol.

## Decision

Future `DeviceTemplate` artifacts contain protocol-neutral definitions and
behavior defaults. Separate immutable `ProtocolMappingProfile` artifacts own
addresses, nodes, topics, encoding, and access details.

## Alternatives

Combined device/protocol templates were rejected because they duplicate device
state and prevent composable multi-protocol exposure.

## Consequences

Instantiation records exact template and mapping versions. Application
validation composes them before registry creation; Core remains unaware of
template catalogs and mapping JSON.

## Failure modes

Missing/incompatible mapping versions fail before runtime creation. Publishing
a new version never mutates existing instances.

## Reversal conditions

Only a demonstrated protocol whose mapping cannot be represented separately
may motivate a new composition model, and it must still preserve Core
independence and one state authority.
