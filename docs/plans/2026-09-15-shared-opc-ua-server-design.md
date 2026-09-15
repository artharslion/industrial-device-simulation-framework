# Shared OPC UA Server Hosting Design

**Status:** Completed and verified on 2026-09-15

## Context and requirements

Each `SimulationHost` currently constructs an `OpcUaAdapter`. When the host
starts, that adapter creates its own OPC UA `ApplicationInstance`, certificate
configuration, `IndustrialOpcUaServer`, `IndustrialNodeManager`, and TCP
listener. `IndustrialNodeManager` is permanently bound to one
`IDeviceRuntime`. `SimulationRegistry` reserves every protocol port for one
simulation, so two otherwise compatible OPC UA launch definitions cannot use
the same endpoint. A `disconnect` Network Fault also stops the adapter's wire
server, which is safe only while the listener belongs to one device.

The applicable requirements are `PROJECT_SPEC.md` sections 13-14, 31, 33-35,
46-50, and 60-61: `StateStore` owns live state; protocol adapters route reads,
writes, commands, events, and network faults across a transport boundary; and
a protocol failure must not stop simulation. The post-v0.1 multi-device and
restore rules additionally require isolated startup failure and safe port
ownership. SQLite remains a control-plane store and does not persist continuous
live values.

## Alternatives

### A. One fixed global server and endpoint

This is the smallest listener model, but it would turn one endpoint into a
process-wide singleton configuration and break devices that intentionally use
dedicated endpoints. CLI overrides, restored catalog devices, and tests that
select independent ports would require special cases. It is rejected because
it removes a supported deployment shape without an architectural benefit.

### B. A process-level pool keyed by normalized endpoint

Compatible devices using the same normalized endpoint share one server and
listener; devices using different endpoints remain independent. This retains
the current per-device endpoint contract and requires no proxy process. The OPC
UA SDK already supports one `StandardServer` with a NodeManager containing many
objects, so ownership can move from the adapter to an endpoint host without
moving runtime state. This is the selected approach.

### C. Per-device servers behind a proxy or port forwarder

This preserves the existing adapter internals but adds another network process,
certificate/application identity boundary, failure mode, and address-space
aggregation layer. The proxy would need to translate sessions, subscriptions,
methods, and status codes, which is substantially broader than this milestone.
It is rejected.

## Ownership and components

`OpcUaEndpointHostManager` is a process-level, concurrency-safe pool owned by
the composition root. `SimulationRegistry` creates one manager by default, and
all hosts created or added to that registry receive it. Directly created hosts
use a private manager so existing CLI and focused adapter usage still work.
The manager owns endpoint hosts; an endpoint host owns exactly one OPC UA
`ApplicationInstance`, certificate/application identity, listener, and
multi-device NodeManager. It does not own simulation engines, device behavior,
fault lifecycle, commands, or `StateStore` values.

An `OpcUaAdapter` becomes a device-scoped projection and registration handle.
Starting it registers an immutable projection containing the simulation key,
`IDeviceRuntime`, configured datapoint NodeIds, and a device-scoped transport
fault controller. Stopping it unregisters that projection. Its existing
in-process `Read`, `Write`, and `InvokeMethodAsync` helpers continue to route
directly to the same runtime and remain useful to tests. `IsRunning` means the
projection is registered; server/listener status is obtained from the shared
registration.

The first successful registration creates and starts the endpoint host before
the member is reported running. Later compatible registrations add device
nodes to the existing NodeManager. Removing a non-final member removes only its
nodes, runtime event subscription, and device resources. Removing the final
member stops the server and removes the endpoint host from the pool, releasing
the port. Start and stop are idempotent at the adapter/host boundary.

## Endpoint identity and compatibility

`OpcUaEndpointDescriptor` parses and normalizes the configured URI before pool
lookup. The key includes lower-cased `opc.tcp` scheme, normalized bind host,
port, and normalized path. Empty path and `/` are equivalent; a trailing slash
is removed otherwise. Host aliases are not guessed: `localhost`, `127.0.0.1`,
and `0.0.0.0` remain distinct configurations because they describe different
bind or advertised identities. Query strings, fragments, user info, and ports
outside 1-65535 are rejected.

The OPC Foundation server used here binds base addresses as server-level
configuration. This milestone permits one normalized endpoint per TCP port in
the process. Two OPC UA endpoints with the same port but a different host or
path are rejected with an actionable `portConflict`/incompatible endpoint
message rather than attempting unsupported path multiplexing. A Modbus or Web
listener on the same port remains illegal.

The current public configuration exposes no per-device certificate, security
policy, ApplicationUri, application name, transport quota, or user-token
settings. The shared host therefore uses the existing server defaults:
application name `IndustrialSim`, ApplicationUri `urn:industrial-sim:server`,
anonymous access, and `None` security policy. These are explicitly server-level
settings. If such settings are exposed later, they must be part of the endpoint
descriptor compatibility signature; a mismatch must fail fast and must never
silently select one member's values.

## Registration, concurrency, and rollback

The manager serializes create/register/unregister operations per normalized
endpoint and protects the port-to-endpoint ownership map under one manager
gate. Registration validates endpoint compatibility, simulation-key
uniqueness, configured NodeId uniqueness within the device, and global NodeId
uniqueness within the shared namespace before mutating the running server.
Concurrent first registrations can create only one endpoint host and listener.

Server startup is transactional: configuration/certificate/server startup must
complete before the host is published in the pool. Device node registration is
then transactional within the NodeManager. If node creation fails, all nodes
and runtime subscriptions created for that device are removed. If the failed
device was the first member, the newly started server is stopped and the port
ownership entry is removed. A failure joining an already-running host leaves
existing members untouched.

Unregistration first removes the named member from the address space and
detaches its runtime event handler. Only after successful member removal may
the last-member path stop the listener. Repeated unregistration returns a
defined no-op result. Stop failures are surfaced, but the manager retains enough
ownership information to prevent another server from incorrectly claiming the
port until cleanup succeeds. Registry lifecycle gates continue to serialize
operations for one simulation, while the manager handles races across devices.

## Address space and stable identifiers

The shared server exposes:

```text
Objects/
  IndustrialSim/
    Devices/
      {simulation-key}/
        Metadata/
        State/
        Datapoints/
          {datapoint}
        Commands/
          {command}()
        Faults/
```

The simulation key is the immutable `DeviceDefinition.Id` and is the identity
used for registration, routing, and uniqueness. The current model contains no
separate simulation ID, so inventing one would add unsupported scope. Device
type and future display name are metadata only and never identity. BrowseNames
use logical names for readability, while NodeIds use stable strings.

For backward compatibility, configured datapoint NodeIds remain authoritative.
Without a mapping, datapoints keep `${deviceId}/${datapoint}` and commands keep
`${deviceId}/${command}`. Folder NodeIds use reserved framework prefixes such
as `industrial-sim`, `industrial-sim/devices`, and
`${deviceId}/$metadata`. The namespace URI remains
`urn:industrial-sim:runtime`, so existing namespace-index-2 tests continue to
work when the server has the same namespace table. Device registration rejects
any configured NodeId that collides with framework folders, another device's
datapoint, or a command. Re-registering the same launch definition produces
the same NodeIds.

## State routing, writes, commands, and subscriptions

Each registered device projection stores a reference to its existing
`IDeviceRuntime`, not a copy of state. OPC UA reads call that runtime's `Read`.
Writes call `Write`, preserving datatype/access validation and the StateStore
transition/event pipeline. Methods call `InvokeCommandAsync`, preserving device
behavior and command events. Routing is resolved from the NodeId to the owning
projection, never from a protocol address back into Core.

The NodeManager subscribes to each projection's `RuntimeEventPublished` event.
For a `DataPointChanged`, it updates only the corresponding OPC UA variable's
protocol cache and clears that node's change masks so SDK subscriptions receive
the change. This cached `NodeState.Value` is an OPC UA notification artifact,
not an authority: every service read re-reads the target `StateStore`, and no
simulation or other adapter reads the cached value. Device removal detaches the
event handler and deletes the device subtree, monitored-node resources, and
routing entries. NodeManager address-space mutations are serialized on the SDK
NodeManager lock; runtime callbacks never hold a StateStore lock while waiting
for another device.

## Network Fault isolation

Network Fault remains device-and-protocol scoped. The transport fault
controller moves into each registered projection. `latency` delays only
services targeting that device; `timeout` returns `BadTimeout` only for that
device; and `disconnect` returns `BadNotConnected` only for that device. The
shared listener, client session, and other devices remain available. Data
change notifications for the faulted device are suppressed while disconnect or
timeout is active and the latest `StateStore` value is published when the fault
recovers. Simulation ticks continue throughout.

This is a deliberate shared-endpoint refinement of the old single-device
`disconnect` implementation, which stopped the listener. Closing a TCP session
for only one logical subtree is not representable without also disconnecting
access to other members on that session. Returning device-scoped bad service
status is the minimal equivalent that preserves isolation. Dedicated endpoints
also use this semantic after the change for consistency. No new fault types or
YAML fields are introduced.

## Registry and port model

Port ownership changes from `port -> simulation id` to `port -> listener
owner`. For Modbus the listener owner is still one simulation. For OPC UA it is
the normalized endpoint host and may have several simulation members.
`SimulationRegistry` accepts a duplicate OPC UA port only when both bindings
resolve to the same normalized endpoint descriptor. It continues rejecting
OPC UA versus Modbus, duplicate bindings inside one launch, and distinct OPC UA
endpoint descriptors that require the same TCP port.

Creating a registry entry reserves compatible endpoint membership but does not
open the listener; starting the first member opens it. A stopped member keeps
its launch reservation, matching the current registry behavior and preventing
another protocol from taking its intended port. Removing or replacing a member
recalculates endpoint membership. The reservation is released only when no
registered simulation launch refers to that endpoint, while the actual TCP
listener is released when no started projection remains.

Startup rollback stops only protocols started by the failing simulation. If an
OPC UA registration joined an existing host and a later Modbus start fails, the
OPC UA projection is unregistered but the shared server and other members stay
running. Stop ordering similarly unregisters only the current device before
stopping its engine.

## Configuration compatibility and unsupported cases

The YAML and launch-document schema remain unchanged. Multiple devices that
configure the same normalized `protocols.opcua.endpoint` share one server.
One device on an endpoint remains a natural dedicated deployment. The Web API
`port` shorthand still expands to `opc.tcp://0.0.0.0:{port}`. Existing custom
datapoint NodeIds remain supported but must now be unique across all devices on
the shared endpoint.

Unsupported cases fail explicitly: non-`opc.tcp` URIs; URI query, fragment, or
credentials; multiple different endpoint descriptors on one TCP port;
colliding custom NodeIds; duplicate device IDs on one endpoint; and future
server-level setting mismatches. The implementation does not add reverse
proxies, multiple security identities on one server, per-device certificate
selection, authentication, or path-based virtual servers.

## Test and acceptance scope

Focused lifecycle tests cover normalized keys, first/second registration,
single listener creation, independent endpoints, last-member release,
idempotence, concurrent registration/unregistration, rollback, and incompatible
configuration. Real OPC UA client tests browse two device subtrees, read and
write same-named datapoints independently, invoke commands, receive isolated
data changes, remove/re-register stable nodes, and keep one device usable after
the other stops.

Hosting tests cover shared endpoint reservation, cross-protocol conflicts,
replace/remove rollback, partial protocol startup failure, and original
single-device behavior. Fault tests prove a device-scoped timeout/disconnect
does not stop ticks, the shared listener, the other device's read/write or
subscription, and recovery exposes the latest state. Configuration and launch
document tests prove no YAML migration is required.

Final verification includes all .NET tests, documentation contracts,
`git diff --check`, the existing OPC UA and Modbus regressions, a Docker build
when the daemon is available, and a manual two-device listener/browse/write/
fault/stop check using the repository OPC UA client code. Third-party client
interoperability is not claimed unless separately executed and recorded.
