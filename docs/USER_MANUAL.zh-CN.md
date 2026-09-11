# 用户手册：完成第一次设备仿真

本手册假设 Web 服务已经成功运行。如果服务还没有启动，请先阅读[服务启动指南](STARTUP_GUIDE.zh-CN.md)。

[English User Manual](USER_MANUAL.md)

推荐按照以下顺序学习和使用系统：

```text
进入控制台
    → 选择或创建设备
    → 启动设备并检查状态
    → 运行场景
    → 注入故障并验证恢复
    → 通过协议客户端验证同一状态
    → 检查事件并保存可复用模型
```

## 第一步：进入控制台并确认运行环境

打开 [http://localhost:8080](http://localhost:8080)。

如果服务使用 `examples/devices/pump.yaml` 启动，**Overview** 页面应该至少显示一个已注册设备。打开 **Devices**，选择 `pump-001`。

在执行操作前，先确认：

- 设备 ID 和设备类型正确；
- 运行模式符合预期，是 Real time 或 Deterministic；
- 设备摘要中可以看到随机种子和仿真时钟；
- 设备协议信息中可以看到 OPC UA 和 Modbus；
- 当前没有非预期的活动故障。

可以通过工作区顶部的 **Theme** 选择 **System**、**Light** 或 **Dark**。主题会统一应用到 Runtime Event Stream、设备详情、模板、Protocol Mapping Profiles，以及 Scenario 的 Flow/YAML 工作区。

如果系统启用了身份认证并要求登录，请打开 **Users**。如果还没有用户，先初始化第一个管理员，然后登录。身份认证关闭时，控制台使用等效的 `local-developer` Admin 身份。

## 第二步：决定使用现有设备还是创建设备

第一次操作时，建议直接使用已经加载的 `pump-001`。它已经包含数据点、命令、OPC UA 配置和 Modbus 映射，并且可以直接配合仓库中的示例场景使用。

完成第一次操作后，再根据用途选择创建设备的方式。

### 创建一次性设备

需要临时运行时设备时，使用 **Devices → New device**，依次填写：

1. 唯一的设备 ID；
2. Device Profile：**Pump**、**Motor**、**Sensor** 或 **Custom**；
3. 如果需要可重复结果，启用确定性模式并指定随机种子；
4. 选择内置 profile 时，配置行为参数和数据点初始值；
5. 选择 Custom 时，自行定义逻辑数据点；
6. 按需添加不冲突的 OPC UA 或 Modbus 端口。

Pump、Motor 和 Sensor 会在创建前显示行为摘要、命令、事件、必需数据点和参数默认值。因为运行时行为依赖这套契约，所以必需数据点的名称、类型和访问模式会被锁定；初始值和描述仍然可以修改。

Custom 会显式创建 `none` 行为 profile，不包含周期性的内置行为。它的状态只会通过允许的写入、场景、命令、故障或其他显式运行时操作发生变化。

这种方式适合快速实验。创建结果是活动的内存仿真，不应该默认将其视为可复用模型。

### 创建可复用设备模型

需要重复使用设备定义时，使用 **Templates → New template**。先定义逻辑数据点和命令，再添加独立的 OPC UA 或 Modbus 映射配置。保存模板后，使用唯一设备 ID 和空闲端口将其实例化。

团队共享模型、重复测试或创建多个同类型设备时，应优先使用模板。

## 第三步：启动设备并理解实时状态

进入设备详情页，执行 **Start**。然后按照以下顺序检查标签页：

1. **overview**：确认运行状态、时钟模式、随机种子、场景状态和故障数量；
2. **state**：查看每个数据点的当前值；
3. **protocols**：确认需要的协议适配器正在运行；
4. **events**：查看 Start 操作产生的生命周期事件。

对于 `pump-001`，重点观察 temperature、pressure、speed、running 和 alarm。

Pump profile 包含周期性行为循环。运行时，它会让 speed 向 `ratedSpeed` 变化、根据 speed 计算 pressure、按照 `heatingRatePerSecond` 提高 temperature，并在达到 `overheatTemperature` 时触发 alarm；停止时按照 `coolingRatePerSecond` 降温。Motor 包含类似的转速、电流和温度行为，Sensor 则会在 quality 为 `Good` 时按照 `ratePerSecond` 增加 value。

因此，执行 `start` 后 Pump 的 temperature 持续变化是预期行为，即使场景只配置了 `speed`。可以在设备详情页的 **Default behavior** 中查看实际生效的 profile 和参数。

只有访问模式允许时才能写入数据点。需要观察稳定状态时使用 **Pause**，继续运行时使用 **Resume**，结束运行时使用 **Stop**，恢复初始运行状态时使用 **Reset**。

如果设备使用确定性模式，可以通过 **Tick** 显式推进仿真时间。这适合需要在不等待真实时间的情况下，到达精确仿真时刻的测试。

## 第四步：运行第一个场景

打开 **Scenarios**，导入：

```text
examples/scenarios/startup.yaml
```

运行前先查看生成的流程。该场景声明了 `target.type: pump`，因此可以在运行时选择任意兼容的 Pump。场景会执行两个逻辑操作：

1. 在仿真时间 `0s` 调用所选 Pump 的 `start` 命令；
2. 延迟一秒后，在 10 秒内将 `speed` 从 `0` 提升到 `1450`。

选择 `pump-001`，然后执行 **Run on selected device**。返回设备详情页，观察：

- `running` 变为 `true`；
- 经过足够仿真时间后，`speed` 达到 `1450`；
- temperature 和 pressure 按照 Pump 行为发生变化；
- 场景事件和状态事件按照正确顺序出现。

如果是确定性设备，并且场景还没有到达最终状态，请通过 Tick 至少推进 12 秒。

可复用场景只描述逻辑设备类型、数据点和命令，实际设备 ID 由运行时选择的目标提供。场景不能直接依赖 Modbus 寄存器或 OPC UA 节点地址。

为了兼容旧配置，仍然支持在每个动作中填写 `device: pump-001`，但这种场景会绑定到该设备。新的单设备场景应优先使用 `scenario.target.type`。

## 第五步：为自己的测试创建场景

确认启动场景正常后，在 **Scenarios** 中创建新场景。

推荐按照以下顺序构建流程：

1. 选择一个参考设备，编辑器会从中获得可复用目标类型和可用能力；
2. 确定用于建立初始条件的状态或命令；
3. 添加 `set`、`ramp` 或 `command` 动作；
4. 为动作添加明确的 `at`、`after`、`every` 或 `when` 触发器；
5. 只有后续动作确实需要延迟时才添加 `wait`；
6. 先证明正常行为，再添加故障；
7. 保存场景，然后选择一个兼容的运行目标。

可视化编辑器会根据参考设备限制表单：

- 时间使用数值和单位输入，不再要求手动输入 `1s`；
- 数据点从设备定义中选择；
- `ramp` 只显示数值类型数据点；
- Boolean 使用 `true`/`false` 下拉框，数值使用数字输入，String 使用文本输入；
- command 从设备的命令契约中选择；
- Run target 只显示与可复用目标类型匹配的设备。

每个 YAML 步骤必须且只能包含一个触发器和一个动作。典型场景如下：

```yaml
scenario:
  name: startup-and-overheat
  target:
    type: pump
  steps:
    - at: 0s
      command:
        name: start
    - after: 1s
      ramp:
        datapoint: speed
        from: 0
        to: 1450
        duration: 10s
    - after: 30s
      fault:
        type: overheat
```

导出的 YAML 持续时间可以使用 `ms`、`s`、`m`、`h` 后缀或 .NET `TimeSpan` 格式。表单会生成 `500ms`、`10s` 或 `2m` 等值；导入 YAML 时也支持 `00:00:10`。

Scenario 动作和内置行为操作同一个 `StateStore`。场景可以写入 profile 也会计算的数据点，例如 Pump 的 `speed`，但内置行为可能在后续 tick 中再次更新它。如果测试需要由场景完全控制状态，应使用 Custom/`none`，或者调整 profile 参数使其行为符合测试目标。

## 第六步：注入故障并验证恢复

应先证明设备的正常行为，再测试失败行为。根据测试目标选择正确的故障层级：

| 测试目标 | 故障作用位置 |
| --- | --- |
| 无效、陈旧、冻结或失真的数据 | 作用于数据点的 Data Fault |
| 过热、损坏等设备行为 | 作用于设备的 Device Fault |
| 客户端重试、超时或重连 | 作用于协议适配器的 Network Fault |

第一次故障测试时，导入并运行：

```text
examples/scenarios/overheating.yaml
```

将确定性时钟推进到 30 秒之后，然后确认故障出现在 **faults**，故障生命周期出现在 **events**。

接下来使用 `examples/scenarios/network-timeout.yaml` 对 OPC UA 注入超时。推进到 60 秒之后观察故障激活，再推进超过故障持续时间，观察故障恢复。

注意：Network Fault 只影响指定的协议边界，不会自动停止设备仿真或其他协议适配器。因此可以验证 OPC UA 失败时，Modbus 和设备运行时仍然继续工作。

## 第七步：通过 OPC UA 或 Modbus 验证设备

使用兼容客户端连接示例端点：

- OPC UA：`opc.tcp://localhost:4840`
- Modbus TCP：主机 `localhost`，端口 `5020`

以 Web 控制台中的状态作为易于阅读的参考，验证协议客户端观察到的是同一个逻辑状态。

使用 OPC UA 时：

1. 连接并浏览设备；
2. 找到设备变量和方法；
3. 读取与 **state** 标签页相同的数据；
4. 只有定义允许时才调用方法或写入值。

使用 Modbus 时：

1. 打开设备定义或原始 YAML；
2. 使用准确的地址区域和寄存器/线圈编号；
3. 使用配置的数据类型、字节序和字序；
4. 注意 32 位和 64 位数据会占用多个 16 位寄存器。

如果数值不一致，优先检查是否存在 Data Fault、Network Fault、只读数据点或错误的 Modbus 数据表示。

## 第八步：使用事件解释系统行为

每次完成场景或故障测试后，打开全局 **Events** 或设备的 **events** 标签页。

从测试开始的操作向后阅读，确认事件顺序符合预期：

```text
设备生命周期
    → 命令或状态转换
    → 场景动作
    → 故障激活（如果存在）
    → 故障恢复（如果配置了持续时间）
```

排查问题时，事件通常比只查看最终数值更可靠。即使最终状态正确，中间动作仍然可能以错误顺序执行。

测试失败时，按照以下顺序检查：

1. 目标设备是否正在运行？
2. 场景是否在正确的设备上启动？
3. 仿真时间是否到达触发时刻？
4. 场景引用的数据点或命令是否存在？
5. 是否已经有故障处于活动状态？
6. 协议适配器是否仍然连接？

## 第九步：保存并复用成功的工作

完整流程验证成功后：

1. 在 **Scenarios** 中保存或导出场景；
2. 将需要重复使用的设备定义创建为模板，并按需保存 behavior metadata；
3. 保持协议映射与逻辑数据点、命令相互独立；
4. 在使用该场景的测试中记录确定性随机种子和所需持续时间；
5. 实例化多个设备时使用不同端口。

模板、场景定义、设置、用户和目录记录保存在 SQLite 中。实时设备状态保存在运行时 `StateStore` 中，二者具有不同的生命周期。

模板中的 behavior metadata 使用如下 JSON：

```json
{
  "profile": "pump",
  "parameters": {
    "ratedSpeed": 1450,
    "accelerationSeconds": 10
  }
}
```

支持的 profile 为 `pump`、`motor`、`sensor` 和 `none`。profile、设备类型、数据点 schema 或命令不兼容时，会在设备注册前返回明确错误。

## 第十步：流程稳定后再配置用户

如果部署使用 `LocalIdentity`，Admin 可以在 **Users** 中创建用户并分配一个角色：

| 角色 | 适用场景 |
| --- | --- |
| Viewer | 查看设备、协议、场景和事件 |
| Operator | 运行设备、修改允许的状态、执行场景和故障操作 |
| Admin | 管理设备、模板、用户和设置 |

应为用户分配能够完成任务的最低权限。关闭身份认证的部署只应运行在受信任的本地网络中。

## 完成检查清单

满足以下条件时，表示已经完成推荐的第一次操作流程：

- `pump-001` 可以启动并显示实时状态；
- 生效的 behavior profile 和参数可以解释自动状态变化；
- startup 场景可以改变 running 状态和 speed；
- 可复用场景无需修改步骤即可在另一台兼容设备上运行；
- 过热或网络故障在预期仿真时间激活；
- 配置了持续时间的故障可以自动恢复；
- OPC UA 或 Modbus 观察到的逻辑状态与 Web 控制台一致；
- Events 能够解释生命周期、场景和故障操作的顺序；
- 可复用设备和场景已经保存为模板或目录数据。

如果需要处理服务启动、部署、环境变量、端口、认证模式、数据库路径或服务级问题，请返回[服务启动指南](STARTUP_GUIDE.zh-CN.md)。

精确 API 契约可通过 `/openapi/v1.json` 查看。规范性架构和 YAML 行为请参考 [PROJECT_SPEC.md](PROJECT_SPEC.md)。
