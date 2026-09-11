# 服务启动指南

本文档只介绍如何启动 Industrial Device Simulation Framework（工业设备仿真框架）服务。服务启动后，请继续阅读[用户手册](USER_MANUAL.zh-CN.md)，按照推荐顺序完成第一次设备仿真。

[English Startup Guide](STARTUP_GUIDE.md)

## 1. 选择启动方式

| 使用场景 | 推荐方式 |
| --- | --- |
| 第一次体验或作为集成测试依赖 | Docker Compose |
| 开发框架或调试 .NET 代码 | 从源码启动 |
| 只验证 YAML 或执行一个场景，不使用 Web 控制台 | CLI |

## 2. 环境要求

Docker 方式：

- Docker Desktop
- 空闲的主机端口 `4840`、`5020` 和 `8080`

源码方式：

- .NET SDK 10.0
- Node.js 和 npm，因为 Web 项目会构建 Vue 客户端

确认 SDK 版本：

```powershell
dotnet --version
```

## 3. 使用 Docker Compose 启动

在仓库根目录执行：

```powershell
docker compose up --build
```

等待 Web 主机输出正在监听的日志，然后打开 [http://localhost:8080](http://localhost:8080)。

默认 Compose 服务会：

- 加载 `examples/devices/pump.yaml`；
- 在 `4840` 发布 OPC UA；
- 在 `5020` 发布 Modbus TCP；
- 在 `8080` 发布 Web 控制台和 API；
- 将 SQLite 数据保存在 `industrial-sim-data` 命名卷；
- 默认关闭身份认证，为本地开发提供直接访问权限。

按 `Ctrl+C` 停止前台进程，然后删除容器和网络：

```powershell
docker compose down
```

该命令会保留数据库卷。只有确定要清空持久化数据时，才执行：

```powershell
docker compose down -v
```

## 4. 从源码启动

### 4.1 验证启动设备配置

```powershell
dotnet run --project src/IndustrialSim.Cli -- validate examples/devices/pump.yaml
```

验证成功会输出 `Configuration valid.`。配置无效时会返回非零退出码和可操作的错误信息。

仓库中的 Pump、Motor 和 Sensor YAML 都会显式声明 `device.behavior.profile`。运行时启动前，验证器会检查 profile 是否与设备类型、必需数据点名称/类型/访问模式、必需命令以及数值参数限制一致。对于只通过显式操作改变状态的自定义设备，请使用 `profile: none`。

### 4.2 启动 Web 服务

设置启动 YAML 路径并运行主机：

```powershell
$env:INDUSTRIALSIM_DEVICE_CONFIG = "$PWD/examples/devices/pump.yaml"
dotnet run --project src/IndustrialSim.Web --urls http://localhost:8080
```

打开 [http://localhost:8080](http://localhost:8080)。在 ASP.NET Development 环境之外，必须设置 `INDUSTRIALSIM_DEVICE_CONFIG`。

### 4.3 只启动 CLI 运行时

按实时模式运行，直到按下 `Ctrl+C`：

```powershell
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml
```

按实际时间运行固定秒数：

```powershell
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml --duration 30
```

以确定性模式立即推进 30 秒：

```powershell
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml --clock deterministic --seed 123 --duration 30
```

`--deterministic` 是 `--clock deterministic` 的简写。

### 4.4 不通过 Web 控制台运行场景

```powershell
dotnet run --project src/IndustrialSim.Cli -- scenario run examples/scenarios/startup.yaml --config examples/devices/pump.yaml --deterministic --duration 12
```

如果不提供 `--duration`，CLI 会持续运行直到被取消。确定性持续时间必须足够长，才能到达需要执行的所有计划步骤。

`examples/scenarios/startup.yaml` 声明了 `scenario.target.type: pump`，动作中不再包含具体设备 ID。CLI 会将其绑定到 `--config` 加载的设备；Web 控制台则允许用户选择兼容的运行目标。带有动作级 device ID 的旧场景仍然兼容。

## 5. 默认端点

示例 Pump 使用：

| 接口 | 端点 |
| --- | --- |
| Web 控制台和 API | `http://localhost:8080` |
| OpenAPI 文档 | `http://localhost:8080/openapi/v1.json` |
| OPC UA | `opc.tcp://localhost:4840` |
| Modbus TCP | `localhost:5020` |

## 6. 配置覆盖规则

主机配置优先级为：命令行选项、环境变量、YAML、内置默认值。

| 用途 | CLI 选项 | 环境变量 |
| --- | --- | --- |
| 启动设备 | — | `INDUSTRIALSIM_DEVICE_CONFIG` |
| OPC UA 端点 | `--opcua-endpoint` | `INDUSTRIALSIM_OPCUA_ENDPOINT` |
| Modbus 端口 | `--modbus-port` | `INDUSTRIALSIM_MODBUS_PORT` |
| Web 端口 | `--web-port` | `INDUSTRIALSIM_WEB_PORT` |
| 日志级别 | `--log-level` | `INDUSTRIALSIM_LOG_LEVEL` |

示例：

```powershell
$env:INDUSTRIALSIM_MODBUS_PORT = "15020"
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml
```

## 7. 身份认证

身份认证默认为 `Disabled`，适用于受信任的本地开发环境。

启动 Web 主机前启用持久化本地身份认证：

```powershell
$env:Auth__Mode = "LocalIdentity"
$env:INDUSTRIALSIM_DEVICE_CONFIG = "$PWD/examples/devices/pump.yaml"
dotnet run --project src/IndustrialSim.Web --urls http://localhost:8080
```

使用 Compose 时，在服务环境变量中增加：

```yaml
environment:
  Auth__Mode: LocalIdentity
```

服务启动后，打开 **Users** 并创建第一个管理员。密码至少需要 12 个字符，并包含大写字母、小写字母、数字和非字母数字字符。

## 8. SQLite 持久化

源码运行时默认连接为：

```text
Data Source=industrial-sim.db
```

可以通过以下环境变量覆盖：

```powershell
$env:ConnectionStrings__IndustrialSim = "Data Source=C:\industrial-sim-data\industrial-sim.db"
```

Docker Compose 将 `/app/data/industrial-sim.db` 保存在命名卷中。模板、场景定义、设置、用户和目录记录会持久化；服务运行期间的实时状态仍由内存中的 `StateStore` 管理。

## 9. 启动问题排查

### 提示必须设置 `INDUSTRIALSIM_DEVICE_CONFIG`

将变量设置为实际存在的设备 YAML 文件。回退设备仅在 ASP.NET Development 环境中可用。

### 端口已被占用

停止占用端口的进程，或覆盖 OPC UA、Modbus、Web 端口。同一 Web 主机中的多个设备必须预留不同的协议端口。

### 容器已经启动，但浏览器无法访问

运行 `docker compose ps`，确认已经发布 `8080` 端口，然后查看 `docker compose logs industrial-sim`。

### 协议客户端无法连接

确认对应适配器已经启用、预期端口已经发布，并且当前没有激活 Network Fault。从另一个 Compose 容器连接时，应使用 Compose 服务名而不是 `localhost`。

### 数据点在没有对应场景步骤时仍然变化

检查设备实际生效的 behavior profile。Pump 和 Motor 会在仿真 tick 中更新转速、温度、压力/电流、运行状态和 alarm 等派生状态；Sensor 会在 quality 为 `Good` 时更新 value。如果场景需要完全控制状态，请调整行为参数或使用 `profile: none`。

### 使用空白 Docker 数据重新启动

以下操作会删除 SQLite 命名卷：

```powershell
docker compose down -v
docker compose up --build
```

## 10. 验证源码工作区

```powershell
dotnet restore IndustrialSim.sln
dotnet build IndustrialSim.sln --configuration Release
dotnet test IndustrialSim.sln --configuration Release --no-build
docker compose config
```

服务可用后，请继续阅读[用户手册：完成第一次设备仿真](USER_MANUAL.zh-CN.md)。
