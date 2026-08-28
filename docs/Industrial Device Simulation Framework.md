# Industrial Device Simulation Framework

> **Define Once. Simulate Anywhere.**
>
> An open-source, developer-first framework for simulating industrial devices, protocols, scenarios, and failures — without physical hardware.

---

## 1. Project Overview

### 1.1 Vision

构建一个面向开发者的 **Industrial Device Simulation Framework**。

项目的核心不是再实现一个 OPC UA Simulator，而是：

> **将工业设备抽象成可执行的 Device Model，通过 Protocol Adapter 暴露到不同工业协议，并通过 Scenario Engine 和 Fault Injection 模拟真实设备行为。**

核心目标：

- 在没有真实工业硬件的情况下模拟工业设备
- 用统一模型表达 Pump、Motor、Sensor、Valve、Energy Meter 等设备
- 同一个设备可以通过不同工业协议暴露
- 用 Scenario 描述设备行为和状态变化
- 支持 Device Fault、Data Fault、Network Fault
- 支持 Docker / CI / Integration Test
- 提供面向开发者的 Web UI，用于观察运行时、执行 Scenario 和注入 Fault
- 同时作为 Industrial Protocol Playground

---

# 2. Why This Project?

## 2.1 Existing Market

目前已经存在大量相关项目：

### Protocol Simulator

- OPC UA Simulator
- Modbus Simulator
- S7 Simulator
- BACnet Simulator
- MQTT Simulator

### Factory / Digital Twin

- Open Industry Project
- Factory I/O
- realvirtual
- Open Commissioning

### Industrial Connectivity

- OPC Foundation UA Edge Translator
- AAS / AID ecosystem

### Multi-protocol Simulator

- ProtoForge

因此：

> **“支持多个工业协议”本身不是足够的项目差异化。**

---

# 3. Research Conclusion

目前最重要的结论：

## Existing Competition

多协议工业模拟已经有人做。

尤其值得关注：

- ProtoForge
- Open Industry Project
- UA Edge Translator

因此不应该定位成：

> Another Multi-protocol Simulator

---

## Existing Standards

OPC UA Device Model 已经提供了非常成熟的工业设备信息模型。

AAS / IDTA AID 进一步解决：

- Asset Model
- Interface
- DataPoint
- Protocol Mapping

因此：

> **Unified Device Model 本身不是新的标准空白。**

但是这些标准主要解决：

```text
What is the device?
What data does it expose?
How can it be accessed?
How is it mapped?
```

而本项目真正关注：

```text
How does the device behave?
How does its state evolve?
What happens when it fails?
How can the failure be reproduced?
How can it be tested?
```

---

# 4. Identified Gap

目前没有发现一个已经形成明显主流地位的开源项目，将以下能力完整组合：

```text
Unified / Domain Device Model
        +
Executable Device Behavior
        +
Simulation Runtime
        +
Scenario Engine
        +
Fault Injection
        +
Multiple Protocol Adapters
        +
Deterministic Simulation
        +
Docker / CI
        +
Developer-first UX
```

需要注意：

> 不能据此断言“世界上不存在任何类似项目”。

更准确的判断是：

> **目前尚未发现一个在上述完整组合上形成明显生态和主导地位的开源项目。**

这构成了本项目最值得尝试的切入点。

---

# 5. Project Positioning

推荐定位：

> **Industrial Device Simulation Framework for Developers**

或者：

> **Build Virtual Industrial Devices Without Hardware**

另一个适合 GitHub README 的描述：

> **An open-source playground for industrial protocols and integration testing.**

核心关键词：

```text
Developer-first
Open-source
Docker-first
CLI-first
Test-first
Programmable
Multi-protocol
Scenario-driven
Deterministic
```

---

# 6. Target Users

项目不应该主要面向：

- 大型工厂数字孪生
- 3D Virtual Commissioning
- PLC Training
- Industrial CAD
- Full Factory Simulation

这些领域已有大量成熟工具。

主要目标用户：

### 6.1 Industrial Software Developers

开发：

- SCADA
- Gateway
- IoT Platform
- OPC UA Client
- Industrial API
- Protocol Adapter

---

### 6.2 QA / Integration Test Developers

需要：

```text
Virtual PLC
Virtual Sensor
Virtual Pump
Virtual Energy Meter
```

来进行自动化测试。

---

### 6.3 Industrial Protocol Developers

需要快速测试：

```text
OPC UA
Modbus
S7
MQTT
BACnet
EtherNet/IP
```

---

### 6.4 Developers Learning Industrial Protocols

项目同时作为：

> Industrial Protocol Playground

让开发者可以直观看到：

```text
Same Device
    ↓
OPC UA
    ↓
Object / Variable

Same Device
    ↓
Modbus
    ↓
Register

Same Device
    ↓
S7
    ↓
DB / Memory

Same Device
    ↓
MQTT
    ↓
Topic / Message
```

---

# 7. Core Concept

最重要的设计原则：

> **Device Model describes WHAT the device is.**
>
> **Protocol Adapter describes HOW the device is exposed.**

例如：

```text
Pump-001
│
├── Temperature
├── Pressure
├── Speed
├── Flow
└── Running
```

这些属于：

> Domain Model

而：

```text
OPC UA
    Pump-001/Temperature

Modbus
    Holding Register 100

S7
    DB10.DBD4
```

属于：

> Protocol Mapping

---

# 8. Executable Device Model

项目不应该只有静态 Device Description。

推荐：

```text
Device Model
=
Properties
+
State
+
Behavior
+
Commands
+
Events
+
Faults
+
Protocol Bindings
```

例如：

```text
Pump
│
├── Properties
│   ├── MaxSpeed
│   ├── MaxPressure
│   └── MaxTemperature
│
├── State
│   ├── Running
│   ├── Speed
│   ├── Pressure
│   └── Temperature
│
├── Behavior
│   ├── Startup
│   ├── Acceleration
│   └── Cooling
│
├── Commands
│   ├── Start
│   └── Stop
│
├── Faults
│   ├── Overheat
│   ├── Overload
│   └── SensorFailure
│
└── Interfaces
    ├── OPC UA
    ├── Modbus
    └── S7
```

---

# 9. Recommended Architecture

```text
Industrial Device Simulation Framework
│
├── Domain
│   ├── Device
│   ├── Capability
│   ├── DataPoint
│   ├── State
│   ├── Command
│   ├── Event
│   └── Fault
│
├── Runtime
│   ├── SimulationEngine
│   ├── SimulationClock
│   ├── Scheduler
│   ├── StateStore
│   └── DeterministicRunner
│
├── Behavior
│   ├── Generator
│   ├── StateMachine
│   ├── Physics-lite
│   └── ControlLogic
│
├── Scenario
│   ├── Timeline
│   ├── Trigger
│   ├── Action
│   ├── Generator
│   └── FaultInjection
│
├── Protocols
│   ├── OPC UA
│   ├── Modbus
│   ├── S7
│   ├── MQTT
│   ├── BACnet
│   └── EtherNet/IP
│
├── Devices
│   ├── Pump
│   ├── Motor
│   ├── Sensor
│   ├── Valve
│   └── EnergyMeter
│
├── Configuration
│   ├── YAML
│   └── JSON
│
└── Interfaces
    ├── CLI
    ├── Web UI
    ├── REST
    └── SDK
```

---

# 10. Protocol Adapter Architecture

Protocol Adapter 不应该直接依赖具体设备。

错误：

```text
ModbusAdapter
    ↓
Pump
```

推荐：

```text
                  Device Runtime
                       │
               DataPoint Registry
                       │
        ┌──────────────┼──────────────┐
        ▼              ▼              ▼
    OPC UA          Modbus            S7
    Adapter         Adapter         Adapter
```

所有 Adapter 面向统一 abstraction：

```text
IDataPoint
ICommand
IEvent
IValue
```

---

# 11. Example Device

```yaml
device:
  id: pump-001
  type: pump

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

  commands:
    start:
    stop:
```

启动：

```bash
industrial-sim run pump.yaml
```

---

# 12. Protocol Configuration

例如：

```yaml
protocols:

  opcua:
    endpoint: opc.tcp://0.0.0.0:4840

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

web:
  enabled: true
  port: 8080
```

同一个：

```text
pump.temperature
```

可以同时映射成：

```text
OPC UA Node
Modbus Register
S7 DB
MQTT Topic
```

---

# 13. Scenario Engine

Scenario 是项目的核心差异化能力之一。

MVP 阶段需要支持：

```text
at
after
every
when
set
ramp
command
wait
fault
```

例如：

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

    - after: 12s
      fault:
        type: stale
        target:
          device: pump-001
          datapoint: temperature
        duration: 10s

```

运行：

```bash
industrial-sim scenario run scenarios/pump-startup.yaml
```

---

# 14. Fault Injection

Fault 分为三类。

## 14.1 Data Fault

```text
BadQuality
Stale
Null
NaN
WrongType
OutOfRange
Frozen
Noise
Spike
Drift
```

示例：

```yaml
fault:
  type: stale
  target:
    device: pump-001
    datapoint: temperature
  duration: 30s
```

---

## 14.2 Device Fault

```text
MotorOverheat
BearingFailure
SensorFailure
LowPressure
Overload
EmergencyStop
PowerLoss
```

示例：

```yaml
- at: 40s
  fault:
    device: pump-001
    type: overheat
```

---

## 14.3 Network Fault

```text
Disconnect
Timeout
Latency
PacketLoss
ConnectionReset
SlowResponse
Reconnect
```

示例：

```yaml
- at: 60s
  fault:
    type: network.timeout
    duration: 10s
```

---

# 15. Deterministic Simulation

这是项目应该尽早支持的核心能力。

避免直接依赖：

```text
DateTime.Now
Random()
Task.Delay()
```

而应该提供：

```bash
industrial-sim run scenario.yaml \
  --clock deterministic \
  --seed 123
```

相同：

```text
Scenario
+
Seed
+
Initial State
```

应该得到相同结果。

目标：

```text
Simulation
    ↓
Deterministic Result
    ↓
Automated Test
```

这会让项目从：

> Simulator

升级为：

> Integration Test Runtime

---

# 16. CI / Integration Testing

一个重要使用场景：

```text
GitHub Actions
       │
       ▼
Docker Compose
       │
       ├── Industrial Simulator
       │
       └── Application Under Test
                │
                ▼
          Integration Tests
```

例如：

```text
Start Pump
     ↓
Temperature increases
     ↓
Temperature > 90°C
     ↓
Alarm becomes true
     ↓
SCADA detects alarm
     ↓
PASS
```

这应该成为项目长期的重要定位。

---

# 17. Protocol Roadmap

不应该第一天支持所有协议。

推荐：

```text
v0.1
  │
  ├── OPC UA
  └── Modbus TCP
       │
v0.2
  │
  └── Runtime Testability
       │
v0.3
  │
  ├── MQTT
  └── S7
       │
v0.4
  │
  ├── BACnet
  └── EtherNet/IP
       │
v1.0
  │
  ├── AAS / AID integration
  ├── PROFINET
  └── EtherCAT
```

---

# 18. Why OPC UA + Modbus First?

## Modbus

优点：

- 简单
- Register-oriented
- 容易理解
- 容易实现
- 非常适合作为第一个 Adapter

模型：

```text
Coil
Discrete Input
Input Register
Holding Register
```

---

## OPC UA

优点：

- Object-oriented
- Information Model
- Variable
- Method
- Event
- DataType
- 与 Device Model 思维高度匹配

模型：

```text
Pump
├── Temperature
├── Pressure
├── Speed
├── Running
└── Start()
```

---

## Learning Value

两者结合可以直接展示：

```text
Same Device
      │
      ├── OPC UA
      │      Object / Variable / Method
      │
      └── Modbus
             Register / Coil
```

非常适合 Industrial Protocol Playground。

---

# 19. Protocol Complexity

| Protocol | Main Model | Complexity | Priority |
|---|---|---:|---:|
| Modbus TCP | Registers / Coils | Low | **P0** |
| OPC UA | Objects / Variables / Methods | Medium | **P0** |
| MQTT | Topics / Messages | Low | P1 |
| S7 | PLC Memory / DB | Medium | P1 |
| BACnet | Objects / Properties | Medium | P2 |
| EtherNet/IP | CIP / Tags | High | P2 |
| PROFINET | Real-time IO | Very High | P3 |
| EtherCAT | Real-time / Motion | Very High | P3 |

---

# 20. MVP Definition

## v0.1 — First Public Release

目标：

> **证明核心架构成立。**

### Core

- [ ] Device
- [ ] DataPoint
- [ ] DataType
- [ ] State
- [ ] Command
- [ ] Event

### Runtime

- [ ] SimulationEngine
- [ ] SimulationClock
- [ ] Scheduler
- [ ] StateStore

### Configuration

- [ ] YAML
- [ ] JSON optional

### Devices

- [ ] Pump
- [ ] Sensor
- [ ] Motor

### Protocols

- [ ] OPC UA
- [ ] Modbus TCP

### Scenario

- [ ] at
- [ ] after
- [ ] every
- [ ] when
- [ ] set
- [ ] ramp
- [ ] command
- [ ] wait

### Fault Injection

- [ ] Data faults
- [ ] Device faults
- [ ] Network faults
- [ ] Scheduled / active / recovered lifecycle

### Web UI

- [ ] Device and DataPoint state view
- [ ] Scenario load and execution
- [ ] Fault activation and recovery
- [ ] Runtime events and logs

### Infrastructure

- [ ] .NET
- [ ] CLI
- [ ] Docker
- [ ] Developer Web UI
- [ ] Basic logging

---

# 21. v0.1 Success Criteria

必须能够完成：

```text
1. Define Pump in YAML
        ↓
2. Start simulator
        ↓
3. OPC UA server starts
        ↓
4. Modbus server starts
        ↓
5. Read same Temperature
   through both protocols
        ↓
6. Run scenario
        ↓
7. Temperature changes
        ↓
8. Fault is injected and recovered
        ↓
9. Web UI and clients observe the same state
```

如果这个闭环成立：

> v0.1 成功。

---

# 22. v0.2 — Runtime Testability

加入：

- [ ] State Machine extensions
- [ ] Seeded replay
- [ ] Snapshot / Restore
- [ ] REST API
- [ ] Integration Test Helpers
- [ ] CI examples

重点 Demo：

```text
Pump
 ↓
Startup
 ↓
Temperature rises
 ↓
Overheat
 ↓
Alarm
 ↓
Network timeout
 ↓
Recovery
```

---

# 23. v0.3 — More Protocols

加入：

- [ ] MQTT
- [ ] S7

目标：

```text
industrial-sim
        ↓
Integration Testing
        ↓
CI
```

---

# 24. v0.4 — Industrial Protocol Expansion

加入：

- [ ] BACnet
- [ ] EtherNet/IP

重点不是“协议数量”，而是证明：

```text
Same Device Model
        ↓
Multiple Protocols
```

---

# 26. v1.0

目标：

> 成为一个稳定的 Industrial Device Simulation Runtime。

考虑：

- [ ] Plugin architecture
- [ ] Device package system
- [ ] Scenario package system
- [ ] AAS integration
- [ ] AID integration
- [ ] Metrics
- [ ] Prometheus
- [ ] OpenTelemetry
- [ ] Documentation
- [ ] SDK
- [ ] Community device repository

---

# 27. Recommended .NET Architecture

建议：

```text
src/
│
├── IndustrialSim.Core
│
├── IndustrialSim.Runtime
│
├── IndustrialSim.Scenarios
│
├── IndustrialSim.Faults
│
├── IndustrialSim.Protocols
│   ├── OpcUa
│   └── Modbus
│
├── IndustrialSim.Devices
│   ├── Pump
│   ├── Motor
│   └── Sensor
│
├── IndustrialSim.Configuration
│
├── IndustrialSim.Cli
│
└── IndustrialSim.Api
```

测试：

```text
tests/
│
├── Core.Tests
├── Runtime.Tests
├── Scenario.Tests
├── Fault.Tests
├── OpcUa.Tests
├── Modbus.Tests
└── Integration.Tests
```

---

# 28. Important Architecture Rules

## Rule 1

Protocol Adapter 不允许依赖具体 Device。

```text
Bad:

ModbusAdapter → Pump
```

应该：

```text
Good:

ModbusAdapter
      ↓
IDataPoint
      ↓
Device Runtime
```

---

## Rule 2

Device 不应该知道协议。

```text
Bad:

Pump.ModbusRegister
Pump.OpcUaNode
Pump.S7Address
```

应该：

```text
Pump
  ↓
DataPoint

Protocol Adapter
  ↓
Mapping
```

---

## Rule 3

Scenario 不应该直接操作 Protocol。

错误：

```text
Scenario
  ↓
Modbus Register 100 = 95
```

正确：

```text
Scenario
  ↓
pump.temperature = 95
  ↓
Device Runtime
  ↓
Protocol Adapters
```

---

## Rule 4

Fault 应该作用在正确的层级。

```text
Data Fault
    ↓
DataPoint

Device Fault
    ↓
Device State / Behavior

Network Fault
    ↓
Protocol Adapter / Transport
```

---

# 29. Device Model vs OPC UA / AAS

不要重新发明工业标准。

推荐关系：

```text
                    Standards
                       │
        ┌──────────────┼──────────────┐
        ▼              ▼              ▼
     OPC UA          AAS             AID
        │              │              │
        └──────────────┼──────────────┘
                       │
                 Reference Model
                       │
                       ▼
             Executable Device Model
                       │
                       ▼
              Simulation Runtime
```

项目的 Device Model 重点解决：

```text
Behavior
State
Simulation
Scenario
Fault
Testing
```

而不是重新定义：

```text
Industrial Asset Standard
```

---

# 30. Long-term AAS Integration

未来可以支持：

```text
AAS
 ↓
Submodel
 ↓
AID
 ↓
Interface Mapping
 ↓
Protocol Adapter
 ↓
Simulator
```

例如：

```text
AAS Pump
   │
   ├── Identification
   ├── TechnicalData
   └── Interfaces
          │
          ├── OPC UA
          └── Modbus
```

Simulator 可以根据这些描述生成：

```text
Virtual Pump
```

这将成为 v1.x 之后的重要方向。

---

# 31. What NOT To Build

项目早期明确禁止范围膨胀。

## 不做

### 3D Factory

不要：

- Unity
- Godot
- 3D rendering
- Physics engine

至少 v1.0 前不做。

---

### Full PLC Runtime

不要自己实现：

- Siemens PLC execution
- Ladder Logic runtime
- IEC 61131 compiler

可以连接已有 PLC simulator。

---

### Full Digital Twin Platform

不要做：

- Asset lifecycle management
- Enterprise digital twin
- ERP integration
- MES
- Full AAS platform

---

### Real-time Fieldbus

不要早期实现：

- PROFINET RT
- EtherCAT
- Motion Control

---

# 32. Competitive Positioning

项目与主要竞争者的区别：

```text
ProtoForge
    ↓
Multi-protocol Simulator

Open Industry Project
    ↓
3D Factory / PLC Simulation

Factory I/O
    ↓
Virtual Factory / PLC Training

realvirtual
    ↓
Digital Twin / Virtual Commissioning

UA Edge Translator
    ↓
Industrial Connectivity

AAS / AID
    ↓
Asset / Interface Description

Our Project
    ↓
Executable Device Simulation
+
Scenario
+
Fault
+
Deterministic Integration Testing
```

---

# 33. Core Differentiator

最终应该形成：

> **Executable Industrial Device Model**

即：

```text
Device
+
State
+
Behavior
+
Command
+
Event
+
Scenario
+
Fault
+
Protocol Binding
```

这是项目最核心的技术概念。

---

# 34. Key Use Cases

## Use Case 1 — Learn Industrial Protocols

```text
Create Pump
        ↓
Expose OPC UA
        ↓
Inspect Objects
        ↓
Expose Modbus
        ↓
Inspect Registers
```

---

## Use Case 2 — Develop Industrial Gateway

```text
Gateway
   ↓
OPC UA Client
   ↓
Virtual Pump
```

无需真实硬件。

---

## Use Case 3 — Integration Testing

```text
Application
    ↓
Virtual Device
    ↓
Scenario
    ↓
Fault
    ↓
Verify Recovery
```

---

## Use Case 4 — CI

```text
Pull Request
      ↓
Docker
      ↓
Simulator
      ↓
Integration Test
      ↓
PASS / FAIL
```

---

# 35. First Demo

README 首页应该展示一个完整的 5 分钟 Demo。

### Step 1

```bash
docker run industrial-sim pump.yaml
```

### Step 2

输出：

```text
✓ Pump-001 started

OPC UA
opc.tcp://localhost:4840

Modbus TCP
localhost:5020
```

### Step 3

读取：

```text
Pump.Temperature = 25°C
```

### Step 4

运行：

```bash
industrial-sim scenario run examples/scenarios/overheating.yaml
```

### Step 5

输出：

```text
00:00 Pump started
00:10 Temperature 42°C
00:20 Temperature 61°C
00:30 Temperature 83°C
00:35 Temperature 91°C
00:35 ⚠ OVERHEAT
00:40 ⚠ DEVICE FAULT
```

### Step 6

验证：

```text
OPC UA Client
     ↓
Temperature > 90
     ↓
Alarm = true
```

这个 Demo 应该成为项目最核心的展示。

---

# 36. Repository Structure

建议：

```text
industrial-device-sim/
│
├── src/
├── tests/
├── devices/
│   ├── pump.yaml
│   ├── motor.yaml
│   └── sensor.yaml
│
├── scenarios/
│   ├── normal-startup.yaml
│   ├── overheating.yaml
│   ├── sensor-failure.yaml
│   └── network-timeout.yaml
│
├── examples/
│   ├── opcua/
│   ├── modbus/
│   └── integration-test/
│
├── docs/
│   ├── architecture.md
│   ├── device-model.md
│   ├── scenarios.md
│   ├── protocols.md
│   └── fault-injection.md
│
├── docker/
├── .github/
│   └── workflows/
│
├── Dockerfile
├── docker-compose.yml
├── README.md
└── LICENSE
```

---

# 37. Open-source Strategy

## License

推荐：

> **Apache-2.0**

原因：

- 对商业用户友好
- 对工业企业友好
- 对 Protocol Adapter contributors 友好
- 比 GPL 更容易被企业采用

如果未来某些协议 Adapter 有特殊 license，可以独立处理。

---

# 38. Contributor Model

未来可以让社区贡献：

### Devices

```text
Pump
Valve
Motor
Compressor
Boiler
EnergyMeter
FlowMeter
TemperatureSensor
```

### Protocols

```text
OPC UA
Modbus
S7
MQTT
BACnet
EtherNet/IP
```

### Scenarios

```text
Startup
Shutdown
Overheat
PowerFailure
SensorFailure
NetworkFailure
```

这样项目天然具有扩展性。

---

# 39. Project Success Metrics

不要把：

> GitHub Stars

作为唯一指标。

第一阶段：

### Technical

- [ ] One Device exposed by two protocols
- [ ] Scenario works
- [ ] Fault works
- [ ] Deterministic replay works
- [ ] Docker works
- [ ] Integration test works

### Community

目标：

```text
v0.1
10 users

v0.2
first external contributor

v0.3
first external Device / Scenario

v1.0
real-world integration usage
```

GitHub stars 是结果，而不是产品目标。

---

# 40. Risks

## Risk 1 — Protocol Scope Explosion

危险：

```text
OPC UA
Modbus
S7
MQTT
BACnet
EIP
PROFINET
EtherCAT
...
```

解决：

> Plugin architecture + strict roadmap.

---

## Risk 2 — Becoming Another Simulator

解决：

始终强调：

```text
Device Model
+
Scenario
+
Fault
+
Testing
```

而不是：

```text
Protocol Count
```

---

## Risk 3 — Device Physics Complexity

不要尝试实现真实物理模型。

第一阶段使用：

```text
Rule-based behavior
State machine
Simple equations
Generators
```

例如：

```text
temperature += heatRate
temperature -= coolingRate
```

已经足够。

---

## Risk 4 — Standard Complexity

AAS / OPC UA 都非常庞大。

不要一开始实现完整标准。

采用：

```text
Core Model
+
Protocol Mapping
+
Optional Standard Integration
```

---

# 41. First 9 Weeks Plan

## Week 1

### Architecture

- [ ] Repository
- [ ] .NET solution
- [ ] Core interfaces
- [ ] Device model
- [ ] DataPoint
- [ ] SimulationClock

---

## Week 2

### Runtime

- [ ] SimulationEngine
- [ ] Scheduler
- [ ] StateStore
- [ ] Basic generators

---

## Week 3

### YAML

- [ ] Device configuration
- [ ] DataPoint configuration
- [ ] Device loading

---

## Week 4

### OPC UA

- [ ] OPC UA Server
- [ ] Device mapping
- [ ] DataPoint mapping
- [ ] Read / Write
- [ ] Basic commands

---

## Week 5

### Modbus

- [ ] TCP server
- [ ] Register mapping
- [ ] Coil mapping
- [ ] Read / Write

---

## Week 6

### Scenario

- [ ] at
- [ ] after
- [ ] every
- [ ] when
- [ ] set
- [ ] ramp
- [ ] command
- [ ] wait

---

## Week 7

### Fault Injection

- [ ] Data faults
- [ ] Device faults
- [ ] Network faults

---

## Week 8

### Web UI

- [ ] Device state view
- [ ] Scenario controls
- [ ] Fault controls
- [ ] Runtime logs and events

---

## Week 9

### Release

- [ ] Docker
- [ ] Integration tests
- [ ] README
- [ ] Examples
- [ ] GitHub Actions
- [ ] v0.1 release

---

# 42. Definition of Done for v0.1

当以下命令能够成功：

```bash
industrial-sim run examples/pump.yaml
```

并且：

```text
OPC UA      ✓
Modbus TCP  ✓
Scenario    ✓
Faults      ✓
Web UI      ✓
Docker      ✓
```

然后：

```bash
industrial-sim scenario run examples/scenarios/overheating.yaml
```

能够产生：

```text
Temperature ↑
Alarm       ↑
Fault       ✓
```

同时一个独立的测试客户端能够验证：

```text
temperature > 90
alarm == true
```

那么：

> **v0.1 可以发布。**

不要继续堆功能。

---

# 43. Final Recommendation

## Verdict

# **RECOMMEND**

但不是：

> Build another OPC UA Simulator.

也不是：

> Build another Multi-protocol Simulator.

而是：

> **Build an executable industrial device simulation runtime.**

---

# 44. The Core Thesis

项目真正的价值可以概括为：

```text
                    DEFINE
                      │
                      ▼
                Industrial Device
                      │
                      ▼
              Executable Model
                      │
          ┌───────────┼───────────┐
          ▼           ▼           ▼
       Behavior     Scenario     Fault
          │           │           │
          └───────────┼───────────┘
                      ▼
               Simulation Engine
                      │
          ┌───────────┼───────────┐
          ▼           ▼           ▼
        OPC UA      Modbus        S7
          │           │           │
          └───────────┼───────────┘
                      ▼
                Integration Test
                      │
                      ▼
                    CI/CD
```

---

# 45. Final Answer to the Original Question

> 如果我是一个有 C#/.NET 背景、想通过这个项目学习工业协议，同时希望做一个真正有机会获得 GitHub 用户的开源项目，我现在是否应该开始做？

## 是。

但应该严格控制项目边界。

第一阶段只做：

```text
.NET
+
CLI
+
YAML
+
Pump
+
OPC UA
+
Modbus TCP
+
Scenario
+
Fault
+
Docker
```

不要做：

```text
3D
Unity
Godot
Digital Twin Platform
PLC Runtime
PROFINET
EtherCAT
完整 AAS Platform
```

---

# 46. One-Sentence Project Definition

最终可以把整个项目定义成：

> **An open-source, developer-first runtime for defining, simulating, exposing, and testing virtual industrial devices across multiple industrial protocols.**

或者更简洁：

> **Define Once. Simulate Anywhere.**

核心不是：

```text
How many protocols can we support?
```

而是：

```text
How realistically and reproducibly can we simulate
an industrial device and expose it through different protocols?
```

这应该成为整个项目从架构到 README、Issue、Roadmap 的核心原则。
