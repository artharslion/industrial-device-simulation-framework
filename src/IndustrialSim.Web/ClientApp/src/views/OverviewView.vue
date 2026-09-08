<script setup lang="ts">
import { computed, inject, onBeforeUnmount, onMounted, ref } from 'vue'
import { platformApi } from '../api'
import PageHeader from '../components/PageHeader.vue'
import ResourceState from '../components/ResourceState.vue'
import StatusCard from '../components/StatusCard.vue'
import { runtimeLiveConnection, runtimeLiveConnectionKey } from '../composables/useRuntimeSignalR'
import { useWorkspaceStore } from '../stores/workspace'
import type { DeviceDetails, DeviceSummary, ProtocolSummary, RuntimeEvent } from '../types'

const workspace = useWorkspaceStore(); const live = inject(runtimeLiveConnectionKey, runtimeLiveConnection)
const devices = ref<DeviceSummary[]>([]); const details = ref<DeviceDetails[]>([]); const protocols = ref<ProtocolSummary[]>([]); const loading = ref(true); const error = ref(''); let poll: number | undefined
const running = computed(() => devices.value.filter(device => device.isRunning).length)
const stopped = computed(() => devices.value.length - running.value)
const activeFaults = computed(() => details.value.reduce((sum, item) => sum + item.faults.length, 0))
const protocolOnline = computed(() => protocols.value.flatMap(item => item.configured).filter(item => item.running).length)
const protocolTotal = computed(() => protocols.value.reduce((sum, item) => sum + item.configured.length + item.reserved.length, 0))
const selected = computed(() => details.value.find(item => item.summary.deviceId === workspace.selectedDeviceId) ?? details.value[0] ?? null)
const recentEvents = computed(() => details.value.flatMap(item => item.events.map(event => ({ deviceId: item.summary.deviceId, event }))).slice(-8).reverse())
const noteworthy = computed(() => recentEvents.value.filter(item => /error|fault|fail|stop/i.test(JSON.stringify(item.event))).slice(0, 4))

async function load(silent = false) { if (!silent) loading.value = true; try { devices.value = await platformApi.devices(); protocols.value = await platformApi.protocols(); details.value = await Promise.all(devices.value.map(device => platformApi.device(device.deviceId))); if (!workspace.selectedDeviceId && devices.value[0]) workspace.selectedDeviceId = devices.value[0].deviceId; error.value = '' } catch (cause) { error.value = cause instanceof Error ? cause.message : String(cause) } finally { loading.value = false } }
function beginPolling() { if (!poll) poll = window.setInterval(() => void load(true), 5000) }
function stopPolling() { if (poll) window.clearInterval(poll); poll = undefined }
const eventName = (event: RuntimeEvent) => event.eventType ?? event.type ?? 'RuntimeEvent'
onMounted(async () => { await load(); try { await live.start(() => void load(true), beginPolling, stopPolling) } catch { beginPolling() } })
onBeforeUnmount(() => { stopPolling(); void live.stop() })
</script>

<template><main class="content"><PageHeader eyebrow="Workspace" title="Platform overview" description="See fleet health, recent activity, and the next useful action. Advanced editors live on their dedicated pages." />
  <div v-if="error" class="inline-error" role="alert">{{ error }}</div>
  <ResourceState :loading="loading" :error="''" :empty="false"><section class="status-grid" aria-label="Fleet status"><StatusCard label="Devices" :value="String(devices.length)" status="active" /><StatusCard label="Running" :value="String(running)" :status="running ? 'running' : 'idle'" /><StatusCard label="Stopped" :value="String(stopped)" :status="stopped ? 'stopped' : 'idle'" /><StatusCard label="Active faults" :value="String(activeFaults)" :status="activeFaults ? 'error' : 'idle'" /></section>
    <section v-if="devices.length === 0" class="welcome-empty"><span class="eyebrow">First run</span><h2>Create your first simulation device</h2><p>Start with a quick logical definition or instantiate a reusable template. You can add scenarios and faults after the device exists.</p><div class="controls"><RouterLink class="button-link" to="/devices">Create device</RouterLink><RouterLink class="button-link secondary-link" to="/templates">Create template</RouterLink></div></section>
    <div v-else class="overview-grid"><section class="panel overview-health"><header class="panel-head"><div class="panel-title"><i class="panel-icon"></i><h2>Protocol health</h2></div><small>{{ protocolOnline }} online / {{ protocolTotal }} configured or reserved</small></header><div class="panel-body"><p class="runtime-copy">Protocol adapters expose the same logical StateStore. A network fault does not stop device simulation.</p><div class="protocol-pills"><template v-for="row in protocols" :key="row.deviceId"><span v-for="item in row.configured" :key="`${row.deviceId}-${item.name}`" :class="{ online: item.running }">{{ row.deviceId }} · {{ item.name }} · {{ item.running ? 'online' : 'offline' }}</span><span v-for="item in row.reserved" :key="`${row.deviceId}-${item.name}-${item.port}`">{{ row.deviceId }} · {{ item.name }} : {{ item.port }}</span></template></div></div></section>
      <section class="panel overview-selected"><header class="panel-head"><div class="panel-title"><i class="panel-icon"></i><h2>Selected device</h2></div><small>{{ selected?.summary.deviceId }}</small></header><div class="panel-body" v-if="selected"><select v-model="workspace.selectedDeviceId" aria-label="Selected device"><option v-for="device in devices" :key="device.deviceId" :value="device.deviceId">{{ device.deviceId }}</option></select><h3>{{ selected.summary.deviceType }} · {{ selected.runtime.state }}</h3><p class="runtime-copy">{{ Object.keys(selected.state).length }} datapoints · {{ selected.faults.length }} active faults · simulation time {{ selected.summary.simulationTime }}</p><RouterLink class="button-link" :to="`/devices/${encodeURIComponent(selected.summary.deviceId)}`">Open device details</RouterLink></div></section>
      <section class="panel overview-events"><header class="panel-head"><div class="panel-title"><i class="panel-icon"></i><h2>Recent runtime events</h2></div><small>SignalR live · polling fallback</small></header><div class="event-terminal"><div v-if="recentEvents.length === 0" class="event-empty">No runtime events yet. Start a device or run a scenario to create activity.</div><div v-for="(item,index) in recentEvents" :key="index" class="event-row"><span class="event-time">{{ item.deviceId }}</span><span class="event-type">{{ eventName(item.event) }}</span><span class="event-data">{{ JSON.stringify(item.event) }}</span></div></div></section>
      <section class="panel overview-errors"><header class="panel-head"><div class="panel-title"><i class="panel-icon"></i><h2>Errors and fault activity</h2></div><small>{{ activeFaults }} active</small></header><div class="panel-body"><p v-if="noteworthy.length === 0" class="hint">No recent error, failure, fault, or stop events.</p><div v-for="(item,index) in noteworthy" :key="index" class="fault-row"><span>{{ item.deviceId }} · {{ eventName(item.event) }}</span></div><p class="runtime-copy">{{ activeFaults }} active fault{{ activeFaults === 1 ? '' : 's' }} across the fleet.</p></div></section>
    </div>
    <section class="quick-links"><RouterLink to="/devices"><strong>Create device</strong><span>Define and launch a SimulationHost.</span></RouterLink><RouterLink to="/templates"><strong>Create template</strong><span>Build a reusable, versioned definition.</span></RouterLink><RouterLink to="/scenarios"><strong>Create scenario</strong><span>Model deterministic runtime actions.</span></RouterLink><RouterLink to="/events"><strong>View faults/events</strong><span>Inspect observable runtime activity.</span></RouterLink></section>
  </ResourceState></main></template>
