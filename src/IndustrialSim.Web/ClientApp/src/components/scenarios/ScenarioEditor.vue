<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { platformApi } from '../../api'
import { useScenarioEditor, type ScenarioEditorStep } from '../../composables/useScenarioEditor'
import { useWorkspaceStore } from '../../stores/workspace'
import type { DeviceDataPointRequest, DeviceDetails, DeviceSummary, ScenarioCatalogItem, ScalarValue } from '../../types'

const props = defineProps<{ value?: ScenarioCatalogItem | null; devices: DeviceSummary[] }>()
const emit = defineEmits<{ saved: []; cancelled: [] }>()
const editor = useScenarioEditor(platformApi)
const workspace = useWorkspaceStore()
const message = ref('')
const referenceDeviceId = ref('')
const runDeviceId = ref('')
const referenceDetails = ref<DeviceDetails | null>(null)
const loadingCapabilities = ref(false)
const yaml = computed(() => { try { return editor.toYaml() } catch { return '' } })
const durationUnits = ['ms', 's', 'm', 'h'] as const
const numericTypes = new Set(['Int8', 'Int16', 'Int32', 'Int64', 'UInt8', 'UInt16', 'UInt32', 'UInt64', 'Float', 'Double'])
const conditionTypes = new Set([...numericTypes, 'Boolean'])
const faultTypes = {
  data: ['Stale', 'Freeze', 'OutOfRange', 'Noise', 'Spike'],
  device: ['SensorFailure', 'Overheat', 'PowerLoss', 'EmergencyStop'],
  network: ['Disconnect', 'Timeout', 'Latency'],
} as const

const dataPoints = computed(() => referenceDetails.value?.definition.dataPoints ?? [])
const numericPoints = computed(() => dataPoints.value.filter(point => numericTypes.has(point.dataType)))
const conditionPoints = computed(() => dataPoints.value.filter(point => conditionTypes.has(point.dataType)))
const commands = computed(() => referenceDetails.value?.definition.commands ?? [])
const protocols = computed(() => referenceDetails.value?.definition.portBindings.map(binding => binding.protocol) ?? [])
const compatibleRunDevices = computed(() => props.devices.filter(device => !editor.targetType.value || device.deviceType.toLowerCase() === editor.targetType.value.toLowerCase()))

function selectedPoint(step: ScenarioEditorStep) {
  return dataPoints.value.find(point => point.name === step.dataPoint)
}

function selectedConditionPoint(step: ScenarioEditorStep) {
  return conditionPoints.value.find(point => point.name === step.conditionDataPoint)
}

function scalarDefault(point?: DeviceDataPointRequest): ScalarValue {
  if (!point) return 0
  if (point.dataType === 'Boolean') return false
  if (numericTypes.has(point.dataType)) return Number(point.initial ?? 0)
  return String(point.initial ?? '')
}

function normalizeStepCapabilities(step: ScenarioEditorStep) {
  if (step.actionType === 'ramp') {
    if (!numericPoints.value.some(point => point.name === step.dataPoint)) step.dataPoint = numericPoints.value[0]?.name ?? ''
  } else if (step.actionType === 'set') {
    if (!dataPoints.value.some(point => point.name === step.dataPoint)) {
      step.dataPoint = dataPoints.value[0]?.name ?? ''
      step.value = scalarDefault(selectedPoint(step))
    } else {
      const point = selectedPoint(step)
      if (point?.dataType === 'Boolean' && typeof step.value !== 'boolean') step.value = String(step.value).toLowerCase() === 'true'
      else if (point && numericTypes.has(point.dataType) && typeof step.value !== 'number') step.value = Number(step.value) || 0
      else if (point?.dataType === 'String' && typeof step.value !== 'string') step.value = String(step.value ?? '')
    }
  } else if (step.actionType === 'command') {
    if (!commands.value.includes(step.command)) step.command = commands.value[0] ?? ''
  } else if (step.actionType === 'fault') {
    if (!faultTypes[step.faultCategory].includes(step.faultType as never)) step.faultType = faultTypes[step.faultCategory][0]
    if (step.faultCategory === 'data' && !dataPoints.value.some(point => point.name === step.dataPoint)) step.dataPoint = dataPoints.value[0]?.name ?? ''
    if (step.faultCategory === 'network' && !protocols.value.includes(step.protocol)) step.protocol = protocols.value[0] ?? ''
  }
  if (step.triggerType === 'when') {
    if (!conditionPoints.value.some(point => point.name === step.conditionDataPoint)) step.conditionDataPoint = conditionPoints.value[0]?.name ?? ''
    normalizeCondition(step)
  }
}

function normalizeCondition(step: ScenarioEditorStep) {
  const point = selectedConditionPoint(step)
  if (point?.dataType === 'Boolean') {
    step.conditionOperator = '=='
    if (typeof step.conditionValue !== 'boolean') step.conditionValue = false
  } else if (typeof step.conditionValue !== 'number') step.conditionValue = Number(step.conditionValue) || 0
}

function changeDataPoint(step: ScenarioEditorStep) {
  step.value = scalarDefault(selectedPoint(step))
}

async function loadReference(deviceId: string, updateTarget = true) {
  referenceDeviceId.value = deviceId
  referenceDetails.value = null
  if (!deviceId) return
  loadingCapabilities.value = true
  try {
    referenceDetails.value = await platformApi.device(deviceId)
    if (updateTarget) editor.targetType.value = referenceDetails.value.definition.type
    editor.steps.forEach(normalizeStepCapabilities)
    if (!compatibleRunDevices.value.some(device => device.deviceId === runDeviceId.value)) runDeviceId.value = compatibleRunDevices.value[0]?.deviceId ?? ''
  } catch (cause) {
    message.value = cause instanceof Error ? cause.message : String(cause)
  } finally {
    loadingCapabilities.value = false
  }
}

function initialReference() {
  const legacyDevice = editor.steps.find(step => step.device)?.device
  return props.devices.find(device => device.deviceId === legacyDevice)?.deviceId
    ?? props.devices.find(device => device.deviceId === workspace.selectedDeviceId && (!editor.targetType.value || device.deviceType.toLowerCase() === editor.targetType.value.toLowerCase()))?.deviceId
    ?? props.devices.find(device => !editor.targetType.value || device.deviceType.toLowerCase() === editor.targetType.value.toLowerCase())?.deviceId
    ?? ''
}

watch(() => props.value, async value => {
  message.value = ''
  if (value) editor.load(value)
  else {
    editor.id.value = ''
    editor.name.value = ''
    editor.targetType.value = ''
    editor.version.value = 0
    editor.steps.splice(0)
    editor.addStep('set')
  }
  const reference = initialReference()
  await loadReference(reference, true)
  runDeviceId.value = compatibleRunDevices.value.find(device => device.deviceId === workspace.selectedDeviceId)?.deviceId ?? compatibleRunDevices.value[0]?.deviceId ?? ''
}, { immediate: true })

async function save() {
  try { await editor.save(); workspace.notify(`Scenario ${editor.id.value} saved`); emit('saved') }
  catch (cause) { message.value = cause instanceof Error ? cause.message : String(cause) }
}

async function run() {
  const deviceId = runDeviceId.value || compatibleRunDevices.value[0]?.deviceId
  if (!deviceId) { message.value = 'Create a compatible device first.'; return }
  try { await editor.run(deviceId); workspace.selectedDeviceId = deviceId; workspace.notify(`Scenario started on ${deviceId}`) }
  catch (cause) { message.value = cause instanceof Error ? cause.message : String(cause) }
}

function download() {
  const blob = new Blob([yaml.value], { type: 'application/yaml' }); const url = URL.createObjectURL(blob)
  const link = document.createElement('a'); link.href = url; link.download = `${editor.id.value || 'scenario'}.yaml`; link.click(); URL.revokeObjectURL(url)
}

async function importFile(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return
  try {
    editor.importYaml(await file.text())
    if (!editor.id.value) editor.id.value = file.name.replace(/\.ya?ml$/i, '')
    await loadReference(initialReference(), true)
  } catch (cause) { message.value = cause instanceof Error ? cause.message : String(cause) }
}
</script>

<template>
  <section class="editor-surface scenario-editor" aria-label="Graphical scenario editor">
    <header class="editor-head"><div><span class="eyebrow">Scenario flow</span><h2>{{ props.value ? props.value.name : 'New reusable scenario' }}</h2><p>Choose one reference device for authoring. Only its type and required capabilities are saved; the actual device is bound when the scenario runs.</p></div><button @click="emit('cancelled')">Close</button></header>
    <div v-if="message || editor.errors.value.length" class="inline-error" role="alert">{{ message || editor.errors.value.join(' ') }}</div>
    <div class="form-grid three scenario-identity">
      <div><label>Scenario ID</label><input v-model="editor.id.value" :readonly="Boolean(props.value)" /></div>
      <div><label>Name</label><input v-model="editor.name.value" /></div>
      <div><label>Revision</label><input :value="editor.version.value" readonly /></div>
      <div><label>Reference device</label><select v-model="referenceDeviceId" aria-label="Reference device" @change="loadReference(referenceDeviceId, true)"><option value="">Select device</option><option v-for="device in props.devices" :key="device.deviceId" :value="device.deviceId">{{ device.deviceId }} · {{ device.deviceType }}</option></select></div>
      <div><label>Reusable target type</label><input :value="editor.targetType.value" readonly placeholder="Derived from reference device" /></div>
      <div class="capability-summary"><label>Available capabilities</label><span>{{ loadingCapabilities ? 'Loading…' : `${dataPoints.length} datapoints · ${commands.length} commands · ${protocols.length} protocols` }}</span></div>
    </div>
    <div class="scenario-workbench">
      <div class="flow-canvas">
        <div class="flow-toolbar"><strong>Execution flow</strong><div class="controls"><button @click="editor.addStep('set')">+ Set</button><button @click="editor.addStep('ramp')">+ Ramp</button><button @click="editor.addStep('command')">+ Command</button><button @click="editor.addStep('wait')">+ Wait</button><button @click="editor.addStep('fault')">+ Fault</button></div></div>
        <div class="flow-rail">
          <article v-for="(step, index) in editor.steps" :key="step.id" class="flow-card">
            <div class="flow-index"><span>{{ String(index + 1).padStart(2, '0') }}</span><i></i></div>
            <div class="flow-card-body">
              <header><div><span class="type-badge">{{ step.actionType }}</span><strong>{{ step.id }}</strong></div><div class="controls"><button class="icon-button" :disabled="index === 0" @click="editor.moveStep(index, -1)">↑</button><button class="icon-button" :disabled="index === editor.steps.length - 1" @click="editor.moveStep(index, 1)">↓</button><button class="icon-button danger" @click="editor.removeStep(index)">×</button></div></header>
              <div class="form-grid three">
                <div><label>Trigger</label><select v-model="step.triggerType" @change="normalizeStepCapabilities(step)"><option>at</option><option>after</option><option>every</option><option>when</option></select></div>
                <template v-if="step.triggerType === 'when'">
                  <div><label>Condition datapoint</label><select v-model="step.conditionDataPoint" @change="normalizeCondition(step)"><option value="">Select datapoint</option><option v-for="point in conditionPoints" :key="point.name" :value="point.name">{{ point.name }} · {{ point.dataType }}</option></select></div>
                  <div><label>Operator</label><select v-model="step.conditionOperator" :disabled="selectedConditionPoint(step)?.dataType === 'Boolean'"><option>==</option><option v-if="selectedConditionPoint(step)?.dataType !== 'Boolean'">&gt;</option><option v-if="selectedConditionPoint(step)?.dataType !== 'Boolean'">&lt;</option></select></div>
                  <div><label>Condition value</label><select v-if="selectedConditionPoint(step)?.dataType === 'Boolean'" v-model="step.conditionValue"><option :value="true">true</option><option :value="false">false</option></select><input v-else v-model.number="step.conditionValue" type="number" step="any" /></div>
                </template>
                <div v-else><label>Time</label><div class="duration-field"><input v-model.number="step.triggerAmount" type="number" :min="step.triggerType === 'every' ? 0.001 : 0" step="any" /><select v-model="step.triggerUnit"><option v-for="unit in durationUnits" :key="unit">{{ unit }}</option></select></div></div>
                <div><label>Action</label><select v-model="step.actionType" @change="normalizeStepCapabilities(step)"><option>set</option><option>ramp</option><option>command</option><option>wait</option><option>fault</option></select></div>

                <template v-if="step.actionType === 'set'">
                  <div><label>Datapoint</label><select v-model="step.dataPoint" @change="changeDataPoint(step)"><option value="">Select datapoint</option><option v-for="point in dataPoints" :key="point.name" :value="point.name">{{ point.name }} · {{ point.dataType }}</option></select></div>
                  <div><label>Value</label><select v-if="selectedPoint(step)?.dataType === 'Boolean'" v-model="step.value"><option :value="true">true</option><option :value="false">false</option></select><input v-else-if="selectedPoint(step) && numericTypes.has(selectedPoint(step)!.dataType)" v-model.number="step.value" type="number" step="any" /><input v-else v-model="step.value" /></div>
                </template>
                <template v-else-if="step.actionType === 'ramp'">
                  <div><label>Numeric datapoint</label><select v-model="step.dataPoint"><option value="">Select datapoint</option><option v-for="point in numericPoints" :key="point.name" :value="point.name">{{ point.name }} · {{ point.dataType }}</option></select></div>
                  <div><label>From</label><input v-model.number="step.from" type="number" step="any" /></div><div><label>To</label><input v-model.number="step.to" type="number" step="any" /></div>
                  <div><label>Duration</label><div class="duration-field"><input v-model.number="step.durationAmount" type="number" min="0.001" step="any" /><select v-model="step.durationUnit"><option v-for="unit in durationUnits" :key="unit">{{ unit }}</option></select></div></div>
                </template>
                <template v-else-if="step.actionType === 'command'">
                  <div><label>Command</label><select v-model="step.command"><option value="">{{ commands.length ? 'Select command' : 'No commands defined' }}</option><option v-for="command in commands" :key="command">{{ command }}</option></select></div>
                </template>
                <template v-else-if="step.actionType === 'wait'">
                  <div><label>Duration</label><div class="duration-field"><input v-model.number="step.durationAmount" type="number" min="0" step="any" /><select v-model="step.durationUnit"><option v-for="unit in durationUnits" :key="unit">{{ unit }}</option></select></div></div>
                </template>
                <template v-else>
                  <div><label>Fault category</label><select v-model="step.faultCategory" @change="normalizeStepCapabilities(step)"><option value="data">Data</option><option value="device">Device</option><option value="network">Network</option></select></div>
                  <div><label>Fault type</label><select v-model="step.faultType"><option v-for="type in faultTypes[step.faultCategory]" :key="type">{{ type }}</option></select></div>
                  <div v-if="step.faultCategory === 'data'"><label>Datapoint</label><select v-model="step.dataPoint"><option value="">Select datapoint</option><option v-for="point in dataPoints" :key="point.name" :value="point.name">{{ point.name }}</option></select></div>
                  <div v-if="step.faultCategory === 'network'"><label>Protocol</label><select v-model="step.protocol"><option value="">Select protocol</option><option v-for="protocol in protocols" :key="protocol">{{ protocol }}</option></select></div>
                  <div><label>Duration</label><div class="duration-field"><input v-model.number="step.durationAmount" type="number" min="0" step="any" /><select v-model="step.durationUnit"><option v-for="unit in durationUnits" :key="unit">{{ unit }}</option></select></div></div>
                </template>
              </div>
            </div>
          </article>
        </div>
      </div>
      <aside class="yaml-preview"><div class="flow-toolbar"><strong>Generated YAML</strong><span>Read-only preview</span></div><pre>{{ yaml }}</pre></aside>
    </div>
    <div class="editor-footer"><div class="controls"><label class="file-button">Import YAML<input type="file" accept=".yaml,.yml,text/yaml" @change="importFile" /></label><button @click="download">Export YAML</button></div><div class="run-target"><label>Run target</label><select v-model="runDeviceId" aria-label="Run target"><option value="">Select compatible device</option><option v-for="device in compatibleRunDevices" :key="device.deviceId" :value="device.deviceId">{{ device.deviceId }}</option></select></div><div class="controls"><button :disabled="!workspace.canOperate || !runDeviceId" @click="run">Run scenario</button><button class="primary" :disabled="editor.saving.value || !workspace.canOperate" @click="save">Save scenario</button></div></div>
  </section>
</template>
