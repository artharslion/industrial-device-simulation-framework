# Visual Modeling Console Design

## Scope

This increment delivers the product workflows that turn the Wave 1 platform
foundation into a usable developer console:

- a local, versioned device-template catalog with visual creation and editing;
- a structured scenario editor with YAML import and export;
- a responsive, role-aware, multi-page Vue console.

Template marketplace distribution, automated test suites, forwarding,
Webhooks, recording/replay, and additional protocols remain out of scope. A
template catalog is an internal project asset store, not a marketplace.

## Architecture

`IndustrialSim.Templates` owns template documents, semantic versions,
validation, instantiation, and protocol-mapping profiles. Template documents
describe device definitions and behavior metadata; mapping profiles remain
separate so a logical device is still independent from protocol addresses.
Application defines catalog use cases and repository contracts. Persistence
stores immutable template versions and mapping profiles in SQLite. Web exposes
the workflows through `/api/v1/templates` and keeps ASP.NET Core concerns out
of Application and Core.

Scenario persistence gains a separate editor document containing stable step
IDs and visual ordering metadata. Executable YAML remains the runtime input and
is always parsed by the existing Scenario parser before it is saved. Changing
editor layout cannot change runtime semantics. Import and export operate on
public YAML contracts; the visual editor produces the same YAML.

The Vue application uses Vue Router for real URL-addressable pages. Pinia holds
only presentation state shared between pages: selected device, authentication
session, connection state, and notices. Runtime state continues to come from
StateStore through `/api/v1` and SignalR. The existing polling path remains a
disconnect fallback.

## User experience

The console retains the current dark Linear-inspired industrial workspace. A
persistent navigation rail exposes Overview, Devices, Templates, Scenarios,
Protocols, Events, Users, and Settings. Read-only pages remain useful to
Viewer users; mutations are hidden or disabled according to the server role,
while the server remains the authorization authority.

Template creation is a three-part visual form: identity/version, datapoints,
and protocol mappings. Datapoints and mappings are editable rows with explicit
types, access, defaults, addresses, and byte/word-order fields. Saving creates
an immutable version; editing an existing template requires a new version.
Instantiation asks for a device ID, seed, deterministic mode, and selected
mapping ports before calling the normal device creation use case.

The scenario editor uses an ordered flow of step cards. Each card exposes a
trigger (`at` or `when`) and an action (`set`, `command`, or `fault`) through
keyboard-accessible controls. Users can reorder, duplicate, and remove steps,
preview generated YAML, import YAML, export YAML, save with optimistic
versioning, and run a saved scenario against a selected device.

## Validation and failure handling

- Template IDs and versions are normalized and immutable.
- Datapoint names are unique and use supported Core data types/access modes.
- Mapping profiles reference an existing template version and known datapoints.
- Protocol-specific mapping JSON is validated at the template boundary without
  adding protocol dependencies to Core.
- Scenario YAML must parse before persistence.
- Optimistic concurrency conflicts return RFC Problem Details with stable
  `errorCode` values.
- Imported documents are treated as data and rendered with Vue text bindings;
  no imported HTML is executed.

## Verification

Focused unit tests cover template versioning, validation, mapping separation,
instantiation, scenario round trips, and repository reopen behavior. Web tests
cover API CRUD/import/export, authorization, and Problem Details. Vitest covers
routing, template editing, scenario editing, API failures, and SignalR/polling
continuity. The final gate runs all Vue and .NET Release checks and a manual
responsive visual inspection of the built console.
