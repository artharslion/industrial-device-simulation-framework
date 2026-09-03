<script setup lang="ts">
import { computed, inject, onBeforeUnmount, onMounted } from 'vue'
import { developerConsoleApi, developerConsoleApiKey } from '../api'
import ConsolePanel from '../components/ConsolePanel.vue'
import PageHeader from '../components/PageHeader.vue'
import StatusCard from '../components/StatusCard.vue'
import { useDeveloperConsole } from '../composables/useDeveloperConsole'
import { runtimeLiveConnection, runtimeLiveConnectionKey } from '../composables/useRuntimeSignalR'
import type { RuntimeEvent, ScalarValue } from '../types'

const consoleApi = inject(developerConsoleApiKey, developerConsoleApi)
const liveConnection = inject(runtimeLiveConnectionKey, runtimeLiveConnection)
const consoleState = useDeveloperConsole(consoleApi, liveConnection)
const stateEntries = computed(() => Object.entries(consoleState.state.value))
const recentEvents = computed(() => consoleState.events.value.slice(-12).reverse())
const busy = computed(() => consoleState.pendingAction.value !== null)
const valueType = (value: ScalarValue) => value === null ? 'null' : typeof value
const eventType = (event: RuntimeEvent) => event.eventType ?? event.type ?? (event.dataPointId ? 'DataPointChanged' : 'RuntimeEvent')
const faultCategory = (category: number | string) => typeof category === 'string' ? category : ['Data', 'Device', 'Network'][category] ?? String(category)

onMounted(async () => { await consoleState.refresh(); await consoleState.startLiveUpdates() })
onBeforeUnmount(() => { void consoleState.stopLiveUpdates() })
</script>

<template>
  <main class="content">
    <PageHeader eyebrow="Live operations" title="Runtime command center" description="Inspect StateStore, control deterministic time, run scenarios, and recover injected faults without leaving the shared runtime." />
    <div v-if="consoleState.error.value" class="inline-error" role="alert">{{ consoleState.error.value }}</div>
    <section class="status-grid" aria-label="Runtime status">
      <StatusCard label="Runtime" :value="consoleState.runtime.value.state" :status="consoleState.runtime.value.state" />
      <StatusCard label="OPC UA" :value="consoleState.protocols.value.opcua ? 'Online' : 'Offline'" :status="consoleState.protocols.value.opcua ? 'running' : 'stopped'" />
      <StatusCard label="Modbus TCP" :value="consoleState.protocols.value.modbus ? 'Online' : 'Offline'" :status="consoleState.protocols.value.modbus ? 'running' : 'stopped'" />
      <StatusCard label="Activity" :value="consoleState.activity.value" :status="consoleState.activity.value === 'Idle' ? 'idle' : 'active'" />
    </section>
    <div class="dashboard-grid">
      <ConsolePanel class="state-panel" title="StateStore datapoints" :meta="consoleState.runtime.value.time">
        <div class="panel-body table-wrap"><table><thead><tr><th>Signal</th><th>Type</th><th>Runtime value</th></tr></thead><tbody>
          <tr v-if="stateEntries.length === 0"><td colspan="3">No datapoints</td></tr>
          <tr v-for="[name, value] in stateEntries" :key="name"><td>{{ name }}</td><td><span class="type-badge">{{ valueType(value) }}</span></td><td><code>{{ JSON.stringify(value) }}</code></td></tr>
        </tbody></table></div>
      </ConsolePanel>
      <ConsolePanel class="runtime-panel" title="Runtime control" :meta="consoleState.runtime.value.deviceId">
        <div class="panel-body"><p class="runtime-copy">Lifecycle commands always execute on the backend-owned SimulationHost.</p><div class="controls">
          <button class="primary" :disabled="busy" @click="consoleState.runRuntimeCommand('start')">Start / Resume</button>
          <button :disabled="busy" @click="consoleState.runRuntimeCommand('pause')">Pause</button>
          <button class="danger" :disabled="busy" @click="consoleState.runRuntimeCommand('stop')">Stop</button>
          <button :disabled="busy" @click="consoleState.runRuntimeCommand('reset')">Reset</button>
          <button :disabled="busy || !consoleState.runtime.value.deterministic" @click="consoleState.tick">Advance 1s</button>
        </div></div>
      </ConsolePanel>
      <ConsolePanel class="events-panel" title="Recent runtime events" meta="SignalR ordered">
        <div class="event-terminal"><div v-if="recentEvents.length === 0" class="event-empty">Waiting for runtime events…</div>
          <div v-for="(event, index) in recentEvents" :key="index" class="event-row"><span class="event-time">{{ index + 1 }}</span><span class="event-type">{{ eventType(event) }}</span><span class="event-data">{{ JSON.stringify(event) }}</span></div>
        </div>
      </ConsolePanel>
      <ConsolePanel class="scenario-panel" title="Quick scenario" :meta="consoleState.runtime.value.scenario.running ? 'Running' : 'Stopped'">
        <div class="panel-body"><label for="scenario-yaml">Scenario YAML</label><textarea id="scenario-yaml" v-model="consoleState.scenarioYaml.value" spellcheck="false"></textarea><div class="controls action-row">
          <button class="primary" :disabled="busy" @click="consoleState.runScenario">Run scenario</button><button class="danger" :disabled="busy || !consoleState.runtime.value.scenario.running" @click="consoleState.stopScenario">Stop</button>
        </div></div>
      </ConsolePanel>
      <ConsolePanel class="fault-panel" title="Fault injection" meta="Data · Device · Network">
        <div class="panel-body"><div class="form-grid"><div><label>Category</label><select v-model="consoleState.faultForm.category"><option>Data</option><option>Device</option><option>Network</option></select></div><div><label>Type</label><input v-model="consoleState.faultForm.type" /></div><div><label>Target</label><input v-model="consoleState.faultForm.target" /></div><div><label>Parameter</label><input v-model="consoleState.faultForm.parameter" /></div><div class="wide"><button class="danger" :disabled="busy" @click="consoleState.activateFault">Activate fault</button></div></div>
          <div class="fault-list"><div v-if="consoleState.faults.value.length === 0" class="hint">No active faults.</div><div v-for="fault in consoleState.faults.value" :key="fault.id" class="fault-row"><span>{{ fault.id }} · {{ faultCategory(fault.category) }} · {{ fault.type }}</span><button @click="consoleState.recoverFault(fault.id)">Recover</button></div></div>
        </div>
      </ConsolePanel>
    </div>
  </main>
</template>
