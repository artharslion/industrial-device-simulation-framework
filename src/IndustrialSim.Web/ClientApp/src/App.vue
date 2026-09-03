<script setup lang="ts">
import { computed, inject, onBeforeUnmount, onMounted } from 'vue'
import { developerConsoleApi, developerConsoleApiKey } from './api'
import ConsolePanel from './components/ConsolePanel.vue'
import StatusCard from './components/StatusCard.vue'
import { useDeveloperConsole } from './composables/useDeveloperConsole'
import type { RuntimeEvent, ScalarValue } from './types'

const consoleApi = inject(developerConsoleApiKey, developerConsoleApi)
const consoleState = useDeveloperConsole(consoleApi)

const stateEntries = computed(() => Object.entries(consoleState.state.value))
const recentEvents = computed(() => consoleState.events.value.slice(-80).reverse())
const activityStatus = computed(() => consoleState.activity.value === 'Idle' ? 'idle' : 'active')
const deviceDescription = computed(() => {
  const runtime = consoleState.runtime.value
  return `${runtime.deviceType} · ${runtime.deterministic ? 'deterministic' : 'real-time'} · seed ${runtime.seed}`
})
const isBusy = computed(() => consoleState.pendingAction.value !== null)

function valueType(value: ScalarValue) {
  return value === null ? 'null' : typeof value
}

function formatValue(value: ScalarValue) {
  return JSON.stringify(value)
}

function eventTime(event: RuntimeEvent) {
  const raw = typeof event.timestamp === 'object' ? event.timestamp?.elapsed : event.timestamp ?? event.time
  if (!raw) return '--:--:--'
  if (raw.includes(':')) return raw.split('.')[0]
  const timestamp = new Date(raw)
  return Number.isNaN(timestamp.valueOf()) ? raw : timestamp.toLocaleTimeString([], { hour12: false })
}

function eventType(event: RuntimeEvent) {
  return event.eventType
    ?? event.type
    ?? (event.dataPointId ? 'DataPointChanged' : undefined)
    ?? (event.commandName ? 'CommandExecuted' : undefined)
    ?? (Object.prototype.hasOwnProperty.call(event, 'eventMetadata') ? 'Lifecycle' : 'RuntimeEvent')
}

function faultCategory(category: number | string) {
  if (typeof category === 'string') return category
  return ['Data', 'Device', 'Network'][category] ?? String(category)
}

onMounted(async () => {
  await consoleState.refresh()
  consoleState.startPolling()
})

onBeforeUnmount(consoleState.stopPolling)
</script>

<template>
  <div class="app-shell">
    <aside class="workspace-sidebar" aria-label="Workspace navigation">
      <div class="brand">
        <div class="brand-mark">IS</div>
        <div class="brand-copy">
          <strong>Industrial Sim</strong>
          <span>Developer runtime</span>
        </div>
      </div>
      <div class="sidebar-label">Workspace</div>
      <nav class="sidebar-nav">
        <a class="nav-item active" href="#overview">Overview</a>
        <a class="nav-item" href="#state-store">State store</a>
        <a class="nav-item" href="#runtime-events">Events</a>
        <a class="nav-item" href="#scenario-control">Scenarios</a>
        <a class="nav-item" href="#fault-control">Faults</a>
      </nav>
      <div class="sidebar-spacer"></div>
      <div class="environment-card">
        <span>Connected device</span>
        <strong>{{ consoleState.runtime.value.deviceId }}</strong>
        <small>{{ consoleState.runtime.value.deviceType }}</small>
      </div>
    </aside>

    <section class="command-center">
      <header class="workspace-header">
        <div class="breadcrumb">
          <strong>Workspace</strong><span class="slash">/</span><span>Runtime</span>
          <span class="slash optional">/</span><span class="optional">Command center</span>
        </div>
        <div class="header-meta">
          <i class="sync-dot" :class="{ syncing: consoleState.loading.value }" aria-hidden="true"></i>
          <span>{{ consoleState.lastSync.value }}</span>
        </div>
      </header>

      <Transition name="notice">
        <div v-if="consoleState.error.value" id="validation-error" role="alert" aria-live="assertive">
          <span>{{ consoleState.error.value }}</span>
          <button type="button" aria-label="Dismiss error" @click="consoleState.error.value = ''">×</button>
        </div>
      </Transition>

      <main id="overview" class="content">
        <section class="page-intro">
          <div>
            <div class="eyebrow">Runtime operations</div>
            <h1>Industrial Device Simulation</h1>
            <p>Inspect shared state, coordinate scenarios, and inject controlled failures from one live operational workspace.</p>
          </div>
          <div class="device-chip">
            <div class="device-glyph">D1</div>
            <div>
              <strong>{{ consoleState.runtime.value.deviceId }}</strong>
              <span>{{ deviceDescription }}</span>
            </div>
          </div>
        </section>

        <section class="status-grid" aria-label="Runtime status" aria-live="polite">
          <StatusCard label="Runtime" :value="consoleState.runtime.value.state" :status="consoleState.runtime.value.state" />
          <StatusCard label="OPC UA" :value="consoleState.protocols.value.opcua ? 'Online' : 'Offline'" :status="consoleState.protocols.value.opcua ? 'running' : 'stopped'" />
          <StatusCard label="Modbus TCP" :value="consoleState.protocols.value.modbus ? 'Online' : 'Offline'" :status="consoleState.protocols.value.modbus ? 'running' : 'stopped'" />
          <StatusCard label="Activity" :value="consoleState.activity.value" :status="activityStatus" />
        </section>

        <div class="dashboard-grid">
          <ConsolePanel id="state-store" class="state-panel" title="StateStore datapoints" :meta="consoleState.runtime.value.time">
            <div class="panel-body table-wrap">
              <table>
                <thead><tr><th>Signal</th><th>Type</th><th>Runtime value</th></tr></thead>
                <tbody>
                  <tr v-if="stateEntries.length === 0"><td colspan="3">No datapoints</td></tr>
                  <tr v-for="[name, value] in stateEntries" :key="name">
                    <td>{{ name }}</td>
                    <td><span class="type-badge">{{ valueType(value) }}</span></td>
                    <td><code>{{ formatValue(value) }}</code></td>
                  </tr>
                </tbody>
              </table>
            </div>
          </ConsolePanel>

          <ConsolePanel class="runtime-panel" title="Runtime control" meta="Running · Paused · Stopped">
            <div class="panel-body">
              <p class="runtime-copy">Control the simulation clock and lifecycle. Advance is available only in deterministic mode.</p>
              <div class="controls">
                <button type="button" class="primary" :disabled="isBusy" @click="consoleState.runRuntimeCommand('start')">Start / Resume</button>
                <button type="button" :disabled="isBusy" @click="consoleState.runRuntimeCommand('pause')">Pause</button>
                <button type="button" class="danger" :disabled="isBusy" @click="consoleState.runRuntimeCommand('stop')">Stop</button>
                <button type="button" :disabled="isBusy" @click="consoleState.runRuntimeCommand('reset')">Reset</button>
                <button type="button" :disabled="isBusy || !consoleState.runtime.value.deterministic" @click="consoleState.tick">Advance 1s</button>
              </div>
            </div>
          </ConsolePanel>

          <ConsolePanel id="runtime-events" class="events-panel" title="Runtime events" meta="Commit ordered · latest 80">
            <div class="event-terminal" aria-live="polite">
              <div v-if="recentEvents.length === 0" class="event-empty">Waiting for runtime events…</div>
              <div v-for="(event, index) in recentEvents" :key="index" class="event-row">
                <span class="event-time">{{ eventTime(event) }}</span>
                <span class="event-type">{{ eventType(event) }}</span>
                <span class="event-data">{{ JSON.stringify(event) }}</span>
              </div>
            </div>
          </ConsolePanel>

          <ConsolePanel id="scenario-control" class="scenario-panel" title="Scenario control" :meta="consoleState.runtime.value.scenario.running ? 'Running' : 'Stopped'">
            <div class="panel-body">
              <label for="scenario-yaml">Scenario YAML</label>
              <textarea id="scenario-yaml" v-model="consoleState.scenarioYaml.value" spellcheck="false"></textarea>
              <div class="controls action-row">
                <button type="button" class="primary" :disabled="isBusy" @click="consoleState.runScenario">Run Scenario</button>
                <button type="button" class="danger" :disabled="isBusy || !consoleState.runtime.value.scenario.running" @click="consoleState.stopScenario">Stop Scenario</button>
              </div>
            </div>
          </ConsolePanel>

          <ConsolePanel id="fault-control" class="fault-panel" title="Fault control" meta="Data · Device · Network">
            <div class="panel-body">
              <div class="form-grid">
                <div>
                  <label for="fault-category">Category</label>
                  <select id="fault-category" v-model="consoleState.faultForm.category">
                    <option>Data</option><option>Device</option><option>Network</option>
                  </select>
                </div>
                <div><label for="fault-type">Type</label><input id="fault-type" v-model="consoleState.faultForm.type" /></div>
                <div><label for="fault-target">Target</label><input id="fault-target" v-model="consoleState.faultForm.target" /></div>
                <div><label for="fault-parameter">Parameter</label><input id="fault-parameter" v-model="consoleState.faultForm.parameter" /></div>
                <div class="wide"><button type="button" class="danger" :disabled="isBusy" @click="consoleState.activateFault">Activate Fault</button></div>
              </div>
              <div class="fault-list">
                <div v-if="consoleState.faults.value.length === 0" class="hint">No active faults.</div>
                <div v-for="fault in consoleState.faults.value" :key="fault.id" class="fault-row">
                  <span>{{ fault.id }} · {{ faultCategory(fault.category) }} · {{ fault.type }}</span>
                  <button type="button" :disabled="isBusy" @click="consoleState.recoverFault(fault.id)">Recover</button>
                </div>
              </div>
            </div>
          </ConsolePanel>
        </div>
      </main>
    </section>
  </div>
</template>
