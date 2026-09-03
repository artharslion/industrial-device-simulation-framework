<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { platformApi } from '../../api'
import { useTemplateEditor } from '../../composables/useTemplateEditor'
import { useWorkspaceStore } from '../../stores/workspace'
import type { TemplatePackage } from '../../types'

const props = defineProps<{ value?: TemplatePackage | null }>()
const emit = defineEmits<{ saved: []; cancelled: [] }>()
const workspace = useWorkspaceStore()
const editor = useTemplateEditor(platformApi)
const instance = ref({ deviceId: '', deterministic: true, seed: 1, modbusPort: 0, opcuaPort: 0 })
const message = ref('')
const tagText = computed({ get: () => editor.draft.template.tags.join(', '), set: value => { editor.draft.template.tags = value.split(',').map(item => item.trim()).filter(Boolean) } })
const commandText = computed({ get: () => editor.draft.template.commands.join(', '), set: value => { editor.draft.template.commands = value.split(',').map(item => item.trim()).filter(Boolean) } })
const eventText = computed({ get: () => editor.draft.template.events.join(', '), set: value => { editor.draft.template.events = value.split(',').map(item => item.trim()).filter(Boolean) } })

watch(() => props.value, value => { editor.reset(value ?? undefined); message.value = ''; if (!value && editor.draft.template.dataPoints.length === 0) editor.addDataPoint() }, { immediate: true })

async function save() {
  message.value = ''
  try { await editor.save(); workspace.notify(`Template ${editor.draft.template.id}@${editor.draft.template.version} saved`); emit('saved') }
  catch (cause) { message.value = cause instanceof Error ? cause.message : String(cause) }
}

async function instantiate() {
  message.value = ''
  const portBindings = [
    instance.value.modbusPort > 0 ? { protocol: 'modbus', port: instance.value.modbusPort } : null,
    instance.value.opcuaPort > 0 ? { protocol: 'opcua', port: instance.value.opcuaPort } : null,
  ].filter((value): value is { protocol: string; port: number } => value !== null)
  try { await editor.instantiate({ ...instance.value, portBindings }); workspace.notify(`Device ${instance.value.deviceId} created`) }
  catch (cause) { message.value = cause instanceof Error ? cause.message : String(cause) }
}

function download() {
  const blob = new Blob([editor.exportJson()], { type: 'application/json' }); const url = URL.createObjectURL(blob)
  const link = document.createElement('a'); link.href = url; link.download = `${editor.draft.template.id || 'template'}-${editor.draft.template.version}.json`; link.click(); URL.revokeObjectURL(url)
}

async function importFile(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return
  try { editor.importJson(await file.text()) } catch (cause) { message.value = cause instanceof Error ? cause.message : String(cause) }
}
</script>

<template>
  <section class="editor-surface" aria-label="Visual device template editor">
    <header class="editor-head"><div><span class="eyebrow">Template workshop</span><h2>{{ props.value ? 'Create a new version' : 'New device template' }}</h2><p>Definitions stay logical; protocol addresses live in separate mapping profiles.</p></div><button @click="emit('cancelled')">Close</button></header>
    <div v-if="message || editor.errors.value.length" class="inline-error" role="alert">{{ message || editor.errors.value.join(' ') }}</div>
    <div class="editor-section"><div class="section-title"><span>01</span><div><h3>Identity and version</h3><p>Versions are immutable after save.</p></div></div><div class="form-grid three">
      <div><label>Template ID</label><input v-model="editor.draft.template.id" placeholder="centrifugal-pump" :readonly="Boolean(props.value)" /></div>
      <div><label>Version</label><input v-model="editor.draft.template.version" placeholder="1.0.0" /></div>
      <div><label>Device type</label><input v-model="editor.draft.template.deviceType" placeholder="Pump" /></div>
      <div><label>Display name</label><input v-model="editor.draft.template.displayName" /></div>
      <div><label>Tags</label><input v-model="tagText" placeholder="water, rotating" /></div>
      <div class="wide"><label>Description</label><input v-model="editor.draft.template.description" /></div>
      <div><label>Commands</label><input v-model="commandText" placeholder="start, stop, reset" /></div>
      <div><label>Events</label><input v-model="eventText" placeholder="overheat, alarm" /></div>
      <div class="wide"><label>Behavior metadata JSON</label><textarea v-model="editor.draft.template.behaviorJson" class="compact-textarea" spellcheck="false"></textarea></div>
    </div></div>

    <div class="editor-section"><div class="section-title"><span>02</span><div><h3>Datapoints</h3><p>Typed runtime state created when this template is instantiated.</p></div><button class="primary" @click="editor.addDataPoint">Add datapoint</button></div>
      <div class="editable-table"><div class="editable-row header"><span>Name</span><span>Type</span><span>Access</span><span>Initial</span><span>Unit</span><span></span></div>
        <div v-for="(point, index) in editor.draft.template.dataPoints" :key="index" class="editable-row"><input v-model="point.name" aria-label="Datapoint name" /><select v-model="point.dataType"><option v-for="type in ['Boolean','Int16','Int32','Int64','UInt16','UInt32','Float','Double','String']" :key="type">{{ type }}</option></select><select v-model="point.access"><option>Read</option><option>Write</option><option>ReadWrite</option></select><input v-model="point.initial" aria-label="Initial value" /><input v-model="point.unit" aria-label="Unit" /><button class="icon-button danger" aria-label="Remove datapoint" @click="editor.removeDataPoint(index)">×</button></div>
      </div>
    </div>

    <div class="editor-section"><div class="section-title"><span>03</span><div><h3>Protocol mapping profiles</h3><p>Optional address profiles remain outside the logical definition.</p></div><div class="controls"><button @click="editor.addMapping('modbus')">+ Modbus</button><button @click="editor.addMapping('opcua')">+ OPC UA</button></div></div>
      <div v-if="editor.draft.mappings.length === 0" class="resource-state compact">No mappings. The template can still be used as a protocol-neutral device.</div>
      <article v-for="(mapping, mappingIndex) in editor.draft.mappings" :key="mappingIndex" class="mapping-card"><div class="mapping-head"><div class="form-grid"><div><label>Protocol</label><select v-model="mapping.protocol"><option>modbus</option><option>opcua</option></select></div><div><label>Profile name</label><input v-model="mapping.name" /></div></div><button class="danger" @click="editor.removeMapping(mappingIndex)">Remove profile</button></div>
        <div class="editable-table mapping"><div class="editable-row header"><span>Datapoint</span><span>Address / Node ID</span><span>Wire type</span><span>Byte order</span><span>Word order</span><span></span></div><div v-for="(entry, entryIndex) in mapping.entries" :key="entryIndex" class="editable-row"><select v-model="entry.dataPoint"><option v-for="point in editor.draft.template.dataPoints" :key="point.name" :value="point.name">{{ point.name || 'unnamed' }}</option></select><input v-model="entry.address" /><input v-model="entry.dataType" /><select v-model="entry.byteOrder"><option>BigEndian</option><option>LittleEndian</option></select><select v-model="entry.wordOrder"><option>HighLow</option><option>LowHigh</option></select><button class="icon-button danger" @click="editor.removeMappingEntry(mapping, entryIndex)">×</button></div></div><button @click="editor.addMappingEntry(mapping)">Add mapping row</button>
      </article>
    </div>

    <div class="editor-footer"><div class="controls"><label class="file-button">Import JSON<input type="file" accept="application/json" @change="importFile" /></label><button @click="download">Export JSON</button></div><button class="primary" :disabled="editor.saving.value || !workspace.canOperate" @click="save">Save immutable version</button></div>

    <div v-if="props.value" class="editor-section instantiate"><div class="section-title"><span>04</span><div><h3>Instantiate device</h3><p>Create an isolated SimulationHost from this saved version.</p></div></div><div class="form-grid three"><div><label>Device ID</label><input v-model="instance.deviceId" placeholder="line-1-pump" /></div><div><label>Seed</label><input v-model.number="instance.seed" type="number" /></div><div class="check-field"><label><input v-model="instance.deterministic" type="checkbox" /> Deterministic clock</label></div><div><label>Modbus port</label><input v-model.number="instance.modbusPort" type="number" min="0" /></div><div><label>OPC UA port</label><input v-model.number="instance.opcuaPort" type="number" min="0" /></div><div><button class="primary" :disabled="!instance.deviceId || !workspace.canOperate" @click="instantiate">Create SimulationHost</button></div></div></div>
  </section>
</template>
