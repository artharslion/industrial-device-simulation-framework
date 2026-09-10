# User Manual: Operate Your First Simulation

This manual assumes that the Web service is already running. If it is not, follow the [Service Startup Guide](STARTUP_GUIDE.md) first.

[简体中文用户手册](USER_MANUAL.zh-CN.md)

The recommended learning path is:

```text
Open the console
    → select or create a device
    → start it and inspect state
    → run a scenario
    → inject and recover a fault
    → verify the same state through a protocol client
    → inspect events and save reusable models
```

## Step 1: Open the console and confirm the runtime

Open [http://localhost:8080](http://localhost:8080).

The **Overview** page should show at least one registered device when the service was started with `examples/devices/pump.yaml`. Open **Devices** and select `pump-001`.

Before changing anything, confirm:

- the device ID and type are correct;
- its runtime mode is Real time or Deterministic as expected;
- OPC UA and Modbus appear under the device protocol information;
- there is no unexpected active fault.

If authentication is enabled and the console asks for credentials, open **Users**, bootstrap the first administrator if no user exists, and sign in. With authentication disabled, the console uses the effective `local-developer` Admin identity.

## Step 2: Decide whether to use an existing device or create one

For the first walkthrough, use the loaded `pump-001`. It already contains datapoints, commands, OPC UA configuration, and Modbus mappings that work with the checked-in scenarios.

After the walkthrough, choose one of these creation paths:

### Create a one-off device

Use **Devices → New device** when you need a temporary runtime device. Enter:

1. a unique device ID and device type;
2. deterministic mode and seed if reproducibility matters;
3. at least one typed datapoint;
4. optional, unique OPC UA or Modbus ports.

This is the quickest path for an experiment. The device is an active in-memory simulation and should not be treated as a reusable model by default.

### Create a reusable device model

Use **Templates → New template** when the definition will be reused. Define the logical datapoints and commands first, then add separate OPC UA or Modbus mapping profiles. Save the template and instantiate it with a unique device ID and available ports.

Prefer templates for team-shared models, repeated tests, or multiple devices of the same type.

## Step 3: Start the device and understand its state

Open the device details page and use **Start**. Then inspect these tabs in order:

1. **overview** — confirms runtime state, clock mode, seed, scenario status, and fault count;
2. **state** — shows the current value of every datapoint;
3. **protocols** — confirms that the expected adapters are running;
4. **events** — shows the lifecycle events produced by the start operation.

For `pump-001`, observe values such as temperature, pressure, speed, running, and alarm.

Only write a datapoint when its access mode permits it. Use **Pause** when you want to inspect a stable runtime, **Resume** to continue, **Stop** to stop it, and **Reset** to return it to its initial runtime state.

If the device is deterministic, use **Tick** to advance simulation time explicitly. This is useful when a test must reach an exact simulation time without waiting for wall-clock time.

## Step 4: Run the first scenario

Open **Scenarios** and import:

```text
examples/scenarios/startup.yaml
```

Review the generated flow before running it. The scenario performs two logical operations:

1. invokes the `start` command on `pump-001` at simulation time `0s`;
2. ramps `speed` from `0` to `1450` over 10 seconds after a one-second delay.

Select `pump-001` and choose **Run on selected device**. Return to the device details page and observe:

- `running` becomes `true`;
- `speed` reaches `1450` after enough simulation time;
- temperature and pressure change according to Pump behavior;
- scenario and state events appear in order.

For a deterministic device, advance at least 12 seconds with Tick if the scenario has not reached its final state.

A scenario always targets logical device IDs, datapoints, and commands. It must never depend directly on a Modbus register or OPC UA node address.

## Step 5: Create a scenario for your own test case

Once the startup example works, create a new scenario in **Scenarios**.

Build the flow in this order:

1. identify the state or command that establishes the starting condition;
2. add `set`, `ramp`, or `command` actions;
3. add explicit `at`, `after`, `every`, or `when` triggers;
4. add `wait` only when later actions must be delayed;
5. add a fault after the normal behavior is proven;
6. save the scenario, then run it on a selected device.

Every YAML step requires exactly one trigger and one action. A typical scenario looks like:

```yaml
scenario:
  name: startup-and-overheat
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
    - after: 30s
      fault:
        type: overheat
        device: pump-001
```

Use `s`, `m`, `h`, or a .NET `TimeSpan` value for durations. Prefer explicit values such as `10s`, `2m`, or `00:00:10`.

## Step 6: Inject a fault and verify recovery

Prove normal behavior before testing failure behavior. Then choose the fault layer that matches the test:

| What you want to test | Fault target |
| --- | --- |
| Invalid, stale, frozen, or distorted data | Data Fault on a datapoint |
| Device behavior such as overheat or failure | Device Fault on the device |
| Client retry, timeout, or reconnect handling | Network Fault on a protocol adapter |

For the first fault walkthrough, import and run:

```text
examples/scenarios/overheating.yaml
```

Advance the deterministic clock past 30 seconds, then confirm the fault appears in **faults** and its lifecycle appears in **events**.

Next, use `examples/scenarios/network-timeout.yaml` to apply an OPC UA timeout. Advance past 60 seconds to activate it and past its duration to observe recovery.

Important: a Network Fault affects the selected protocol boundary. It does not automatically stop device simulation or the other protocol adapter. This lets you verify, for example, that OPC UA fails while Modbus and the device runtime continue.

## Step 7: Verify the device through OPC UA or Modbus

Connect a compatible client to the example endpoints:

- OPC UA: `opc.tcp://localhost:4840`
- Modbus TCP: host `localhost`, port `5020`

Use the Web console as the human-readable reference and verify that the protocol client observes the same logical state.

For OPC UA:

1. connect and browse the device;
2. locate its variables and methods;
3. read the same values shown on the **state** tab;
4. invoke or write only when the definition permits it.

For Modbus:

1. open the device definition or source YAML;
2. use the exact address area and register/coil number;
3. use the configured data type, byte order, and word order;
4. remember that 32-bit and 64-bit values span multiple 16-bit registers.

If values disagree, first check for an active Data Fault, an active Network Fault, a read-only datapoint, or an incorrect Modbus representation.

## Step 8: Use events to explain what happened

After every scenario or fault test, open **Events** or the device **events** tab.

Read the stream from the operation that started the test and confirm the expected order:

```text
device lifecycle
    → command or state transition
    → scenario action
    → fault activation, if any
    → fault recovery, if configured
```

Events are more reliable for diagnosis than looking only at the final value. A final state may be correct even when intermediate actions ran in the wrong order.

When a test fails, check these questions in sequence:

1. Was the intended device running?
2. Did the scenario start on the correct device?
3. Did simulation time reach the trigger?
4. Did the referenced datapoint or command exist?
5. Was a fault already active?
6. Did the protocol adapter remain connected?

## Step 9: Save and reuse successful work

Once the complete flow works:

1. save or export the scenario from **Scenarios**;
2. create a template for any device definition that will be reused;
3. keep protocol mappings separate from logical datapoints and commands;
4. record the deterministic seed and required duration in the consuming test;
5. use unique ports when instantiating multiple devices.

Templates, scenario definitions, settings, users, and catalog records are stored in SQLite. Live device state remains in the runtime `StateStore` and has a different lifecycle from catalog data.

## Step 10: Add users only when the workflow is stable

If the deployment uses `LocalIdentity`, an Admin can create users in **Users** and assign one role:

| Role | Use it for |
| --- | --- |
| Viewer | Inspecting devices, protocols, scenarios, and events |
| Operator | Running devices, changing permitted state, scenarios, and faults |
| Admin | Managing devices, templates, users, and settings |

Start with the lowest role that can complete the task. Keep authentication-disabled deployments on a trusted local network.

## Completion checklist

You have completed the recommended first workflow when you can confirm all of the following:

- `pump-001` starts and its state is visible;
- the startup scenario changes running state and speed;
- an overheat or network fault activates at the expected simulation time;
- a configured fault recovers when its duration ends;
- OPC UA or Modbus observes the same logical state as the Web console;
- Events explain the order of lifecycle, scenario, and fault operations;
- reusable devices and scenarios are saved as templates or catalog items.

For startup, deployment, environment variables, ports, authentication mode, database location, or service-level troubleshooting, return to the [Service Startup Guide](STARTUP_GUIDE.md).

For exact API contracts, use `/openapi/v1.json`. For normative architecture and YAML behavior, see [PROJECT_SPEC.md](PROJECT_SPEC.md).
