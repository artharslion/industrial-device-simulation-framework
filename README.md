# Industrial Device Simulation Framework

Developer-first .NET runtime for defining, simulating, exposing, testing, and intentionally failing virtual industrial devices.

The v0.1 MVP provides one runtime-owned `StateStore`, YAML device configuration, deterministic and real-time execution, Scenario and Fault engines, interoperable OPC UA and Modbus TCP servers, a CLI, and a developer Web console.

## Quick start

Requirements:

- .NET SDK 10.0
- Docker Desktop only for the container workflow

Validate the canonical Pump configuration:

```powershell
dotnet run --project src/IndustrialSim.Cli -- validate examples/devices/pump.yaml
```

Run it until `Ctrl+C`:

```powershell
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml
```

Run deterministic time with an explicit seed and advance 12 seconds immediately:

```powershell
dotnet run --project src/IndustrialSim.Cli -- run examples/devices/pump.yaml --deterministic --seed 123 --duration 12
```

Execute a Scenario against the same YAML-composed runtime:

```powershell
dotnet run --project src/IndustrialSim.Cli -- scenario run examples/scenarios/startup.yaml --config examples/devices/pump.yaml --deterministic --duration 12
```

Exercise the documented Device and Network Fault scenarios deterministically:

```powershell
dotnet run --project src/IndustrialSim.Cli -- scenario run examples/scenarios/overheating.yaml --config examples/devices/pump.yaml --deterministic --duration 31
dotnet run --project src/IndustrialSim.Cli -- scenario run examples/scenarios/network-timeout.yaml --config examples/devices/pump.yaml --deterministic --duration 71
```

Data Fault YAML targets a datapoint explicitly, while Network Fault YAML
targets a protocol boundary:

```yaml
fault:
  type: stale
  target: { device: pump-001, datapoint: temperature }
  duration: 10s
```

```yaml
fault:
  type: network.timeout
  protocol: opcua
  duration: 10s
```

## Developer Web console

Set the device configuration and run the Web host:

```powershell
$env:INDUSTRIALSIM_DEVICE_CONFIG = "$PWD/examples/devices/pump.yaml"
dotnet run --project src/IndustrialSim.Web --urls http://localhost:8080
```

Open [http://localhost:8080](http://localhost:8080). The console provides:

- live `StateStore` inspection
- start, resume, pause, stop, reset, and deterministic tick controls
- Scenario YAML run/stop controls
- Data, Device, and Network Fault activation/recovery
- runtime, adapter, Scenario, and Fault lifecycle status
- ordered state and fault events

The JSON API is available under `/api/state`, `/api/runtime`, `/api/protocols`, `/api/scenario`, `/api/fault`, `/api/faults`, and `/api/events`.

## Protocol endpoints

The canonical Pump configuration exposes:

- OPC UA: `opc.tcp://localhost:4840`
- Modbus TCP: `localhost:5020`
- Web console/API: `http://localhost:8080`

Protocol adapters do not own state. OPC UA variables/methods and Modbus coils/registers read and write the same runtime `StateStore` used by Scenario, Fault, CLI, and Web operations.

Modbus mappings explicitly select `coil`, `discreteInput`, `inputRegister`, `holdingRegister`, or legacy `register`. Numeric mappings support `int8`, `uint8`, `int16`, `uint16`, `int32`, `uint32`, `int64`, `uint64`, `float`/`float32`, and `double`, with optional `byteOrder` and `wordOrder` values of `big` or `little`.

## YAML shape

```yaml
device:
  id: pump-001
  type: pump
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

See [examples/devices/pump.yaml](examples/devices/pump.yaml) and [examples/scenarios](examples/scenarios).

## Docker

```powershell
docker compose up --build
```

The Compose service mounts `examples/devices/pump.yaml` read-only and publishes ports `4840`, `5020`, and `8080`.

To build without Compose:

```powershell
docker build -t industrial-sim:local .
docker run --rm -p 4840:4840 -p 5020:5020 -p 8080:8080 industrial-sim:local
```

## Verification

```powershell
dotnet restore IndustrialSim.sln
dotnet build IndustrialSim.sln --configuration Release
dotnet test IndustrialSim.sln --configuration Release --no-build
docker build -t industrial-sim:local .
docker compose config
```

The integration suite starts real OPC UA, Modbus TCP, and HTTP clients against one runtime and covers cross-protocol state, Scenario execution, and Data/Device/Network Fault activation and recovery.

## v0.1 boundaries

The MVP intentionally excludes authentication, multi-user management, PLC programming, 3D visualization, AAS implementation, enterprise asset management, and protocols beyond OPC UA and Modbus TCP.
