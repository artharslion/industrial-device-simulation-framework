# Industrial Device Simulation Framework

> Define once. Simulate anywhere.

Industrial Device Simulation Framework is a developer-first .NET runtime for defining, running, exposing, testing, and intentionally failing virtual industrial devices.

One logical device is owned by one runtime `StateStore` and can be observed through OPC UA, Modbus TCP, the CLI, the HTTP API, and the developer Web console. Scenarios and faults change that same state, which makes the framework useful for protocol learning, gateway development, integration tests, and deterministic CI environments.

## What you can do

- Define Pump, Motor, Sensor, or custom devices in YAML.
- Run simulations in real time or with a deterministic clock and seed.
- Expose the same logical state through OPC UA and Modbus TCP.
- Host several devices behind one shared OPC UA endpoint and one OPC UA port.
- Create and run `set`, `ramp`, `command`, `wait`, and `fault` scenario steps.
- Inject Data, Device, and Network Faults and observe their lifecycle.
- Operate devices from a multi-page Web console.
- Create reusable device templates and persistent scenario definitions.
- Optionally enable local users with Viewer, Operator, and Admin roles.
- Inspect bounded structured runtime events, health, Prometheus metrics, and
  trace-correlated control operations.

## Five-minute start with Docker

Requirements: Docker Desktop and free host ports `4840`, `5020`, and `8080`.

```powershell
docker compose up --build
```

Wait for the Web host to report that it is listening, then open [http://localhost:8080](http://localhost:8080).

The Compose stack starts the example Pump with:

| Interface | Endpoint |
| --- | --- |
| Web console and API | `http://localhost:8080` |
| OPC UA | `opc.tcp://localhost:4840` |
| Modbus TCP | `localhost:5020` |

Compatible devices configured with the same normalized shared OPC UA endpoint
reuse the listener. They appear under
`Objects/IndustrialSim/Devices/{deviceId}`; reads, writes, commands,
subscriptions, and Network Fault status remain device-scoped. The existing
YAML `protocols.opcua.endpoint` contract is unchanged. See
`examples/devices/pump-shared-opcua.yaml` and
`examples/devices/sensor-shared-opcua.yaml`.

In the console:

1. Open **Devices**, select `pump-001`, and inspect its live state.
2. Use **Start**, **Pause**, **Stop**, **Reset**, or deterministic **Tick** controls.
3. Open **Scenarios**, import `examples/scenarios/startup.yaml`, and run it on `pump-001`.
4. Return to the device details to see state, protocols, faults, and ordered events.

Stop the stack with `Ctrl+C`, then:

```powershell
docker compose down
```

The named Docker volume retains the SQLite control-plane database. Use `docker compose down -v` only when you intentionally want to delete that persisted data.

## Run from source

Requirements:

- .NET SDK 10.0
- Node.js and npm when building the Web project from source

Validate the example configuration:

```powershell
dotnet run --project src/IndustrialSim.Cli -- validate examples/devices/pump.yaml
```

Run the Pump until `Ctrl+C`:

```powershell
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml
```

Run a reproducible 12-second simulation immediately:

```powershell
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml --deterministic --seed 123 --duration 12
```

Run the Web console:

```powershell
$env:INDUSTRIALSIM_DEVICE_CONFIG = "$PWD/examples/devices/pump.yaml"
dotnet run --project src/IndustrialSim.Web --urls http://localhost:8080
```

Operational endpoints are anonymous so local tooling and container
orchestrators can inspect the process:

| Endpoint | Meaning |
| --- | --- |
| `/health/live` | Web process can execute requests; independent of SQLite, devices, protocols, and exporters |
| `/health/ready` | Control plane can query the registry, the event pump is healthy, and SQLite is reachable |
| `/metrics` | Prometheus text for bounded runtime, fault, protocol, scenario, tick, and stream-drop metrics |

Set `OpenTelemetry__Otlp__Endpoint` to an absolute HTTP or HTTPS collector
endpoint to enable batch OTLP trace export. With no endpoint configured, no
external trace exporter runs. Observability does not persist continuous live
state or events to SQLite and does not replace `StateStore` as runtime authority.

## Run a scenario

```powershell
dotnet run --project src/IndustrialSim.Cli -- scenario run examples/scenarios/startup.yaml --config examples/devices/pump.yaml --deterministic --duration 12
```

Fault examples:

```powershell
dotnet run --project src/IndustrialSim.Cli -- scenario run examples/scenarios/overheating.yaml --config examples/devices/pump.yaml --deterministic --duration 31
dotnet run --project src/IndustrialSim.Cli -- scenario run examples/scenarios/network-timeout.yaml --config examples/devices/pump.yaml --deterministic --duration 71
```

The scenario targets logical devices, datapoints, commands, and protocols—not Modbus addresses or OPC UA node identifiers:

```yaml
scenario:
  name: pump-startup
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
```

The target type makes this scenario reusable across compatible Pump devices. The concrete device is selected when the scenario runs; legacy action-level `device` fields remain supported.

## Configuration at a glance

```yaml
device:
  id: pump-001
  type: pump
  behavior:
    profile: pump
    parameters:
      ratedSpeed: 1450
      accelerationSeconds: 10
      heatingRatePerSecond: 0.5
  datapoints:
    speed:
      type: int32
      initial: 0
      access: readwrite

protocols:
  opcua:
    enabled: true
    endpoint: "opc.tcp://0.0.0.0:4840"
  modbus:
    enabled: true
    port: 5020
    mappings:
      speed:
        holdingRegister: 104
        type: int32
        access: readwrite

web:
  enabled: true
  port: 8080
```

Built-in profiles are `pump`, `motor`, and `sensor`. Use `profile: none` for a custom device with no periodic behavior. The Web console exposes the same server-owned profiles, parameter defaults, required datapoints, commands, and events during device creation.

Start with [examples/devices/pump.yaml](examples/devices/pump.yaml), [examples/devices/motor.yaml](examples/devices/motor.yaml), or [examples/devices/sensor.yaml](examples/devices/sensor.yaml).

## Documentation

- [Service Startup Guide](docs/STARTUP_GUIDE.md) / [中文启动指南](docs/STARTUP_GUIDE.zh-CN.md) — Docker, source startup, ports, environment variables, authentication mode, persistence, and startup troubleshooting.
- [User Manual](docs/USER_MANUAL.md) / [中文用户手册](docs/USER_MANUAL.zh-CN.md) — the recommended workflow from selecting a device through scenarios, faults, protocol verification, events, and reusable models.
- [Technical Specification](docs/PROJECT_SPEC.md) — normative behavior, architecture, and scope.
- [Implementation Notes](docs/IMPLEMENTATION_NOTES.md) — implementation-level context and limitations.
- [AI Development Guide](docs/AI_DEVELOPMENT_GUIDE.md) — repository workflow for AI-assisted changes.

## Configuration overrides

Precedence is command-line option, environment variable, YAML, then built-in default.

| Purpose | CLI | Environment |
| --- | --- | --- |
| OPC UA endpoint | `--opcua-endpoint` | `INDUSTRIALSIM_OPCUA_ENDPOINT` |
| Modbus port | `--modbus-port` | `INDUSTRIALSIM_MODBUS_PORT` |
| Web port | `--web-port` | `INDUSTRIALSIM_WEB_PORT` |
| Log level | `--log-level` | `INDUSTRIALSIM_LOG_LEVEL` |

The Web host also uses `INDUSTRIALSIM_DEVICE_CONFIG`, `ConnectionStrings__IndustrialSim`, and `Auth__Mode`.

## Verify the repository

```powershell
dotnet restore IndustrialSim.sln
dotnet build IndustrialSim.sln --configuration Release
dotnet test IndustrialSim.sln --configuration Release --no-build
docker compose config
```

The test suite covers the shared runtime state, deterministic scenarios, fault activation and recovery, real OPC UA and Modbus clients, HTTP contracts, persistence, authentication, the Web console, bounded runtime events, health, Prometheus metrics, trace correlation, secret redaction, and observation-path isolation.

## Continuous integration and Docker releases

The public repository uses GitHub Actions with standard Ubuntu runners:

- `.github/workflows/ci.yml` runs the .NET and Vue checks, validates Compose,
  builds the Docker image, starts it, and verifies the HTTP runtime API.
- `.github/workflows/docker-publish.yml` publishes release images to Docker Hub
  for `v*` tags or an explicit manual workflow dispatch.

Configure these repository Actions secrets before publishing:

| Secret | Value |
| --- | --- |
| `DOCKERHUB_USERNAME` | Docker Hub account or organization name |
| `DOCKERHUB_TOKEN` | Docker Hub access token with permission to push images |

Create a public Docker Hub repository named
`industrial-device-simulation-framework` under that account. A stable tag such
as `v0.3.0` publishes `v0.3.0`, `0.3.0`, `0.3`, `0`, and `latest`. Prerelease
tags do not move `latest`. Manual runs publish the requested tag, or
`manual-<short-sha>` when no tag is provided, and never move `latest`.

## Scope

The accepted v0.1 runtime includes YAML devices, Pump/Motor/Sensor models, deterministic simulation, scenarios, faults, OPC UA, Modbus TCP, CLI, Docker, and the developer Web UI. Post-v0.1 platform work adds the versioned control API, persistent catalogs, visual modeling workflows, and optional local identity while preserving the original runtime boundaries.

The project is not a PLC runtime, 3D factory simulator, AAS implementation, enterprise asset-management system, or general-purpose factory management platform.
