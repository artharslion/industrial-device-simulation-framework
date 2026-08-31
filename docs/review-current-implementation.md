# Current Implementation Review Notes

## Review Scope

This document records the implementation currently present in the repository
for review by another session. It is intentionally descriptive and does not
define a new architecture.

Baseline commits:

- `4ffbdd0` - protocol server lifecycle and shared-state wiring
- `c0819f4` - expanded validated Modbus mapping kinds
- `e73aceb` - start protocol servers from options and apply transport faults

The working tree also contains an untracked `docs/plans/` directory that
predates this review document.

## Intended Simulator Flow

The intended product flow remains:

```text
YAML configuration
    -> DeviceDefinition
    -> Runtime / StateStore
    -> SimulationEngine, ScenarioRunner, FaultManager
    -> thin protocol mappings and optional external protocol servers
```

`StateStore` is the only runtime state authority. Protocol code should not
own device state, device behavior, or scenario execution.

## Implemented Changes

### Modbus configuration

`ModbusMappingConfiguration` now accepts explicit fields for:

- `coil`
- `discreteInput`
- `inputRegister`
- `holdingRegister`
- legacy `register`

`ModbusMappingValidator` enforces one address kind per mapping, address bounds,
boolean types for bit mappings, width bounds, and overlap checks within the
same address kind.

### Modbus adapter

`ModbusAdapter` currently provides:

- `TcpListener` startup and shutdown.
- Port injection through `ProtocolOptions.Port` or `StartServerAsync`.
- Function-code handling for basic reads (`1`, `2`, `3`, `4`) and writes (`5`,
  `6`, `16`).
- Big-endian encoding for several integer and float types.
- Illegal-address exception responses.
- Transport disconnect/timeout and latency flags.
- All logical reads and writes routed through `IDeviceRuntime` and therefore
  `StateStore`.

Known limitations requiring review:

- The wire implementation is minimal and does not yet cover every configured
  datatype, byte order, word order, or multi-register write semantics.
- There is no real TCP client test covering all supported function codes.
- Mapping access permissions are not fully enforced at the protocol function
  level.
- `discrete`/`input` mapping naming is internal and should be checked against
  the public YAML contract.

### OPC UA adapter

`OpcUaAdapter` currently provides:

- Runtime attachment/detachment and `IsRunning` lifecycle state.
- Endpoint and port properties with a default endpoint string of
  `opc.tcp://0.0.0.0:4840`.
- A basic `TcpListener` opened by `StartServerAsync` or a non-zero
  `ProtocolOptions.Port`.
- In-process `Read`, `Write`, and `InvokeMethodAsync` calls mapped to the
  runtime.
- A `Nodes` collection derived from the runtime definition.
- Forwarding of `StateStore.DataPointChanged` events.
- In-process disconnect/timeout and latency behavior.

## Critical OPC UA Status

The current OPC UA implementation is **not a usable OPC UA Server**.

It does not implement the OPC UA binary protocol or an OPC Foundation server
host. In particular, it has no:

- `ApplicationConfiguration` and server certificate setup.
- `StandardServer` instance.
- `CustomNodeManager2` or `NodeState` address space.
- Standard OPC UA endpoint negotiation.
- Browse, Read, Write, Call, Subscription, or DataChangeNotification services.
- Real OPC UA client compatibility test.

The TCP listener only accepts and immediately disposes raw TCP connections. A
client such as UaExpert or an OPC UA .NET client cannot browse or read nodes
from it. The adapter's in-process methods must not be presented as protocol
interoperability.

## Host Wiring

### CLI

`industrial-sim run <yaml>` now constructs an `InMemoryDeviceRuntime`, starts
the deterministic engine, and conditionally starts configured OPC UA/Modbus
adapters. It prints basic runtime/protocol state and stops adapters before
returning.

This is lifecycle wiring only. Long-running hosting, cancellation handling,
and a complete runtime/device registration flow still need review.

### Web

The Web project references both protocol projects and exposes:

- `/api/runtime`
- `/api/protocols`

The current Web program creates a hard-coded pump definition. It does not yet
load `protocols.opcua` or `protocols.modbus` from YAML and does not start the
real protocol services.

## Verification Performed

The following command passed after the latest implementation changes:

```text
dotnet test IndustrialSim.sln --configuration Release
```

Result: all 68 existing tests passed.

The following command also passed:

```text
dotnet build IndustrialSim.sln --configuration Release
```

The passing tests are primarily unit/contract tests. They do not prove real
OPC UA interoperability, and the existing Modbus tests are not yet a complete
external TCP client suite.

## Review Questions

1. Should v0.1 prioritize YAML-driven simulation and thin protocol-format
   mapping over implementing a complete OPC UA server stack?
2. If a real OPC UA endpoint remains required, should it be optional and
   isolated behind a separate host/integration package?
3. Should `IProtocolAdapter` expose mapping operations only, with network
   hosting moved to explicit protocol-host classes?
4. Which Modbus datatype and byte/word-order combinations are actually part of
   the v0.1 YAML contract?
5. Should the hard-coded Web pump be replaced with shared YAML loading before
   any further protocol work?

## Recommended Review Boundary

Treat `StateStore`, `SimulationEngine`, Scenario, Fault, and YAML loading as
the core simulator. Treat current protocol implementations as incomplete
adapters. Do not accept the current OPC UA listener as a real server without a
real OPC UA client test that successfully connects, browses, reads, writes,
invokes a Method, and receives a subscription update.
