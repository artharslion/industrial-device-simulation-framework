# ADR 0005: Protocol compatibility gates

- Status: Accepted
- Date: 2026-09-03

## Context

ProtoForge markets broad protocol coverage, but source folders or partial
parsers are not credible compatibility claims.

## Decision

Every protocol publishes a capability manifest with transport/platform limits,
services, datatypes, security modes, known constraints, tested external tool
and version, and evidence. Legal, license, and SDK feasibility precede code.

## Alternatives

Stub adapters and unqualified “supported” labels were rejected. Requiring all
protocols in the main Linux image was rejected for Windows/proprietary cases.

## Consequences

Protocols may be `Constrained`. OPC DA is an optional Windows host; proprietary
profiles are described narrowly when full interoperability cannot be proven.

## Failure modes

An adapter bind/crash affects only that adapter. Missing evidence blocks a
Verified matrix status but does not block truthful constrained documentation.

## Reversal conditions

Gates may be strengthened as interoperability infrastructure improves; they
may not be weakened merely to claim feature parity.
