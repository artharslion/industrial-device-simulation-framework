<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { platformApi } from '../../api'
import { useScenarioEditor } from '../../composables/useScenarioEditor'
import { useWorkspaceStore } from '../../stores/workspace'
import type { DeviceSummary, ScenarioCatalogItem } from '../../types'

const props = defineProps<{ value?: ScenarioCatalogItem | null; devices: DeviceSummary[] }>()
const emit = defineEmits<{ saved: []; cancelled: [] }>()
const editor = useScenarioEditor(platformApi)
const workspace = useWorkspaceStore()
const message = ref('')
const yaml = computed(() => { try { return editor.toYaml() } catch { return '' } })

watch(() => props.value, value => {
  message.value = ''
  if (value) editor.load(value)
  else { editor.id.value = ''; editor.name.value = ''; editor.version.value = 0; editor.steps.splice(0); editor.addStep('set') }
}, { immediate: true })

async function save() {
  try { await editor.save(); workspace.notify(`Scenario ${editor.id.value} saved`); emit('saved') }
  catch (cause) { message.value = cause instanceof Error ? cause.message : String(cause) }
}

async function run() {
  const deviceId = workspace.selectedDeviceId || props.devices[0]?.deviceId
  if (!deviceId) { message.value = 'Create or select a device first.'; return }
  try { await editor.run(deviceId); workspace.notify(`Scenario started on ${deviceId}`) }
  catch (cause) { message.value = String(cause) }
}

function download() {
  const blob = new Blob([yaml.value], { type: 'application/yaml' }); const url = URL.createObjectURL(blob)
  const link = document.createElement('a'); link.href = url; link.download = `${editor.id.value || 'scenario'}.yaml`; link.click(); URL.revokeObjectURL(url)
}

async function importFile(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return
  try { editor.importYaml(await file.text()); if (!editor.id.value) editor.id.value = file.name.replace(/\.ya?ml$/i, '') }
  catch (cause) { message.value = cause instanceof Error ? cause.message : String(cause) }
}
</script>

<template>
  <section class="editor-surface scenario-editor" aria-label="Graphical scenario editor">
    <header class="editor-head"><div><span class="eyebrow">Scenario flow</span><h2>{{ props.value ? props.value.name : 'New deterministic scenario' }}</h2><p>Visual order is explicit; generated YAML remains the executable public contract.</p></div><button @click="emit('cancelled')">Close</button></header>
    <div v-if="message || editor.errors.value.length" class="inline-error" role="alert">{{ message || editor.errors.value.join(' ') }}</div>
    <div class="form-grid three scenario-identity"><div><label>Scenario ID</label><input v-model="editor.id.value" :readonly="Boolean(props.value)" /></div><div><label>Name</label><input v-model="editor.name.value" /></div><div><label>Revision</label><input :value="editor.version.value" readonly /></div></div>
    <div class="scenario-workbench">
      <div class="flow-canvas">
        <div class="flow-toolbar"><strong>Execution flow</strong><div class="controls"><button @click="editor.addStep('set')">+ Set</button><button @click="editor.addStep('ramp')">+ Ramp</button><button @click="editor.addStep('command')">+ Command</button><button @click="editor.addStep('wait')">+ Wait</button><button @click="editor.addStep('fault')">+ Fault</button></div></div>
        <div class="flow-rail">
          <article v-for="(step, index) in editor.steps" :key="step.id" class="flow-card">
            <div class="flow-index"><span>{{ String(index + 1).padStart(2, '0') }}</span><i></i></div>
            <div class="flow-card-body">
              <header><div><span class="type-badge">{{ step.actionType }}</span><strong>{{ step.id }}</strong></div><div class="controls"><button class="icon-button" :disabled="index === 0" @click="editor.moveStep(index, -1)">↑</button><button class="icon-button" :disabled="index === editor.steps.length - 1" @click="editor.moveStep(index, 1)">↓</button><button class="icon-button danger" @click="editor.removeStep(index)">×</button></div></header>
              <div class="form-grid three">
                <div><label>Trigger</label><select v-model="step.triggerType"><option>at</option><option>after</option><option>every</option><option>when</option></select></div>
                <div><label>{{ step.triggerType === 'when' ? 'Condition' : 'Time' }}</label><input v-model="step.triggerValue" :placeholder="step.triggerType === 'when' ? 'speed >= 900' : '0s'" /></div>
                <div><label>Action</label><select v-model="step.actionType"><option>set</option><option>ramp</option><option>command</option><option>wait</option><option>fault</option></select></div>
                <div v-if="step.actionType !== 'wait'"><label>Device</label><select v-model="step.device"><option value="">Select device</option><option v-for="device in props.devices" :key="device.deviceId" :value="device.deviceId">{{ device.deviceId }}</option></select></div>
                <template v-if="step.actionType === 'set'"><div><label>Datapoint</label><input v-model="step.dataPoint" placeholder="speed" /></div><div><label>Value</label><input v-model="step.value" /></div></template>
                <template v-else-if="step.actionType === 'ramp'"><div><label>Datapoint</label><input v-model="step.dataPoint" /></div><div><label>From</label><input v-model.number="step.from" type="number" /></div><div><label>To</label><input v-model.number="step.to" type="number" /></div><div><label>Duration</label><input v-model="step.duration" placeholder="5s" /></div></template>
                <template v-else-if="step.actionType === 'command'"><div><label>Command</label><input v-model="step.command" placeholder="start" /></div></template>
                <template v-else-if="step.actionType === 'wait'"><div><label>Duration</label><input v-model="step.duration" placeholder="2s" /></div></template>
                <template v-else><div><label>Fault type</label><input v-model="step.faultType" /></div><div><label>Protocol target</label><input v-model="step.protocol" placeholder="optional" /></div><div><label>Duration</label><input v-model="step.duration" placeholder="5s" /></div></template>
              </div>
            </div>
          </article>
        </div>
      </div>
      <aside class="yaml-preview"><div class="flow-toolbar"><strong>Generated YAML</strong><span>Read-only preview</span></div><pre>{{ yaml }}</pre></aside>
    </div>
    <div class="editor-footer"><div class="controls"><label class="file-button">Import YAML<input type="file" accept=".yaml,.yml,text/yaml" @change="importFile" /></label><button @click="download">Export YAML</button></div><div class="controls"><button :disabled="!workspace.canOperate" @click="run">Run on selected device</button><button class="primary" :disabled="editor.saving.value || !workspace.canOperate" @click="save">Save scenario</button></div></div>
  </section>
</template>
