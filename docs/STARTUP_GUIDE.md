# Service Startup Guide

This guide explains how to start the Industrial Device Simulation Framework service. After the service is running, continue with the [User Manual](USER_MANUAL.md) for the recommended operating workflow.

[简体中文启动指南](STARTUP_GUIDE.zh-CN.md)

## 1. Choose how to run the service

| Situation | Recommended method |
| --- | --- |
| First evaluation or integration-test dependency | Docker Compose |
| Framework development or .NET debugging | Run from source |
| Validate YAML or execute one scenario without the Web console | CLI |

## 2. Prerequisites

Docker workflow:

- Docker Desktop
- Free host ports `4840`, `5020`, and `8080`

Source workflow:

- .NET SDK 10.0
- Node.js and npm because the Web project builds the Vue client

Confirm the SDK:

```powershell
dotnet --version
```

## 3. Start with Docker Compose

From the repository root:

```powershell
docker compose up --build
```

Wait until the Web host reports that it is listening, then open [http://localhost:8080](http://localhost:8080).

The default Compose service:

- loads `examples/devices/pump.yaml`;
- publishes OPC UA on `4840`;
- publishes Modbus TCP on `5020`;
- publishes the Web console and API on `8080`;
- stores SQLite data in the `industrial-sim-data` named volume;
- runs with authentication disabled for local developer access.

Stop the foreground process with `Ctrl+C`, then remove the container and network:

```powershell
docker compose down
```

This preserves the database volume. Delete the persisted database only when you intentionally want a clean environment:

```powershell
docker compose down -v
```

## 4. Start from source

### 4.1 Validate the startup device

```powershell
dotnet run --project src/IndustrialSim.Cli -- validate examples/devices/pump.yaml
```

Success prints `Configuration valid.`. Invalid configuration returns a non-zero exit code with an actionable error.

The checked-in Pump, Motor, and Sensor YAML files declare an explicit `device.behavior.profile`. Validation checks that the profile matches the device type, required datapoint names/types/access modes, required commands, and numeric parameter limits before the runtime starts. Use `profile: none` for a custom device that should change only through explicit operations.

### 4.2 Start the Web service

Set the startup YAML path and run the host:

```powershell
$env:INDUSTRIALSIM_DEVICE_CONFIG = "$PWD/examples/devices/pump.yaml"
dotnet run --project src/IndustrialSim.Web --urls http://localhost:8080
```

Open [http://localhost:8080](http://localhost:8080). Outside the ASP.NET Development environment, `INDUSTRIALSIM_DEVICE_CONFIG` is required.

### 4.3 Start the CLI-only runtime

Run in real time until `Ctrl+C`:

```powershell
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml
```

Run for a fixed wall-clock duration:

```powershell
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml --duration 30
```

Run deterministically and advance 30 seconds immediately:

```powershell
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml --clock deterministic --seed 123 --duration 30
```

`--deterministic` is shorthand for `--clock deterministic`.

### 4.4 Run a scenario without the Web console

```powershell
dotnet run --project src/IndustrialSim.Cli -- scenario run examples/scenarios/startup.yaml --config examples/devices/pump.yaml --deterministic --duration 12
```

Without `--duration`, the CLI continues until cancelled. A deterministic duration must be long enough to reach every scheduled step you want to execute.

`examples/scenarios/startup.yaml` declares `scenario.target.type: pump` and omits concrete device IDs from its actions. The CLI binds it to the device loaded from `--config`; the Web console lets the operator choose a compatible run target. Legacy scenarios with action-level device IDs remain supported.

## 5. Endpoints

The example Pump uses:

| Interface | Endpoint |
| --- | --- |
| Web console and API | `http://localhost:8080` |
| OpenAPI document | `http://localhost:8080/openapi/v1.json` |
| OPC UA | `opc.tcp://localhost:4840` |
| Modbus TCP | `localhost:5020` |

## 6. Configuration overrides

Host configuration precedence is command-line option, environment variable, YAML, then built-in default.

| Purpose | CLI option | Environment variable |
| --- | --- | --- |
| Startup device | — | `INDUSTRIALSIM_DEVICE_CONFIG` |
| OPC UA endpoint | `--opcua-endpoint` | `INDUSTRIALSIM_OPCUA_ENDPOINT` |
| Modbus port | `--modbus-port` | `INDUSTRIALSIM_MODBUS_PORT` |
| Web port | `--web-port` | `INDUSTRIALSIM_WEB_PORT` |
| Log level | `--log-level` | `INDUSTRIALSIM_LOG_LEVEL` |

## Shared OPC UA endpoints

Devices in the same Web process may configure the same normalized shared OPC
UA endpoint, for example `opc.tcp://0.0.0.0:4840`. The first running device
opens the listener, later devices join `Objects/IndustrialSim/Devices`, and the
last running device releases the port. One published Docker port is sufficient
for all compatible members. Different OPC UA hosts or paths on the same port,
and OPC UA/Modbus/Web collisions, are rejected.

Example:

```powershell
$env:INDUSTRIALSIM_MODBUS_PORT = "15020"
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml
```

## 7. Authentication

Authentication defaults to `Disabled`, which is intended for a trusted local development environment.

Enable persistent local identity before starting the Web host:

```powershell
$env:Auth__Mode = "LocalIdentity"
$env:INDUSTRIALSIM_DEVICE_CONFIG = "$PWD/examples/devices/pump.yaml"
dotnet run --project src/IndustrialSim.Web --urls http://localhost:8080
```

For Compose, add the setting to the service environment:

```yaml
environment:
  Auth__Mode: LocalIdentity
```

After startup, open **Users** and create the first administrator. The password must be at least 12 characters and include uppercase, lowercase, numeric, and non-alphanumeric characters.

## 8. SQLite persistence

The default source-run connection is:

```text
Data Source=industrial-sim.db
```

Override it with:

```powershell
$env:ConnectionStrings__IndustrialSim = "Data Source=C:\industrial-sim-data\industrial-sim.db"
```

Docker Compose stores `/app/data/industrial-sim.db` in a named volume. Templates, scenario definitions, settings, users, and catalog records are persistent. Live runtime state continues to be owned by the in-memory `StateStore` while the service is running.

## 9. Startup troubleshooting

### `INDUSTRIALSIM_DEVICE_CONFIG` is required

Set the variable to an existing device YAML file. The fallback device is available only in the ASP.NET Development environment.

### A port is already in use

Stop the external process using the port or override the OPC UA, Modbus, or Web port. Multiple devices may share only an identical normalized OPC UA endpoint; other listener conflicts remain invalid.

### The container starts but the browser cannot connect

Run `docker compose ps`, confirm that port `8080` is published, and inspect `docker compose logs industrial-sim`.

### A protocol client cannot connect

Confirm that the configured adapter is enabled, the expected port is published, and no Network Fault is active. From another Compose container, use the Compose service name instead of `localhost`.

### A datapoint changes without a scenario step

Inspect the device's effective behavior profile. Pump and Motor update derived speed, temperature, pressure/current, running, and alarm state on simulation ticks; Sensor updates its value while quality is `Good`. Configure the behavior parameters or use `profile: none` when the scenario must have full ownership of state.

### Start again with an empty Docker database

This operation deletes the named SQLite volume:

```powershell
docker compose down -v
docker compose up --build
```

## 10. Verify a source checkout

```powershell
dotnet restore IndustrialSim.sln
dotnet build IndustrialSim.sln --configuration Release
dotnet test IndustrialSim.sln --configuration Release --no-build
docker compose config
```

Once the service is available, continue with [User Manual: Operate Your First Simulation](USER_MANUAL.md).
