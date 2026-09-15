<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { platformApi } from '../api'
import PageHeader from '../components/PageHeader.vue'
import ResourceState from '../components/ResourceState.vue'
import type { DeviceSummary, RuntimeEvent } from '../types'

const devices = ref<DeviceSummary[]>([])
const events = ref<RuntimeEvent[]>([])
const loading = ref(true)
const error = ref('')
const filters = reactive({ deviceId: '', eventType: '', search: '' })

async function loadEvents() {
  loading.value = true
  error.value = ''
  try {
    events.value = await platformApi.runtimeEvents({
      deviceId: filters.deviceId || undefined,
      eventType: filters.eventType.trim() || undefined,
      search: filters.search.trim() || undefined,
      limit: 200,
    })
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : String(cause)
  } finally {
    loading.value = false
  }
}

async function clearFilters() {
  filters.deviceId = ''
  filters.eventType = ''
  filters.search = ''
  await loadEvents()
}

onMounted(async () => {
  try {
    devices.value = await platformApi.devices()
    await loadEvents()
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : String(cause)
    loading.value = false
  }
})
</script>

<template>
  <main class="content events-page">
    <PageHeader eyebrow="Observe" title="Runtime event stream" description="Search retained, commit-ordered device, command, scenario, and fault activity emitted by the backend runtime.">
      <span class="event-result-count">{{ events.length }} retained event{{ events.length === 1 ? '' : 's' }}</span>
    </PageHeader>
    <form class="event-filter-bar" @submit.prevent="loadEvents">
      <div>
        <label for="event-device">Device</label>
        <select id="event-device" v-model="filters.deviceId" aria-label="Device filter">
          <option value="">All devices</option>
          <option v-for="device in devices" :key="device.deviceId" :value="device.deviceId">{{ device.deviceId }}</option>
        </select>
      </div>
      <div>
        <label for="event-type">Event type</label>
        <input id="event-type" v-model="filters.eventType" aria-label="Event type filter" placeholder="DataPointChanged" />
      </div>
      <div class="event-search-field">
        <label for="event-search">Search</label>
        <input id="event-search" v-model="filters.search" aria-label="Search event payloads" placeholder="alarm, temperature, fault…" />
      </div>
      <div class="event-filter-actions">
        <button type="button" @click="clearFilters">Clear</button>
        <button class="primary" type="submit">Search</button>
      </div>
    </form>
    <ResourceState :loading="loading" :error="error" :empty="events.length === 0" empty-text="No retained runtime events match these filters.">
      <div class="event-terminal expanded">
        <div v-for="event in events.slice().reverse()" :key="event.sequence ?? JSON.stringify(event)" class="event-row">
          <span class="event-time">#{{ event.sequence ?? '—' }}</span>
          <span class="event-device">{{ event.deviceId ?? 'unknown' }}</span>
          <span class="event-type">{{ event.eventType ?? event.type ?? 'RuntimeEvent' }}</span>
          <span class="event-data">{{ JSON.stringify(event) }}</span>
        </div>
      </div>
    </ResourceState>
  </main>
</template>
