<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { platformApi } from '../api'
import PageHeader from '../components/PageHeader.vue'
import ResourceState from '../components/ResourceState.vue'
import ScenarioEditor from '../components/scenarios/ScenarioEditor.vue'
import type { DeviceSummary, ScenarioCatalogItem } from '../types'

const scenarios = ref<ScenarioCatalogItem[]>([]); const devices = ref<DeviceSummary[]>([]); const loading = ref(true); const error = ref(''); const editing = ref(false); const selected = ref<ScenarioCatalogItem | null>(null)
async function load() { loading.value = true; try { [scenarios.value, devices.value] = await Promise.all([platformApi.scenarios(), platformApi.devices()]) } catch (cause) { error.value = cause instanceof Error ? cause.message : String(cause) } finally { loading.value = false } }
function open(item: ScenarioCatalogItem) { selected.value = item; editing.value = true }
function create() { selected.value = null; editing.value = true }
async function saved() { await load(); editing.value = false }
onMounted(load)
</script>
<template><main class="content wide-content"><PageHeader eyebrow="Model" title="Scenario flows" description="Compose ordered triggers and actions visually, import or export YAML, then run the saved revision on a selected device."><button class="primary" @click="create">New scenario</button></PageHeader>
  <ScenarioEditor v-if="editing" :value="selected" :devices="devices" @saved="saved" @cancelled="editing = false" />
  <ResourceState v-else :loading="loading" :error="error" :empty="scenarios.length === 0" empty-text="No saved scenarios yet. Build the first deterministic flow."><div class="list-panel"><button v-for="scenario in scenarios" :key="scenario.id" class="list-row row-button" @click="open(scenario)"><div><strong>{{ scenario.name }}</strong><small>{{ scenario.id }} · YAML + visual metadata</small></div><span class="version-chip">rev {{ scenario.version }}</span></button></div></ResourceState>
</main></template>
