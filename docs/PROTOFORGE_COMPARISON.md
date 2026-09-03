# Industrial Device Simulation Framework 与 ProtoForge 对比分析

> **状态更新（2026-09-02）**：本文最初记录的是 IndustrialSim v0.1
> 完成前的对比快照。v0.1 MVP 现已作为完成里程碑接受。下一阶段以
> ProtoForge 的用户可见功能作为产品 baseline，最新目标架构与执行任务见：
> `docs/plans/2026-09-02-protoforge-baseline-design.md` 和
> `docs/plans/2026-09-02-protoforge-baseline-implementation-plan.md`。
> 本文中关于“当前尚未完成 v0.1”的描述仅保留为历史分析，不再代表当前计划状态。

## 1. 文档目的

本文从产品功能和设计架构两个层面对比：

- 当前仓库：Industrial Device Simulation Framework（下文简称 **IndustrialSim**）
- 对比仓库：`D:\Repos\ProtoForge`（下文简称 **ProtoForge**）

IndustrialSim 目前仍处于 v0.1 开发阶段。因此，本文不把所有尚未完成的能力都视为代码缺陷，而是区分为：

1. v0.1 规范已经要求、但仍需完成的 MVP 缺口；
2. ProtoForge 已具备、可供后续借鉴的产品化能力；
3. 与 IndustrialSim 定位不符、当前不应引入的范围。

分析基线：

- IndustrialSim：提交 `6967c7a`，规范以 `docs/PROJECT_SPEC.md` 为准；
- ProtoForge：提交 `14b4e35`；
- 分析日期：2026-09-01；
- IndustrialSim 的 71 个 .NET 测试全部通过；
- ProtoForge 的本地环境缺少 `pytest`，本次对其结论主要来自 README、目录结构和静态源码检查，不等同于对全部协议能力的互操作验证。

## 2. 核心结论

IndustrialSim 和 ProtoForge 并不是同一种产品的两个等价实现。

**IndustrialSim 更像一个“确定性工业设备仿真运行时”。** 它以设备模型和统一状态为中心，强调协议无关、场景驱动、故障注入、可重复执行和自动化测试。它当前的优势主要在架构方向和领域边界，而不足主要是端到端产品闭环尚未完成。

**ProtoForge 更像一个“开箱即用的多协议仿真与测试平台”。** 它以 Web 控制台、协议服务、模板、API、持久化和集成功能为中心，功能面更宽、可见度更高、用户上手路径更完整，但其设备模型更接近“某个协议下的设备实例”，确定性仿真和跨协议统一状态并不是主要设计中心。

因此，IndustrialSim 不应以“协议数量追平 ProtoForge”为近期目标。更合理的路线是：

> 先把一个设备、两个协议、一个场景和一组故障形成可信的确定性闭环，再吸收 ProtoForge 在 UI、模板、API、演示和运维体验上的优点。

## 3. 产品定位差异

| 维度 | IndustrialSim | ProtoForge |
|---|---|---|
| 核心定位 | 开发者优先的工业设备仿真框架和运行时 | 开箱即用的物联网协议仿真与测试平台 |
| 核心对象 | Device、DataPoint、Command、Event、Fault | 协议服务、设备实例、测点、模板、测试用例 |
| 首要价值 | 同一设备状态可被场景、故障和多个协议一致观察 | 快速创建多种协议设备并通过 Web/API 操作 |
| 配置入口 | YAML 和 CLI 优先 | Web UI、REST API、模板和数据库优先 |
| 协议策略 | v0.1 聚焦 OPC UA 与 Modbus TCP | 横向覆盖多种工业和物联网协议 |
| 仿真策略 | 可注入时间、确定性执行、可复现测试 | 基于后台循环和数据生成器的实时运行 |
| 故障策略 | Data / Device / Network Fault 是一等运行时概念 | 更侧重协议仿真、规则联动和测试诊断 |
| 目标使用方式 | CI、集成测试、本地开发、故障验证 | 图形化创建、演示、协议联调、平台化测试 |

这意味着 ProtoForge 的“功能更多”不能直接推出 IndustrialSim 的“架构落后”。两者的重点不同：ProtoForge 已经形成平台外壳，IndustrialSim 则在构建更严格的仿真内核。

## 4. 架构对比

### 4.1 IndustrialSim 的目标架构

```text
YAML / CLI / Web API
          |
          v
Application Host / Composition Layer
          |
          v
Simulation Runtime
  - SimulationClock
  - SimulationEngine
  - StateStore
  - Scenario Engine
  - Fault Manager
          |
          v
Device Model and Behavior
          |
          +--------------------+
          |                    |
          v                    v
    OPC UA Adapter       Modbus Adapter
```

关键设计特征：

- `StateStore` 是运行时状态的唯一事实来源；
- 设备定义、设备运行状态和协议映射相互分离；
- 场景和设备行为操作逻辑设备，不操作协议地址；
- 协议适配器只暴露运行时，不拥有业务行为；
- 仿真时间和随机性应可注入，以支持可重复测试；
- Network Fault 停止或干扰协议边界，但不自动停止设备仿真。

这一结构更适合验证“OPC UA 与 Modbus 是否观察到同一个设备状态”，也更适合构造可重复的故障和场景测试。

### 4.2 ProtoForge 的现有架构

```text
Vue Web UI / REST API / SDK
            |
            v
FastAPI Application
  - Auth
  - Database
  - Templates
  - Test Runner
  - Logs / Metrics / Webhooks
            |
            v
Simulation Engine
  - Device Registry
  - Scenario Registry
  - Protocol Server Registry
            |
            v
Protocol-specific Servers and Behaviors
```

关键设计特征：

- FastAPI 是产品控制面的中心；
- 每个设备配置直接关联一个协议；
- 协议服务拥有各自的设备行为和协议侧数据；
- SQLite 支撑设备、场景、模板、测试和用户数据的恢复；
- Vue 控制台、REST API、WebSocket、模板和演示模式构成完整用户入口；
- 扩展方向以增加协议、模板、测试与集成为主。

这一结构能更快形成“创建即使用”的平台体验，但不天然保证一个逻辑设备同时通过多个协议共享完全一致的状态，也不以确定性时间推进为核心。

### 4.3 架构差异的影响

| 架构问题 | IndustrialSim | ProtoForge | 对 IndustrialSim 的启示 |
|---|---|---|---|
| 状态所有权 | 设计上集中在 `StateStore` | 设备实例与协议行为均可能保存值 | 保持统一状态，不复制协议侧业务状态 |
| 设备与协议关系 | 一个设备可映射到多个协议 | 设备配置通常绑定一个协议 | 不采用“设备属于协议”的模型 |
| 时间模型 | 确定性时钟和实时钟可替换 | 主要依赖实时循环 | 继续把确定性作为差异化能力 |
| 控制面 | 目前较薄 | API、UI、数据库和 SDK 完整 | 需要补 Application Host 和控制面，但不要侵入 Core |
| 扩展方式 | 领域、场景、故障、协议分层扩展 | 注册更多协议服务和模板 | 优先完善纵向闭环，再横向扩协议 |
| 运维形态 | 仍在形成 | Docker、设置、日志、指标较完整 | 借鉴运行体验和可观测性，不照搬业务范围 |

## 5. 功能对比

“当前状态”描述的是本次检查时可见的成熟度，不代表最终设计目标。

| 功能域 | IndustrialSim 当前状态 | ProtoForge 当前状态 | 判断 |
|---|---|---|---|
| 领域模型 | 已有类型化 Device、DataPoint、Command、Event 基础 | 有设备、测点和协议配置模型 | IndustrialSim 的协议无关边界更清晰 |
| 统一运行时状态 | 已有线程安全 `StateStore` 和状态变更事件 | 设备和协议服务均参与保存/同步值 | IndustrialSim 应保持现有方向 |
| 仿真时间 | 已有确定性时钟和实时钟基础 | 主要使用实时后台循环 | IndustrialSim 设计更适合 CI，但运行闭环待完成 |
| 设备行为 | Pump 有基础行为；Motor、Sensor 尚未完成 | 通过生成器和各协议行为驱动多种模板 | IndustrialSim 需完成 v0.1 三类内置设备 |
| YAML 配置 | 设备和协议配置已具备基础加载与校验 | 主要依赖 JSON 模板、API 和数据库 | IndustrialSim 更符合“配置即仿真”的目标 |
| Scenario | 已有解析、调度和基础动作模型 | 有场景 CRUD、规则联动和可视化编辑 | IndustrialSim 需要完成从配置到运行时的完整执行链 |
| Fault | 已有 Data、Device、Network 分类和生命周期基础 | 不是产品主轴，更多通过规则和测试能力覆盖异常 | IndustrialSim 有差异化潜力，但需端到端接通 |
| OPC UA | 已有标准服务器和外部客户端基础验证 | 基于 `asyncua` 提供服务器 | 两者均有真实协议栈基础；IndustrialSim 仍需补齐完整契约验证 |
| Modbus TCP | 已有显式映射和基础服务器 | 基于成熟库提供服务器 | IndustrialSim 需加强线级兼容性和数据布局契约 |
| Web UI | 目前主要是后端 API，尚无可用开发者控制台 | Vue 控制台包含设备、协议、场景、测试、日志、设置等页面 | 这是当前最明显的产品体验差距 |
| REST API | 只有少量运行时、状态、场景和故障端点 | API 面较完整，并有 Swagger/ReDoc | 应补 v0.1 必需的控制 API，不必复制全部平台 API |
| 设备模板 | 只有少量 YAML 示例 | 有较丰富的内置模板市场 | v0.1 后可引入轻量模板目录 |
| 自动化测试产品 | 以仓库级单元、协议和集成测试为主 | 有面向用户的测试用例、测试套件和报告 | 两类测试用途不同；后者可作为 v0.2 借鉴项 |
| 持久化 | 非 v0.1 核心能力 | SQLite 持久化设备、场景、模板、用户和测试 | 当前不应为追平平台而引入数据库 |
| 日志与指标 | 规范要求结构化日志，当前能力仍基础 | 有日志总线、WebSocket 和 Prometheus 指标 | 应先补结构化事件流，再考虑指标系统 |
| Docker 与演示 | v0.1 要求，但仓库尚未形成可运行交付 | 有 Dockerfile、Compose 和 demo 模式 | IndustrialSim 的 v0.1 发布阻塞项 |
| 认证与多用户 | 明确属于 v0.1 非目标 | 有 JWT 和 RBAC | 不应纳入 IndustrialSim v0.1 |
| 数据转发、录制回放、Webhook | 不在 v0.1 范围 | 已提供相应平台能力 | 可进入后续路线，不应阻塞 MVP |

## 6. IndustrialSim 当前最需要补齐的部分

### 6.1 第一优先级：完成 v0.1 纵向闭环

当前最重要的不足不是协议数量或模板数量，而是规范中的关键部件尚未作为一个产品流程共同工作。

目标闭环应是：

```text
pump.yaml
   -> 配置校验
   -> 创建 Pump 与 StateStore
   -> 启动 SimulationEngine
   -> 运行 startup / overheating / network-timeout Scenario
   -> 执行 Device / Data / Network Fault
   -> OPC UA 与 Modbus TCP 同时观察统一状态
   -> Web UI 显示状态、场景、故障和事件
   -> CLI、Docker 和端到端测试可重复运行
```

在这个闭环完成前，增加更多协议、复杂模板市场或持久化会扩大表面积，却不会提高核心可信度。

### 6.2 第二优先级：建立真正的 Application Host

目前各模块已经存在，但缺少一个清晰的应用编排层来负责：

- 加载并校验完整配置；
- 创建 Device Definition、Device Behavior 和 `StateStore`；
- 选择实时或确定性时钟；
- 注册命令、场景和故障执行器；
- 按配置启动 OPC UA、Modbus 和 Web；
- 统一处理启动失败、取消、停止和资源释放；
- 向 CLI 和 Web 暴露同一个运行时实例。

建议新增明确的 Host/Orchestrator 边界，但不要把这些职责放入 Core 或协议项目。CLI、Web 和未来 Docker 入口都应复用同一个 Host。

### 6.3 第三优先级：把 Scenario 和 Fault 从“组件”变成“能力”

IndustrialSim 已经有场景动作、条件、故障分类和生命周期基础，但产品定义要求的是可观察的端到端行为：

- Scenario 中的设备、DataPoint、Command 和 Fault 引用在启动前完成校验；
- `set`、`ramp`、`command`、`wait` 与 `at`、`after`、`every`、`when` 有稳定且文档化的语义；
- Data Fault 真正影响测点读数或数据质量；
- Device Fault 真正影响设备状态、行为、命令或事件；
- Network Fault 只影响目标协议边界，设备仿真继续推进；
- 所有激活、恢复和状态变化进入统一事件流；
- 相同初始状态、场景和 seed 得到相同结果。

这是 IndustrialSim 相对 ProtoForge 最有价值的差异化能力，应优先于功能数量。

### 6.4 第四优先级：完成开发者 Web Console

ProtoForge 最值得借鉴的不是页面数量，而是它给用户提供了一条明确操作路径。IndustrialSim 的 v0.1 Web UI 应保持克制，只覆盖规范要求：

- 当前设备和 DataPoint 状态；
- 仿真 start、pause、stop、reset；
- Scenario 加载、运行和停止；
- Fault 激活和恢复；
- OPC UA、Modbus 的端点、状态和故障状态；
- 运行时事件和结构化日志。

不需要在 v0.1 引入账号、角色、模板市场、报表中心或复杂系统设置。

### 6.5 第五优先级：形成可发布、可验证的交付物

ProtoForge 已经具备源码启动、Docker 启动和 demo 模式。IndustrialSim 在 v0.1 完成前需要补齐：

- 根目录 README 和 5 分钟快速开始；
- Dockerfile、`.dockerignore` 和 `docker-compose.yml`；
- 明确并一致的 OPC UA、Modbus 和 Web 端口；
- canonical Pump 配置与场景；
- 一个真实端到端测试，证明同一状态可被两个协议和 Web 同时观察；
- 一个故障演示，证明网络故障不会停止设备仿真；
- CI 中的构建、测试和容器冒烟验证。

## 7. 应从 ProtoForge 借鉴的能力

### 7.1 v0.1 内可以借鉴

1. **明确的首次使用路径**：启动、创建设备、启动协议、查看数据、运行场景应在几分钟内完成。
2. **统一控制面**：用户不需要分别理解每个内部项目，就能查看运行状态并执行常用操作。
3. **协议状态可见性**：端点、端口、运行状态和错误原因应直接可见。
4. **实时日志体验**：运行事件、场景动作、故障生命周期和协议错误应在同一视图中检索。
5. **Demo 模式**：提供开箱即用的 Pump 双协议演示，有利于验证和传播项目价值。

### 7.2 v0.2 以后可以借鉴

1. 设备模板目录与模板实例化；
2. 更完整的 REST API 和 SDK；
3. Prometheus 指标与运行健康检查；
4. 面向用户的协议测试用例、套件和报告；
5. 配置导入导出与批量设备操作；
6. Webhook、数据转发、录制与回放。

这些能力应建立在稳定运行时契约之上，并通过独立模块接入，避免改变 Core 的协议无关性。

## 8. 当前不应照搬的部分

根据 IndustrialSim 的 v0.1 非目标和架构原则，以下 ProtoForge 能力不应成为近期追赶项：

- 15 种协议的横向扩张；
- JWT、RBAC 和多用户管理；
- SQLite 作为运行时前置依赖；
- 模板市场和复杂 CRUD 管理；
- 数据转发、协议录制回放和第三方集成中心；
- 将协议地址放入 Core Device/DataPoint 模型；
- 让每个协议适配器维护独立的设备业务状态；
- 用协议数量或页面数量代替端到端协议兼容性测试。

盲目复制这些能力会使 IndustrialSim 从“确定性设备仿真框架”滑向“通用物联网管理平台”，与当前规范冲突。

## 9. 建议的分阶段路线

### Phase 0：架构收口

- 定义 Application Host 的职责和生命周期；
- 让 CLI 与 Web 复用同一运行时组装；
- 固化 Device Definition、Runtime State、Behavior 和 Protocol Mapping 的边界；
- 为场景、故障和协议建立统一事件流；
- 明确 v0.1 配置契约和错误分类。

完成标准：一个进程能够从 YAML 创建并持续运行 Pump，所有模块使用同一个 `StateStore`。

### Phase 1：v0.1 功能闭环

- 完成 Pump、Motor、Sensor；
- 完成场景动作、触发器和引用校验；
- 接通 Data、Device、Network Fault；
- 完成 OPC UA 与 Modbus TCP 的目标契约；
- 完成开发者 Web Console；
- 完成 CLI 的 run、validate、scenario run 和确定性参数。

完成标准：`PROJECT_SPEC.md` 第 67 节 Definition of Done 全部由测试或明确的手工验证覆盖。

### Phase 2：交付与可信度

- Docker、Compose、README 和 demo；
- 外部 OPC UA 客户端兼容测试；
- 外部 Modbus TCP 客户端兼容测试；
- 跨协议、Web、场景和故障端到端测试；
- 结构化日志、健康检查和基本指标；
- 并发和长时间运行测试。

完成标准：新用户能在 5 分钟内启动 canonical demo，CI 能复现相同场景结果。

### Phase 3：选择性产品化

- 轻量设备模板目录；
- 更完整的控制 API 和 SDK；
- 测试用例与报告；
- 持久化作为可选控制面能力；
- 根据真实需求增加协议和集成。

完成标准：新增能力不破坏 Core 的协议独立性、统一状态和确定性。

## 10. 建议保留的架构决策

下面这些方向是 IndustrialSim 相比 ProtoForge 更鲜明的价值，应明确保留：

1. **Device First**：设备是核心，协议只是访问方式。
2. **Single State Authority**：所有状态变化经过 `StateStore`。
3. **Protocol Independent Core**：Core 不包含 OPC UA Node 或 Modbus Register。
4. **Deterministic by Design**：时间、随机数和调度可控制、可复现。
5. **Fault as a First-Class Concept**：故障有类型、生命周期、目标和可观察事件。
6. **Thin Protocol Adapters**：协议层不实现设备业务行为和场景逻辑。
7. **Test-Gated Claims**：协议和故障能力必须由外部兼容测试或明确手工验证支撑。

## 11. 非功能需求与风险

| 关注点 | IndustrialSim 应达到的 v0.1 基线 | 当前主要风险 |
|---|---|---|
| 确定性 | 同输入、初始状态和 seed 产生同结果 | 模块存在但尚未形成完整运行路径 |
| 并发安全 | 仿真、场景、协议和 Web 并发访问有序 | 统一状态已打基础，整体调度仍需验证 |
| 协议兼容性 | 真实客户端可连接、读写并观察更新 | OPC UA 已有基础验证，Modbus 线级覆盖仍需加强 |
| 可观测性 | 状态、动作、故障和协议错误统一记录 | 当前事件与日志体验不完整 |
| 可部署性 | CLI 和容器均可稳定启动、停止 | Docker 和正式 Host 尚未闭环 |
| 可维护性 | 模块边界清晰，配置契约有测试 | 应避免为追赶功能面而引入跨层依赖 |
| 故障隔离 | 网络故障不停止设备仿真 | 需要端到端测试证明边界成立 |

## 12. 最终判断

如果以“今天打开后能做多少事情”衡量，ProtoForge 明显更完整；它已经具备平台外壳、操作界面、模板、API、持久化和较宽的协议覆盖。

如果以“是否围绕统一设备状态构建确定性、多协议、可故障注入的仿真内核”衡量，IndustrialSim 的目标架构更集中，也更适合自动化集成测试。当前问题不是架构方向错误，而是核心模块还没有被一个可靠的 Host 组装成 v0.1 的可运行产品。

近期最有价值的工作不是复制 ProtoForge 的功能总量，而是完成并证明以下承诺：

> 一个 Pump，由一个 `StateStore` 驱动；一个 Scenario 可以确定性改变它；Data、Device、Network Fault 可以被注入和恢复；OPC UA、Modbus TCP 与 Web 能观察同一状态；CLI 和 Docker 能稳定复现全过程。

这个承诺一旦成立，IndustrialSim 就拥有与 ProtoForge 清晰不同且更难替代的核心价值。
