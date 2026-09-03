<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { platformApi, request } from '../api'
import PageHeader from '../components/PageHeader.vue'
import ResourceState from '../components/ResourceState.vue'
import { useWorkspaceStore } from '../stores/workspace'
import type { DeviceSummary } from '../types'

const workspace = useWorkspaceStore(); const devices = ref<DeviceSummary[]>([]); const loading = ref(true); const error = ref('')
async function load() { loading.value = true; try { devices.value = await platformApi.devices(); if (!workspace.selectedDeviceId && devices.value[0]) workspace.selectedDeviceId = devices.value[0].deviceId } catch (cause) { error.value = String(cause) } finally { loading.value = false } }
async function lifecycle(id: string, operation: string) { try { await request(`/api/v1/devices/${encodeURIComponent(id)}/${operation}`, { method: 'POST' }); workspace.notify(`${id} ${operation} completed`); await load() } catch (cause) { error.value = cause instanceof Error ? cause.message : String(cause) } }
onMounted(load)
</script>
<template><main class="content"><PageHeader eyebrow="Fleet" title="Simulation devices" description="Select and operate isolated SimulationHosts. Live state remains authoritative on each host." />
  <ResourceState :loading="loading" :error="error" :empty="devices.length === 0" empty-text="No devices are registered. Instantiate one from Templates."><div class="card-grid">
    <article v-for="device in devices" :key="device.deviceId" class="entity-card" :class="{ selected: workspace.selectedDeviceId === device.deviceId }" @click="workspace.selectedDeviceId = device.deviceId"><div class="entity-card-head"><div><span class="type-badge">{{ device.deviceType }}</span><h2>{{ device.deviceId }}</h2></div><i class="lamp" :class="device.isRunning ? 'running' : 'stopped'"></i></div><dl><div><dt>Mode</dt><dd>{{ device.deterministic ? 'Deterministic' : 'Real time' }}</dd></div><div><dt>Seed</dt><dd>{{ device.seed }}</dd></div><div><dt>Clock</dt><dd>{{ device.simulationTime }}</dd></div></dl><div class="controls"><button class="primary" @click.stop="lifecycle(device.deviceId, 'start')">Start</button><button @click.stop="lifecycle(device.deviceId, 'pause')">Pause</button><button class="danger" @click.stop="lifecycle(device.deviceId, 'stop')">Stop</button></div></article>
  </div></ResourceState></main></template>
