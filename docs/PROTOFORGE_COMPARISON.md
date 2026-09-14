# Industrial Device Simulation Framework 与 ProtoForge 对比分析

> 调研日期：2026-09-14
> IndustrialSim 基线：`5e51b2e`
> ProtoForge 公共仓库基线：[`14b4e35`](https://github.com/suoten/ProtoForge/commit/14b4e3535a4d1787b88f7a11714e70e91e2bc821)，2026-04-24

## 1. 结论

IndustrialSim 已经不再处于“核心产品闭环尚未完成”的阶段。v0.1、Wave 1、
Wave 2，以及设备启动和目录恢复闭环均已完成。当前更准确的判断是：

> IndustrialSim 在协议无关的统一状态、确定性执行、跨协议一致性、一等故障模型和运行时隔离型可观测性上更强；ProtoForge 在协议数量、现成模板、用户测试平台和外部集成上更完整。

ProtoForge 是用户可见能力 baseline，不是源码或内部架构模板。IndustrialSim
不应通过复制协议所有权、实时循环或平台数据模型来追求表面功能数量。

## 2. 调研方法和证据边界

本次检查使用 ProtoForge 公共仓库的最新 `main` 提交。该提交与 2026-09-01
首次对比时相同，因此 ProtoForge 一侧没有新的公开变化，差距变化来自
IndustrialSim 后续交付。

IndustrialSim 的当前验证结果：

- GitHub Actions CI run
  [`34801843690`](https://github.com/artharslion/industrial-device-simulation-framework/actions/runs/34801843690)
  在 `9d480d2` 上通过，包含 `Application and client checks` 与
  `Container build and smoke` 两个成功 job；
- 该 CI run 执行了 178 个 .NET 测试、28 个 Vue/Vitest 测试、Vue production
  build、`docker compose config`、Docker image build，并通过
  `/api/runtime` 容器冒烟；
- `WebApplicationFactoryTests` 通过 TestServer 执行正式 `Program`
  composition root，覆盖 `/api/v1/devices`、runtime state、OpenAPI、security
  headers 和 RFC Problem Details；
- Docker Hub manual-dispatch run
  [`34802093256`](https://github.com/artharslion/industrial-device-simulation-framework/actions/runs/34802093256)
  成功发布 `industrialdevicesimulation/industrial-device-simulation-framework:ci-smoke`；
- 发布镜像 digest 为
  `sha256:3b90a83c8631e7e39828a47b26cd996c095c650a69a45bc648fada4ec113b790`，
  OCI revision 指向 `9d480d2`；本地复验确认未提供 SQLite 连接字符串时，
  容器以 `uid=1654(app)` 运行并创建 `/app/data/industrial-sim.db`，`/` 和
  `/api/runtime` 均可访问；
- OPC UA、Modbus TCP 存在真实客户端及跨协议共享状态测试。
- Wave 3.1 本地验收在 `5e51b2e` 后通过 207 个 .NET 测试、28 个 Vue/Vitest
  测试和 Vue production build；`RuntimeEventLogTests`、`RuntimeIsolationTests`、
  `HealthEndpointTests`、`PrometheusMetricsTests`、`TraceCorrelationTests` 和
  secret-redaction 测试覆盖有界事件、drop、health、metrics、trace correlation
  和 simulation isolation；
- source host 与本地新构建的 `industrial-sim:wave31` 镜像都对
  `/health/live`、`/health/ready`、`/metrics` 返回 HTTP 200。容器未传 SQLite
  连接字符串，以 UID 1654 创建默认数据库，并暴露七个约定的 metric family。

ProtoForge 的结论来自 README、项目结构和静态源码检查。本机没有安装
`pytest`，因此没有执行其测试套件。ProtoForge README 中“15 种协议”和
“49 个模板”是公开产品声明及源码目录证据，不等同于对所有协议完成外部
互操作或完整规范符合性验证。

## 3. 产品定位

| 维度 | IndustrialSim | ProtoForge |
|---|---|---|
| 核心定位 | 确定性工业设备仿真运行时和开发测试框架 | 开箱即用的 IoT 协议仿真与测试平台 |
| 状态模型 | 一个逻辑设备由一个 `StateStore` 拥有 | 设备实例通常选择一个协议，协议服务参与数据同步 |
| 时间模型 | 可注入实时/确定性时钟和 seed | 主要使用实时后台循环和数据生成器 |
| 协议策略 | 少量协议、明确映射、真实客户端验证 | 广泛协议覆盖和快速设备创建 |
| 故障策略 | Data、Device、Network Fault 是一等运行时概念 | 异常主要通过规则、测试和协议行为表达 |
| 用户入口 | YAML、CLI、REST、Vue、Docker | REST、Vue、模板、SDK、Docker demo |

## 4. 已经追平或领先的能力

| 能力 | 当前判断 | 主要证据 |
|---|---|---|
| 确定性运行时 | IndustrialSim 领先 | 可注入时钟、seed、显式 tick 和确定性 Scenario 测试 |
| 统一状态所有权 | IndustrialSim 领先 | Core 不包含协议地址；所有协议观察同一 `StateStore` |
| 跨协议一致性 | IndustrialSim 领先 | 同一设备同时通过 OPC UA、Modbus TCP、HTTP 和 Web 观察 |
| 故障模型 | IndustrialSim 领先 | Data、Device、Network Fault 生命周期和隔离测试 |
| 多设备生命周期 | 已追平 | `SimulationRegistry`、批量操作、目录恢复和失败隔离 |
| 持久化控制面 | 已追平 | SQLite 保存定义、模板、场景、设置、用户和版本化启动文档 |
| Web 控制台 | 核心工作流已追平 | Overview、Devices、Templates、Scenarios、Protocols、Events、Users、Settings |
| 模板建模 | 架构领先、内容落后 | 不可变版本；逻辑定义与协议映射 profile 分离 |
| 场景建模 | 已追平并具有确定性优势 | YAML 导入导出、图形编辑、可复用目标、确定性运行 |
| 认证与 API | 已追平 | 可选 Identity/RBAC、`/api/v1`、OpenAPI、Problem Details |
| Docker 和快速开始 | 基础已追平 | Dockerfile、Compose、持久卷、Pump 双协议示例和操作文档 |
| 运行时可观测性 | 已追平并强调隔离 | 有界结构化事件、drop accounting、health、Prometheus、OpenTelemetry correlation 和阻塞观察者隔离测试 |

## 5. 仍然存在的差距

### 5.1 发布证据和自动化门禁

Wave 2.5 Release Evidence Gate 已关闭。仓库现在包含基于 GitHub-hosted
`ubuntu-latest` runner 的 CI、正式 composition root 的服务器集成测试、Docker
build/smoke，以及只在 tag 或手动 dispatch 时读取 Docker Hub secrets 的发布流程。

当前真实发布证据是手动 `ci-smoke` tag，而不是正式语义版本 release tag。后续正式
版本仍应通过 `vX.Y.Z` tag 路径发布并保留对应 release record；这不影响 Wave 2.5
对 CI、容器和显式手动发布能力的验收。

### 5.2 用户测试平台

ProtoForge 已公开提供用例、套件、断言、报告和 quick test。IndustrialSim
目前只有仓库开发测试，没有面向使用者的测试领域、持久化 API 或报告 UI。

IndustrialSim 不应复制通用 HTTP 测试器；应利用确定性时钟、跨协议统一状态和
Fault，提供工业仿真专用的可重复测试与诊断。

### 5.3 可观测性边界

Wave 3.1 Observability Gate 已关闭。IndustrialSim 现在提供进程内有界结构化事件
保留与过滤、非阻塞 subscriber、runtime-log/SignalR drop 计数、独立 liveness 和
readiness、七个低基数 Prometheus metric family，以及带 secret redaction 的
OpenTelemetry control-operation correlation。source host 和本地 Docker image 的三个
观测端点均已实际返回 HTTP 200。

这不代表持久化日志平台、分布式 collector 可用性保证，或对第三方库内部日志的全面
重写。OTLP exporter 是可选 batch exporter，collector 不可用不参与 readiness，也
不得阻塞 simulation tick。SQLite 仍不保存连续 live state 或事件流。

### 5.4 外部集成和录制回放

Webhook、HTTP/文件/InfluxDB forwarding、语义录制回放和 typed .NET SDK 尚未
实现。这些能力应通过有界异步队列接入，外部目标故障不得阻塞 simulation tick
或改变 `StateStore` 所有权。

### 5.5 协议与模板内容广度

IndustrialSim 当前验证 OPC UA 和 Modbus TCP；ProtoForge 公开声明 15 种协议。
IndustrialSim 也没有与 ProtoForge 49 个内置模板等量的 curated catalog。

协议数量不是近期发布门槛。新增协议必须先有 capability manifest、明确支持子集、
地址/数据类型契约、Network Fault 行为和真实客户端互操作证据。模板应优先交付
少量带行为、场景、故障和映射测试的高质量 starter pack。

## 6. 当前优先级

1. **Wave 3.2/3.3 User Testing**：用例、套件、断言、报告和可解释 quick test。
2. **Starter Template Pack**：先交付 8–12 个经过映射与行为验证的模板，不建设
   marketplace。
3. **Wave 3 Integrations**：Webhook、forwarding、语义录制回放、typed SDK。
4. **Wave 4 Protocols**：按需求和互操作证据分批增加协议。

## 7. 不应改变的架构决策

1. Core 不依赖 OPC UA、Modbus、Web 或 transport code。
2. `StateStore` 是运行时状态唯一事实来源。
3. Scenario 操作逻辑设备、datapoint、command 和 fault，不操作协议地址。
4. SQLite 只保存控制面定义和显式快照，不保存连续 live state。
5. 外部集成、日志、录制和 Network Fault 不得自动停止设备仿真。
6. 协议支持声明必须由测试或明确的互操作记录支撑。

## 8. 最终判断

如果按“功能数量和现成内容”衡量，ProtoForge 仍然领先。如果按“同一设备能否
确定性地跨协议运行、故障、验证和恢复”衡量，IndustrialSim 已形成更清晰且更难
替代的核心价值。

下一阶段不应回到横向堆协议。Wave 3.1 已关闭；是否进入 Wave 3.2 User Testing
应由单独决策确认，再围绕确定性仿真构建用户测试能力。
