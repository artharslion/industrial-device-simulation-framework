# ADR 0006: Optional local authentication and RBAC

- Status: Accepted
- Date: 2026-09-03

## Context

Local trusted development needs zero-friction operation, while shared platform
deployments need users and authorization.

## Decision

Support `Auth:Mode=Disabled|LocalIdentity`. LocalIdentity uses ASP.NET Core
Identity with SQLite and roles `Viewer`, `Operator`, and `Admin`. Viewer reads;
Operator performs runtime/scenario/fault operations; Admin also manages users,
settings, protocols, and integrations. No fixed password ships with the app.

## Alternatives

Always-on authentication was rejected for local compatibility. Custom password
hashing and authorization were rejected in favor of maintained framework
components.

## Consequences

Disabled mode preserves the v0.1 developer experience. Enabled mode fails
closed. Bootstrap is one-time, and credentials, hashes, tokens, and secrets are
excluded from logs, snapshots, exports, and ordinary API responses.

## Failure modes

Identity storage/provider failures do not stop existing simulations; protected
operations fail. Role policy mistakes are covered by endpoint tests.

## Reversal conditions

External identity providers may be added behind the Web boundary when required;
Application use cases remain independent of provider and ASP.NET Core types.
