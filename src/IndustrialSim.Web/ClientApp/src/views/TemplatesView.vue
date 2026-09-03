<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { platformApi } from '../api'
import PageHeader from '../components/PageHeader.vue'
import ResourceState from '../components/ResourceState.vue'
import TemplateEditor from '../components/templates/TemplateEditor.vue'
import type { DeviceTemplateDocument, TemplatePackage } from '../types'

const templates = ref<DeviceTemplateDocument[]>([]); const loading = ref(true); const error = ref(''); const query = ref(''); const editing = ref(false); const selected = ref<TemplatePackage | null>(null)
async function load() { loading.value = true; try { templates.value = await platformApi.templates(query.value) } catch (cause) { error.value = cause instanceof Error ? cause.message : String(cause) } finally { loading.value = false } }
async function open(item: DeviceTemplateDocument) { try { selected.value = await platformApi.template(item.id, item.version); editing.value = true } catch (cause) { error.value = String(cause) } }
function create() { selected.value = null; editing.value = true }
async function saved() { await load(); editing.value = false }
onMounted(load)
</script>
<template><main class="content wide-content"><PageHeader eyebrow="Model" title="Device templates" description="Create complete logical device definitions, version them immutably, and attach explicit OPC UA or Modbus mappings."><input v-model="query" class="search-input" placeholder="Search templates" @keyup.enter="load" /><button @click="load">Search</button><button class="primary" @click="create">New template</button></PageHeader>
  <TemplateEditor v-if="editing" :value="selected" @saved="saved" @cancelled="editing = false" />
  <ResourceState v-else :loading="loading" :error="error" :empty="templates.length === 0" empty-text="No templates yet. Create the first reusable device definition."><div class="card-grid"><button v-for="item in templates" :key="`${item.id}@${item.version}`" class="entity-card card-button" @click="open(item)"><div class="entity-card-head"><div><span class="type-badge">{{ item.deviceType }}</span><h2>{{ item.displayName }}</h2></div><span class="version-chip">v{{ item.version }}</span></div><p>{{ item.description }}</p><div class="tag-row"><span v-for="tag in item.tags" :key="tag">{{ tag }}</span></div><small>{{ item.dataPoints.length }} datapoints · {{ item.commands.length }} commands</small></button></div></ResourceState>
</main></template>
