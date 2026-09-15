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
- its seed and simulation clock are visible in the device summary;
- OPC UA and Modbus appear under the device protocol information;
- there is no unexpected active fault.

Use the **Theme** selector in the workspace header to choose **System**, **Light**, or **Dark**. The selection applies to the runtime event stream, device details, templates, protocol mapping profiles, and the Scenario flow/YAML workspace.

If authentication is enabled and the console asks for credentials, open **Users**, bootstrap the first administrator if no user exists, and sign in. With authentication disabled, the console uses the effective `local-developer` Admin identity.

## Step 2: Decide whether to use an existing device or create one

For the first walkthrough, use the loaded `pump-001`. It already contains datapoints, commands, OPC UA configuration, and Modbus mappings that work with the checked-in scenarios.

After the walkthrough, choose one of these creation paths:

### Create a one-off device

Use **Devices → New device** when you need a temporary runtime device. Enter:

1. a unique device ID;
2. a Device Profile: **Pump**, **Motor**, **Sensor**, or **Custom**;
3. deterministic mode and seed if reproducibility matters;
4. behavior parameters and initial values when a built-in profile is selected;
5. logical datapoints for a Custom device;
6. whether the device should expose OPC UA or Modbus TCP;
7. an available protocol port and, for Modbus, an explicit mapping for every datapoint that should be exposed.

Pump, Motor, and Sensor profiles show their behavior summary, commands, events, required datapoints, and parameter defaults before creation. Their required datapoint names, types, and access modes are locked because the runtime behavior depends on that contract; initial values and descriptions remain editable.

Custom creates an explicit `none` behavior profile. It has no periodic built-in behavior: its state changes only through permitted writes, scenarios, commands, faults, or other explicit runtime operations.

Protocol selection is explicit:

- If neither protocol is enabled, the device is created without a network adapter. Merely creating the device does not expose it to an OPC UA or Modbus client.
- Enabling OPC UA registers the device with a real server when the device starts. Compatible devices using the same normalized shared OPC UA endpoint reuse one listener and appear under `Objects/IndustrialSim/Devices/{deviceId}`. Unless a template mapping profile overrides it, datapoints use `${deviceId}/${datapoint}` NodeIds and commands use `${deviceId}/${command}` method NodeIds.
- Enabling Modbus requires mappings. For each exposed datapoint, the Quick Create editor captures the address area, zero-based address, and wire datatype, and applies its defined access/byte-order/word-order defaults. The API and persisted launch definition carry all of those mapping fields explicitly. The console rejects an enabled Modbus port with no mapping instead of treating the port as a configured protocol.
- Modbus and incompatible listener ports must be unique across registered devices. Identical normalized OPC UA endpoints may share one reserved listener owner; custom NodeIds must still be unique within that endpoint.

This is the quickest path for an experiment. Its launch definition is saved in the SQLite device catalog, while its live datapoint values remain in its in-memory `StateStore`. Use a template when the model itself should be reusable.

### Create a reusable device model

Use **Templates → New template** when the definition will be reused. Define the logical datapoints and commands first, then add separate OPC UA or Modbus mapping profiles. Save the template and instantiate it with a unique device ID and available ports.

When instantiating a template:

- select the exact mapping profile for each protocol that needs one;
- Modbus always requires a selected mapping profile;
- OPC UA may use either a selected profile or the default NodeId rule;
- the resolved mappings are copied into the device launch definition, so the instantiated device remains restorable even if the source template is later removed.

Prefer templates for team-shared models, repeated tests, or multiple devices of the same type.

## Step 3: Start the device and understand its state

Open the device details page and use **Start runtime / resume**. Runtime
lifecycle controls start the host, clock, and configured protocol adapters;
they do not implicitly execute a logical device command with the same name.
For a Pump, use the separate **Device commands → start** control to set
`running=true` and activate its behavior. Then inspect these tabs in order:

1. **overview** — confirms runtime state, clock mode, seed, scenario status, and fault count;
2. **state** — shows the current value of every datapoint;
3. **protocols** — confirms that the expected adapters are running;
4. **events** — shows the lifecycle events produced by the start operation.

For `pump-001`, observe values such as temperature, pressure, speed, running, and alarm.

The Pump profile has a periodic behavior loop. While running, it moves speed toward `ratedSpeed`, derives pressure from speed, increases temperature by `heatingRatePerSecond`, and raises an alarm at `overheatTemperature`. While stopped, it cools by `coolingRatePerSecond`. Motor has a similar speed/current/temperature loop, while Sensor increases its value by `ratePerSecond` while quality is `Good`.

This means a Pump temperature changing after its device command `start` is
expected behavior, even if a scenario only mentions `speed`. Starting only the
runtime leaves the Pump stopped. Inspect **Default behavior** on the device
details page to see the effective profile and parameter values.

Only write a datapoint when its access mode permits it. Use **Pause** when you want to inspect a stable runtime, **Resume** to continue, **Stop** to stop it, and **Reset** to return it to its initial runtime state.

If the device is deterministic, use **Advance 1s** after the device command to
advance simulation time explicitly. This is useful when a test must reach an
exact simulation time without waiting for wall-clock time. Real-time devices
advance through the existing background behavior loop after the command.

## Step 4: Run the first scenario

Open **Scenarios** and import:

```text
examples/scenarios/startup.yaml
```

Review the generated flow before running it. The scenario declares `target.type: pump`, so it can be run on any compatible Pump selected at run time. It performs two logical operations:

1. invokes the `start` command on the selected Pump at simulation time `0s`;
2. ramps `speed` from `0` to `1450` over 10 seconds after a one-second delay.

Select `pump-001` and choose **Run on selected device**. Return to the device details page and observe:

- `running` becomes `true`;
- `speed` reaches `1450` after enough simulation time;
- temperature and pressure change according to Pump behavior;
- scenario and state events appear in order.

For a deterministic device, advance at least 12 seconds with Tick if the scenario has not reached its final state.

A reusable scenario targets a logical device type, datapoints, and commands. The selected run target supplies the concrete device ID. It must never depend directly on a Modbus register or OPC UA node address.

Legacy YAML that puts `device: pump-001` in every action is still accepted, but it is bound to that exact device. Prefer `scenario.target.type` for new single-device scenarios.

## Step 5: Create a scenario for your own test case

Once the startup example works, create a new scenario in **Scenarios**.

Build the flow in this order:

1. choose a reference device; the editor derives the reusable target type and available capabilities from it;
2. identify the state or command that establishes the starting condition;
3. add `set`, `ramp`, or `command` actions;
4. add explicit `at`, `after`, `every`, or `when` triggers;
5. add `wait` only when later actions must be delayed;
6. add a fault after the normal behavior is proven;
7. save the scenario, then choose a compatible run target.

The visual editor constrains fields using the reference device:

- time fields use a numeric amount and unit instead of requiring text such as `1s`;
- datapoints are selected from the device definition;
- `ramp` only offers numeric datapoints;
- Boolean values use `true`/`false`, numeric values use number inputs, and strings remain text;
- commands are selected from the device's command contract;
- the run target list only shows devices matching the reusable target type.

Every YAML step requires exactly one trigger and one action. A typical scenario looks like:

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

Exported YAML uses `ms`, `s`, `m`, `h`, or a .NET `TimeSpan` value for durations. The form generates values such as `500ms`, `10s`, or `2m`; imported YAML may also use `00:00:10`.

Scenario actions and built-in behavior operate on the same `StateStore`. A scenario can intentionally write a datapoint also calculated by the selected profile, such as Pump `speed`; the behavior loop may update it again on later ticks. Use Custom/`none` when the test requires full scenario ownership of state, or configure the profile so its behavior matches the test.

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

Important: a Network Fault affects the selected protocol boundary. It does not automatically stop device simulation or the other protocol adapter. On a shared OPC UA endpoint, disconnect and timeout return device-scoped bad status and suppress only that device's notifications; other devices and the listener remain available. Recovery publishes the target device's latest runtime state.

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

1. open the device details, selected template mapping profile, or source YAML;
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
2. create a template for any device definition that will be reused, including its behavior metadata when required;
3. keep protocol mappings separate from logical datapoints and commands;
4. record the deterministic seed and required duration in the consuming test;
5. use unique ports when instantiating multiple devices.

Templates, scenario definitions, settings, users, and device launch records are stored in SQLite. A launch record includes the logical definition, simulation options, enabled adapters, resolved mappings, source-template provenance, and desired lifecycle state. Live datapoint values, active faults, simulation time, and continuously changing runtime state remain in each device's `StateStore` and are not written to SQLite as a live datapoint database.

### Understand restart and desired-state behavior

When the Web service restarts, it loads the configured YAML boot device first and then restores each saved catalog device independently. A malformed device definition, invalid mapping, duplicate ID, or port conflict prevents only that device from being restored; it does not prevent the other devices or the Web service from starting.

The catalog records `Running` or `Stopped` as the desired state when start/stop operations are requested. Restored devices are reconstructed stopped by default. This prevents a service restart from unexpectedly reopening industrial protocol listeners. An administrator may opt in to automatic restart of desired-Running catalog devices with:

```text
IndustrialSim:Restore:AutoStartDesiredRunning=true
```

With automatic start enabled, every device is still started independently. If a listener cannot bind, that device remains registered and stopped, its `Running` intent is retained for retry, and other devices continue restoring. Release the conflicting port and start the device again.

Legacy catalog rows that only stored protocol port reservations are restored conservatively without silently enabling a protocol. Edit and save such a device with a complete protocol configuration before expecting an external client to connect.

Template behavior metadata uses JSON such as:

```json
{
  "profile": "pump",
  "parameters": {
    "ratedSpeed": 1450,
    "accelerationSeconds": 10
  }
}
```

Supported profiles are `pump`, `motor`, `sensor`, and `none`. Invalid profile/type/schema/command combinations are rejected before the device is registered.

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
- the effective behavior profile and parameters explain automatic state changes;
- the startup scenario changes running state and speed;
- a reusable scenario can run on another compatible device without editing its steps;
- an overheat or network fault activates at the expected simulation time;
- a configured fault recovers when its duration ends;
- OPC UA or Modbus observes the same logical state as the Web console;
- Events explain the order of lifecycle, scenario, and fault operations;
- reusable devices and scenarios are saved as templates or catalog items.
- a Web-created protocol device can still be found after a service restart, and its protocol is only auto-started when the deployment explicitly enables that policy.

For startup, deployment, environment variables, ports, authentication mode, database location, or service-level troubleshooting, return to the [Service Startup Guide](STARTUP_GUIDE.md).

For exact API contracts, use `/openapi/v1.json`. For normative architecture and YAML behavior, see [PROJECT_SPEC.md](PROJECT_SPEC.md).
