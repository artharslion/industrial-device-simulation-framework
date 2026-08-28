# PROJECT_SPEC.md

# Industrial Device Simulation Framework

> **Define Once. Simulate Anywhere.**
>
> An open-source, developer-first runtime for defining, simulating, exposing, and testing virtual industrial devices across multiple industrial protocols.

---

# 1. Purpose

本项目是一个面向开发者的工业设备仿真框架。

它允许开发者：

1. 定义一个虚拟工业设备。
2. 为设备定义 DataPoint、State、Command 和 Event。
3. 使用简单的行为模型驱动设备状态变化。
4. 使用 Scenario 描述设备在时间轴上的行为。
5. 注入 Device / Data / Network Fault。
6. 通过不同 Protocol Adapter 暴露同一个设备。
7. 在没有真实工业硬件的情况下进行开发和 Integration Testing。
8. 在 Docker / CI 环境中运行确定性的工业设备仿真。

---

# 2. Scope

## 2.1 v0.1 Goals

v0.1 必须实现：

```text
Device Model
    +
Simulation Runtime
    +
YAML Configuration
    +
Scenario Engine
    +
OPC UA Adapter
    +
    Modbus TCP Adapter
    +
    Web UI
    +
    CLI
    +
Docker
```

最小可用场景：

```text
YAML
 ↓
Pump Device
 ↓
Simulation Engine
 ↓
OPC UA Server
 ↓
Modbus TCP Server
 ↓
Scenario
 ↓
State Changes
```

---

# 3. Non-Goals

v0.1 不实现：

- 3D visualization
- Unity / Godot integration
- Full Digital Twin platform
- Full AAS platform
- PLC programming runtime
- IEC 61131 compiler
- PROFINET
- EtherCAT
- Real-time fieldbus simulation
- Full physical simulation
- MES / SCADA platform
- Enterprise asset management
- Authentication / authorization
- Multi-user management

这些功能不属于 v0.1。

---

# 4. Design Principles

## 4.1 Device First

核心对象是：

```text
Device
```

而不是：

```text
Protocol
```

Protocol 只是 Device 的一种访问方式。

---

## 4.2 Protocol Independent Core

Core Domain 不允许依赖具体工业协议。

错误：

```text
Pump
 └── ModbusRegister
```

正确：

```text
Pump
 └── DataPoint

ModbusAdapter
 └── DataPoint → Register
```

---

## 4.3 Simulation First

Scenario 修改的是：

```text
Device State
```

而不是：

```text
OPC UA Node
Modbus Register
```

正确：

```text
Scenario
   ↓
Device State
   ↓
Simulation Runtime
   ↓
Protocol Adapters
```

---

## 4.4 Deterministic by Design

Simulation Runtime 必须支持 deterministic execution。

相同：

```text
Device Definition
+
Scenario
+
Initial State
+
Random Seed
```

应该得到相同结果。

---

## 4.5 Simple Configuration

用户应该能够通过一个 YAML 文件启动一个设备。

目标：

```bash
industrial-sim run pump.yaml
```

---

# 5. High-Level Architecture

```text
┌─────────────────────────────────────────────────────┐
│                  IndustrialSim CLI                  │
└───────────────────────┬─────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────┐
│                Simulation Runtime                   │
│                                                     │
│  SimulationEngine                                   │
│  SimulationClock                                    │
│  Scheduler                                          │
│  StateStore                                         │
└───────────────┬───────────────────┬─────────────────┘
                │                   │
                ▼                   ▼
        ┌──────────────┐    ┌─────────────────┐
        │ Device Model │    │ Scenario Engine │
        └──────┬───────┘    └────────┬────────┘
               │                     │
               └──────────┬──────────┘
                          ▼
                  ┌───────────────┐
                  │ Device State  │
                  └───────┬───────┘
                          │
             ┌────────────┼────────────┐
             ▼            ▼            ▼
        ┌────────┐   ┌────────┐   ┌────────┐
        │ OPC UA │   │ Modbus │   │ Future │
        │Adapter │   │Adapter │   │Adapter │
        └────────┘   └────────┘   └────────┘
```

---

# 6. Solution Structure

Recommended .NET solution:

```text
IndustrialSim.sln

src/
├── IndustrialSim.Core
├── IndustrialSim.Runtime
├── IndustrialSim.Scenarios
├── IndustrialSim.Faults
├── IndustrialSim.Configuration
├── IndustrialSim.Protocols.Abstractions
├── IndustrialSim.Protocols.OpcUa
├── IndustrialSim.Protocols.Modbus
├── IndustrialSim.Devices
├── IndustrialSim.Web
└── IndustrialSim.Cli

tests/
├── IndustrialSim.Core.Tests
├── IndustrialSim.Runtime.Tests
├── IndustrialSim.Scenarios.Tests
├── IndustrialSim.Faults.Tests
├── IndustrialSim.Protocols.OpcUa.Tests
├── IndustrialSim.Protocols.Modbus.Tests
├── IndustrialSim.Web.Tests
└── IndustrialSim.IntegrationTests

examples/
├── devices/
│   ├── pump.yaml
│   ├── motor.yaml
│   └── sensor.yaml
│
└── scenarios/
    ├── startup.yaml
    ├── overheating.yaml
    └── sensor-failure.yaml

docs/
├── architecture.md
├── device-model.md
├── scenario.md
├── protocols.md
└── fault-injection.md
```

---

# 7. Core Domain Model

The core domain consists of:

```text
Device
DataPoint
Value
State
Command
Event
Fault
Capability
```

---

# 8. Device

A `Device` represents a virtual industrial asset.

Conceptually:

```csharp
public interface IDevice
{
    string Id { get; }

    string Type { get; }

    IReadOnlyCollection<IDataPoint> DataPoints { get; }

    IReadOnlyCollection<ICommand> Commands { get; }

    IReadOnlyCollection<IEventDefinition> Events { get; }
}
```

A Device must not contain protocol-specific concepts.

---

# 9. DataPoint

A DataPoint represents a readable or writable value exposed by a Device.

Example:

```text
Pump
├── temperature
├── pressure
├── speed
└── running
```

Conceptual model:

```csharp
public interface IDataPoint
{
    string Name { get; }

    DataType DataType { get; }

    object? Value { get; }

    string? Unit { get; }

    DataPointAccess Access { get; }
}
```

---

# 10. DataPoint Access

Supported access modes:

```text
Read
Write
ReadWrite
```

Example:

```yaml
temperature:
  type: float
  access: read

speed:
  type: int
  access: readwrite
```

---

# 11. Supported Data Types

v0.1 must support:

```text
boolean
int8
int16
int32
int64
uint8
uint16
uint32
uint64
float
double
string
```

Optional:

```text
datetime
```

Complex structures are out of scope for v0.1.

---

# 12. DataPoint Metadata

A DataPoint may contain:

```yaml
temperature:
  type: float
  unit: "°C"
  description: "Pump motor temperature"
  initial: 25
  access: read
```

Supported metadata:

```text
name
type
unit
description
initial
access
```

Do not introduce arbitrary metadata until required.

---

# 13. Device State

Device state is the current runtime value of DataPoints.

Example:

```text
Pump-001 State

temperature = 42.5
pressure    = 3.2
speed       = 1450
running     = true
```

State must be centrally owned by the Runtime.

Protocol adapters must read/write through the Runtime state abstraction.

---

# 14. State Ownership

The Runtime is the source of truth.

```text
                 StateStore
                     │
          ┌──────────┼──────────┐
          ▼          ▼          ▼
       Scenario   Device      Protocol
                    Logic      Adapter
```

No Protocol Adapter should maintain an independent copy of Device state.

---

# 15. Commands

Commands represent actions that can be executed on a Device.

Example:

```text
Pump
├── start
└── stop
```

Conceptual interface:

```csharp
public interface ICommand
{
    string Name { get; }

    Task ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken);
}
```

Example:

```yaml
commands:
  start:
  stop:
```

---

# 16. Command Semantics

A command may:

- modify state
- trigger behavior
- emit events
- generate faults

Example:

```text
start()
    ↓
running = true
    ↓
speed begins increasing
```

---

# 17. Events

Events represent significant device state transitions.

Examples:

```text
PumpStarted
PumpStopped
Overheated
SensorFailure
EmergencyStop
```

Events are runtime concepts and are not tied to a particular protocol.

---

# 18. Device Behavior

Device behavior is responsible for changing state over simulation time.

Examples:

```text
Pump startup
Motor acceleration
Temperature increase
Cooling
Pressure decay
Sensor noise
```

A Device Behavior should not know whether the device is exposed via OPC UA or Modbus.

---

# 19. Behavior Model

v0.1 supports simple behavior primitives:

```text
Constant
Ramp
Random
Noise
StateTransition
```

Example:

```text
speed:
0 → 1450
over 10 seconds
```

---

# 20. Simulation Engine

The Simulation Engine controls:

- simulation time
- device updates
- scenario execution
- behavior execution
- fault execution
- state changes

Conceptual interface:

```csharp
public interface ISimulationEngine
{
    SimulationTime CurrentTime { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
```

---

# 21. Simulation Clock

The Runtime must abstract time.

```csharp
public interface ISimulationClock
{
    TimeSpan Elapsed { get; }

    DateTimeOffset UtcNow { get; }

    bool IsDeterministic { get; }
}
```

v0.1 supports:

```text
RealTimeClock
DeterministicClock
```

---

# 22. Real-Time Mode

Default mode:

```bash
industrial-sim run pump.yaml
```

Simulation time follows wall-clock time.

Example:

```text
1 real second = 1 simulation second
```

---

# 23. Deterministic Mode

Optional:

```bash
industrial-sim run pump.yaml --deterministic --seed 123
```

The simulation should advance according to controlled simulation time rather than depending directly on wall-clock timing.

---

# 24. Scenario Engine

Scenario Engine describes planned changes to Device state.

Scenario operates on:

```text
Device
DataPoint
Command
Fault
```

It must not operate directly on protocol addresses.

---

# 25. Scenario Actions

v0.1 supports:

```text
set
ramp
command
wait
```

Triggers:

```text
at
after
every
when
```

---

# 26. Scenario Example

```yaml
scenario:
  name: pump-startup

  steps:

    - at: 0s
      command:
        device: pump-001
        name: start

    - after: 1s
      ramp:
        device: pump-001
        datapoint: speed
        from: 0
        to: 1450
        duration: 10s
```

---

# 27. Scenario Conditions

v0.1 supports basic expressions:

```text
temperature > 90
speed == 0
running == true
pressure < 1.0
```

Example:

```yaml
- when:
    device: pump-001
    condition: "temperature > 90"

  set:
    device: pump-001
    datapoint: alarm
    value: true
```

Expression language should remain intentionally small.

Do not introduce a full scripting language in v0.1.

---

# 28. Fault Injection

Faults are first-class runtime objects.

Three categories:

```text
Data Fault
Device Fault
Network Fault
```

---

# 29. Data Faults

v0.1:

```text
Stale
Freeze
OutOfRange
Noise
Spike
```

Example:

```yaml
- after: 30s
  fault:
    type: stale
    target:
      device: pump-001
      datapoint: temperature
    duration: 20s
```

---

# 30. Device Faults

v0.1:

```text
SensorFailure
Overheat
PowerLoss
EmergencyStop
```

Example:

```yaml
- after: 40s
  fault:
    type: overheat
    device: pump-001
```

A Device Fault may alter:

```text
state
behavior
commands
events
```

---

# 31. Network Faults

v0.1 supports conceptual network faults:

```text
Disconnect
Timeout
Latency
```

Network faults belong to the Protocol Adapter / transport layer.

Example:

```yaml
- after: 60s
  fault:
    type: network.timeout
    protocol: opcua
    duration: 10s
```

---

# 32. Fault Lifecycle

A fault may be:

```text
Scheduled
Active
Recovered
```

Example:

```text
00:30 Fault scheduled
00:40 Fault active
00:50 Fault recovered
```

Faults must be observable by the Runtime.

---

# 33. Protocol Adapter

Protocol Adapter exposes the Device Runtime through an external industrial protocol.

Conceptual interface:

```csharp
public interface IProtocolAdapter
{
    string Name { get; }

    Task StartAsync(
        IDeviceRuntime runtime,
        ProtocolOptions options,
        CancellationToken cancellationToken);

    Task StopAsync(
        CancellationToken cancellationToken);
}
```

---

# 34. Protocol Adapter Rules

An Adapter:

- may read Device state
- may write Device state
- may invoke Commands
- may publish Events
- may inject transport-level faults

An Adapter must not:

- own Device state
- implement Device behavior
- execute Scenario logic
- contain Device-specific business logic

---

# 35. OPC UA Adapter

v0.1 must expose:

```text
Device
 ├── DataPoints
 ├── Commands
 └── Events
```

Basic mapping:

```text
Device
    ↓
OPC UA Object

DataPoint
    ↓
OPC UA Variable

Command
    ↓
OPC UA Method
```

Example:

```text
Pump-001
├── Temperature
├── Pressure
├── Speed
├── Running
├── Start()
└── Stop()
```

---

# 36. Modbus TCP Adapter

v0.1 supports:

```text
Coils
Discrete Inputs
Input Registers
Holding Registers
```

The mapping must be explicit.

Example:

```yaml
modbus:
  port: 5020

  mappings:

    temperature:
      register: 100
      type: float32

    pressure:
      register: 102
      type: float32

    speed:
      register: 104
      type: uint16

    running:
      coil: 10
```

---

# 37. Modbus Mapping Rules

The Core must not know:

```text
register 100
coil 10
```

These belong exclusively to the Modbus Adapter configuration.

---

# 38. Protocol Configuration

Protocol configuration is separate from Device definition.

Recommended structure:

```yaml
device:
  ...

protocols:

  opcua:
    enabled: true
    endpoint: "opc.tcp://0.0.0.0:4840"

  modbus:
    enabled: true
    port: 5020

web:
  enabled: true
  port: 8080
```

---

# 39. Complete v0.1 YAML

Example:

```yaml
device:
  id: pump-001
  type: pump
  name: Pump 001

  datapoints:

    temperature:
      type: float
      unit: "°C"
      initial: 25
      access: read

    pressure:
      type: float
      unit: "bar"
      initial: 3.2
      access: read

    speed:
      type: int
      unit: "rpm"
      initial: 0
      access: readwrite

    running:
      type: boolean
      initial: false
      access: readwrite

    alarm:
      type: boolean
      initial: false
      access: read

  commands:
    start:
    stop:

protocols:

  opcua:
    enabled: true
    endpoint: "opc.tcp://0.0.0.0:4840"

  modbus:
    enabled: true
    port: 5020

    mappings:

      temperature:
        register: 100
        type: float32

      pressure:
        register: 102
        type: float32

      speed:
        register: 104
        type: uint16

      running:
        coil: 10

      alarm:
        coil: 11

web:
  enabled: true
  port: 8080
```

---

# 40. Device + Scenario Separation

Device definition：

```yaml
device:
  ...
```

Scenario：

```yaml
scenario:
  ...
```

必须允许独立存在。

例如：

```text
devices/pump.yaml

scenarios/
├── startup.yaml
├── overheating.yaml
└── sensor-failure.yaml
```

同一个 Device 可以运行多个 Scenario。

---

# 41. CLI

v0.1 CLI commands：

```bash
industrial-sim run <file>
industrial-sim validate <file>
industrial-sim scenario run <file>
```

---

# 42. CLI Examples

Start:

```bash
industrial-sim run devices/pump.yaml
```

Validate:

```bash
industrial-sim validate devices/pump.yaml
```

Run scenario:

```bash
industrial-sim scenario run scenarios/overheating.yaml
```

Deterministic:

```bash
industrial-sim run devices/pump.yaml \
  --deterministic \
  --seed 123
```

---

# 43. Logging

v0.1 must provide structured console logging.

Example:

```text
[00:00:00] Device pump-001 started
[00:00:01] Command start executed
[00:00:05] speed = 725
[00:00:10] speed = 1450
[00:00:35] temperature = 91
[00:00:35] Fault overheat activated
```

Use standard .NET logging abstractions.

---

# 44. Error Handling

The Runtime must distinguish:

```text
Configuration Error
Runtime Error
Protocol Error
Scenario Error
Fault Error
```

Configuration errors should fail fast.

Example:

```text
Invalid Modbus mapping:
temperature register overlaps pressure register.
```

---

# 45. Validation

Before starting the Runtime:

1. Parse YAML.
2. Validate Device.
3. Validate DataTypes.
4. Validate DataPoint references.
5. Validate Commands.
6. Validate Scenario references.
7. Validate Protocol mappings.
8. Validate conflicting Modbus addresses.

Only after validation succeeds should the Runtime start.

---

# 46. Thread Safety

Runtime state may be accessed concurrently by:

```text
Simulation Engine
Scenario Engine
OPC UA Server
Modbus Server
CLI/API
```

State updates must therefore be thread-safe.

The implementation should prefer a single logical state transition mechanism rather than allowing arbitrary concurrent mutation.

---

# 47. State Change Pipeline

All state changes should conceptually pass through:

```text
Request
   ↓
Validation
   ↓
State Transition
   ↓
Event
   ↓
Observers / Protocol Adapters
```

Example:

```text
Scenario
   ↓
Set temperature = 95
   ↓
StateStore
   ↓
TemperatureChanged
   ↓
OPC UA Variable updated
   ↓
Modbus Register updated
```

---

# 48. Event Model

v0.1 events:

```text
DataPointChanged
CommandExecuted
FaultActivated
FaultRecovered
DeviceStarted
DeviceStopped
```

Events should contain:

```text
timestamp
deviceId
eventType
previousValue
newValue
metadata
```

---

# 49. Testing Strategy

Testing is a first-class concern.

## Unit Tests

Test:

```text
Device
DataPoint
StateStore
Scenario
Scheduler
Fault
```

---

## Protocol Tests

Test:

```text
OPC UA mapping
Modbus mapping
Read
Write
Command invocation
```

---

## Integration Tests

Example:

```text
Start simulator
     ↓
Connect OPC UA client
     ↓
Read temperature
     ↓
Run scenario
     ↓
Read temperature again
     ↓
Assert changed value
```

---

# 50. First Integration Test

The first end-to-end test should prove:

```text
Pump
 ↓
OPC UA
 ↓
Client
```

and:

```text
Pump
 ↓
Modbus
 ↓
Client
```

Then:

```text
Scenario
 ↓
Pump State
 ↓
OPC UA + Modbus
```

Both protocols must observe the same logical Device state.

---

# 51. Docker

The Runtime must run in a container.

Example:

```bash
docker run \
  -p 4840:4840 \
  -p 5020:5020 \
  -p 8080:8080 \
  industrial-sim \
  run /config/pump.yaml
```

---

# 52. Docker Compose

Example target:

```yaml
services:

  simulator:
    image: industrial-sim
    ports:
      - "4840:4840"
      - "5020:5020"
      - "8080:8080"
    volumes:
      - ./devices:/config/devices
      - ./scenarios:/config/scenarios
```

---

# 53. Configuration Loading

Configuration sources:

Priority:

```text
CLI arguments
    >
Environment Variables
    >
YAML
    >
Default Values
```

Environment variables should be supported for:

```text
OPC UA endpoint
Modbus port
Web UI port
log level
```

---

# 54. Device Extensibility

Device implementations should be extensible.

Example:

```csharp
public interface IDeviceFactory
{
    bool CanCreate(string type);

    IDevice Create(DeviceDefinition definition);
}
```

Configuration:

```yaml
device:
  type: pump
```

Runtime:

```text
"pump"
 ↓
PumpFactory
 ↓
PumpDevice
```

---

# 55. Built-in Devices

v0.1:

```text
Pump
Motor
Sensor
```

## Pump

Minimum:

```text
temperature
pressure
speed
running
alarm
```

Commands:

```text
start
stop
```

---

## Motor

Minimum:

```text
speed
temperature
current
running
alarm
```

Commands:

```text
start
stop
```

---

## Sensor

Minimum:

```text
value
quality
```

Commands:

```text
reset
```

---

# 56. Device Behavior Example

Pump startup:

```text
start()
    ↓
running = true
    ↓
speed ramps
    ↓
temperature increases
    ↓
pressure follows speed
```

A simple deterministic model is sufficient.

No physical simulation engine is required.

---

# 57. Example Pump Model

Conceptually:

```text
speed += acceleration * dt

temperature += heatRate(speed) * dt
temperature -= coolingRate * dt

pressure = speed / maxSpeed * maxPressure
```

This is intentionally a simplified model.

The goal is deterministic and understandable behavior, not physical accuracy.

---

# 58. Scenario Example — Overheat

```yaml
scenario:
  name: overheating

  steps:

    - at: 0s
      command:
        device: pump-001
        name: start

    - after: 10s
      fault:
        type: overheat
        device: pump-001
```

Expected:

```text
temperature ↑
alarm = true
```

---

# 59. Scenario Example — Sensor Failure

```yaml
scenario:
  name: sensor-failure

  steps:

    - after: 20s
      fault:
        type: stale
        target:
          device: pump-001
          datapoint: temperature
        duration: 30s
```

Expected:

```text
temperature stops changing
```

---

# 60. Scenario Example — Network Failure

```yaml
scenario:
  name: network-failure

  steps:

    - after: 30s
      fault:
        type: network.timeout
        protocol: opcua
        duration: 10s
```

Expected:

```text
OPC UA requests timeout
Device simulation continues
Network recovers after 10s
```

---

# 61. Important Runtime Rule

A Protocol failure must not automatically stop the Device simulation.

For example:

```text
OPC UA disconnected
```

does not mean:

```text
Pump stopped
```

The simulation and communication layers are independent.

```text
Device Runtime
      │
      ├── OPC UA connection
      ├── Modbus connection
      └── Simulation
```

This is important for testing network failures.

---

# 62. Observability

v0.1:

```text
Console Logs
```

v0.2/v0.3:

```text
Metrics
Prometheus
OpenTelemetry
```

Potential metrics:

```text
simulation_ticks_total
device_state_changes_total
scenario_actions_total
faults_active
protocol_connections
protocol_errors
```

---

# 63. Future Plugin Model

The architecture should eventually allow:

```text
Device Plugin
Protocol Plugin
Scenario Plugin
Fault Plugin
```

Potential future structure:

```text
plugins/
├── devices/
├── protocols/
└── faults/
```

Do not implement dynamic plugin loading unless needed for v0.1.

Keep interfaces extensible first.

---

# 64. Future Protocols

After v0.1:

```text
MQTT
S7
BACnet
EtherNet/IP
```

Potential later:

```text
OPC DA
ADS
IEC 61850
PROFINET
EtherCAT
```

Protocol implementation priority must be driven by:

1. Technical feasibility
2. Developer demand
3. Learning value
4. Integration-test usefulness
5. License / SDK constraints

---

# 65. Future AAS / AID Integration

Possible future architecture:

```text
AAS / AID
    ↓
Asset Definition
    ↓
Device Model
    ↓
Simulation Runtime
    ↓
Protocol Adapters
```

The project should not implement AAS from scratch.

Instead, integrate with existing standards where practical.

---

# 66. Version Roadmap

## v0.1

```text
Core
YAML
CLI
Pump
Motor
Sensor
Simulation Engine
Deterministic Clock Core
Scenario Engine
Data / Device / Network Faults
OPC UA
Modbus TCP
Developer Web UI
Docker
Unit / Protocol / E2E Tests
```

## v0.2

```text
State Machine Extensions
Seeded Replay
Snapshot / Restore
REST API
Integration Test Helpers
CI examples
```

## v0.3

```text
MQTT
S7
```

## v0.4

```text
BACnet
EtherNet/IP
```

## v1.0

```text
Stable Plugin Architecture
AAS/AID Integration
Metrics
Documentation
Community Device Repository
```

---

# 67. v0.1 Definition of Done

v0.1 is complete when all of the following work:

### Device

- [ ] Pump can be defined through YAML.
- [ ] Pump has DataPoints.
- [ ] Pump has Commands.
- [ ] Pump state changes over time.

### Runtime

- [ ] SimulationEngine runs.
- [ ] SimulationClock works.
- [ ] StateStore works.
- [ ] Device behavior is deterministic enough for tests.

### Scenario

- [ ] `at`
- [ ] `after`
- [ ] `every`
- [ ] `when`
- [ ] `set`
- [ ] `ramp`
- [ ] `command`
- [ ] `wait`

### Fault Injection

- [ ] Data faults: stale, freeze, out-of-range, noise, spike
- [ ] Device faults: sensor failure, overheat, power loss, emergency stop
- [ ] Network faults: disconnect, timeout, latency
- [ ] Fault lifecycle: scheduled, active, recovered

### Web UI

- [ ] View devices and current DataPoint state.
- [ ] Start, stop, pause, and reset a simulation.
- [ ] Load and run a Scenario.
- [ ] Activate and recover a Fault.
- [ ] Display event and runtime logs.

### OPC UA

- [ ] Server starts.
- [ ] Device appears as Object.
- [ ] DataPoints appear as Variables.
- [ ] Commands are callable.

### Modbus

- [ ] TCP server starts.
- [ ] Registers can be read.
- [ ] Coils can be read.
- [ ] Writable values work.

### Docker

- [ ] Container starts.
- [ ] OPC UA is accessible.
- [ ] Modbus is accessible.
- [ ] Web UI is accessible.

### Testing

- [ ] Unit tests.
- [ ] Protocol tests.
- [ ] End-to-end test.

---

# 68. First Milestone

The first milestone should be:

> **One Pump, Two Protocols, One Scenario.**

Specifically:

```text
Pump-001
    │
    ├── Temperature
    ├── Pressure
    ├── Speed
    └── Running
         │
         ├── OPC UA
         └── Modbus TCP
```

Scenario:

```text
Startup
 ↓
Speed 0 → 1450
 ↓
Temperature 25 → 70
```

Both OPC UA and Modbus clients must observe the same changes.

The scenario must also demonstrate one Data/Device Fault and one Network Fault,
with the same runtime state visible in the Web UI.

---

# 69. First Release Demo

The README must be able to demonstrate:

```bash
git clone ...
cd industrial-device-sim

docker compose up
```

Then:

```text
OPC UA
opc.tcp://localhost:4840

Modbus TCP
localhost:5020
```

Run:

```bash
industrial-sim scenario run examples/scenarios/overheating.yaml
```

Expected:

```text
Pump started
Speed increasing
Temperature increasing
Overheat detected
Alarm activated
```

---

# 70. Project Success Criteria

The project is successful if a developer can answer:

> “I don't have a PLC, pump, sensor, or Modbus device. How can I test my application?”

with:

```bash
docker compose up
```

and immediately get:

```text
Virtual Industrial Device
+
Real Industrial Protocol
+
Programmable Scenario
+
Injectable Fault
```

---

# 71. Core Architectural Principle

The most important rule in this project:

```text
                    Device Model
                         │
                         ▼
                Simulation Runtime
                         │
              ┌──────────┴──────────┐
              ▼                     ▼
        Scenario Engine       Fault Injection
              │                     │
              └──────────┬──────────┘
                         ▼
                  Device State
                         │
          ┌──────────────┼──────────────┐
          ▼              ▼              ▼
       OPC UA          Modbus          S7
```

**Never reverse this dependency.**

Protocols must adapt to the Device Model.

The Device Model must never be designed around a protocol.

---

# 72. Final Product Definition

The project should ultimately become:

> **A programmable runtime for virtual industrial devices.**

Not:

> A collection of protocol simulators.

Not:

> A 3D factory simulator.

Not:

> A digital twin platform.

Not:

> A PLC emulator.

The central abstraction is:

```text
Executable Device Model
```

with:

```text
Behavior
Scenario
Fault
Protocol
Testing
```

as first-class capabilities.

---

# 73. Guiding Question

For every future feature, ask:

> **Does this make it easier to define, simulate, expose, test, or intentionally break a virtual industrial device?**

If yes:

```text
Consider it.
```

If no:

```text
Defer it.
```

This rule should be used to prevent scope creep.

---

# 74. First Implementation Target

The first implementation should therefore be exactly:

```text
.NET
 │
 ├── Core
 │    ├── Device
 │    ├── DataPoint
 │    ├── Command
 │    └── State
 │
 ├── Runtime
 │    ├── Clock
 │    ├── Engine
 │    └── StateStore
 │
 ├── Scenario
 │    ├── at / after / every / when
 │    ├── set / ramp / command / wait
 │    └── Fault Injection
 │
 ├── Devices
 │    └── Pump
 │
 ├── Protocols
 │    ├── OPC UA
 │    └── Modbus
 │
 ├── CLI
 │
 └── Web UI
```

The first public release should keep this vertical slice focused; additional
protocols, devices, REST APIs, plugins, and standards integration come later.

---

# 75. One-Line Definition

> **Industrial Device Simulation Framework: define a virtual industrial device once, simulate its behavior deterministically, expose it through real industrial protocols, and intentionally make it fail.**

---
