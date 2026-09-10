<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ApiProblemError, platformApi } from '../../api'
import { useDeviceEditor } from '../../composables/useDeviceEditor'
import type { BuiltInDeviceProfile, DeviceSummary } from '../../types'

const emit = defineEmits<{ created: [device: DeviceSummary]; cancelled: [] }>()
const editor = useDeviceEditor()
const busy = ref(false)
const error = ref('')
const profiles = ref<BuiltInDeviceProfile[]>([])
const selectedProfile = computed(() => profiles.value.find(profile => profile.name === editor.form.profile) ?? null)

onMounted(async () => {
  try { profiles.value = await platformApi.deviceProfiles() }
  catch (cause) { error.value = cause instanceof Error ? cause.message : String(cause) }
})

function changeProfile() {
  editor.applyProfile(selectedProfile.value)
}

async function submit() {
  error.value = ''
  try {
    const value = editor.toRequest()
    busy.value = true
    emit('created', await platformApi.createDevice(value))
  } catch (cause) {
    error.value = cause instanceof ApiProblemError
      ? `${cause.message}${cause.errorCode ? ` (${cause.errorCode})` : ''}`
      : cause instanceof Error ? cause.message : String(cause)
  } finally { busy.value = false }
}
</script>

<template>
  <section class="editor-surface device-create" aria-labelledby="new-device-title">
    <header class="editor-head"><div><span class="eyebrow">Quick create</span><h2 id="new-device-title">New simulation device</h2><p>Define logical datapoints first, then reserve optional protocol ports.</p></div><button @click="emit('cancelled')">Cancel</button></header>
    <div v-if="error" class="inline-error" role="alert">{{ error }}</div>
    <form @submit.prevent="submit">
      <section class="editor-section"><div class="section-title"><span>01</span><div><h3>Identity and runtime</h3><p>IDs are unique across active simulations.</p></div></div><div class="form-grid three">
        <div><label for="device-id">Device ID</label><input id="device-id" v-model="editor.form.id" required /></div>
        <div><label for="device-profile">Device profile</label><select id="device-profile" v-model="editor.form.profile" @change="changeProfile"><option value="custom">Custom</option><option v-for="profile in profiles" :key="profile.name" :value="profile.name">{{ profile.displayName }}</option></select></div>
        <div><label for="device-type">Device type</label><input id="device-type" v-model="editor.form.type" :readonly="editor.form.profile !== 'custom'" required /></div>
        <div><label for="device-seed">Seed</label><input id="device-seed" v-model.number="editor.form.seed" type="number" /></div>
        <div class="check-field wide"><label><input v-model="editor.form.deterministic" type="checkbox" /> Deterministic mode</label></div>
      </div></section>
      <section class="editor-section behavior-profile"><div class="section-title"><span>02</span><div><h3>Default behavior</h3><p>{{ editor.form.behaviorDescription }}</p></div></div>
        <div class="behavior-contract"><div><small>Commands</small><strong>{{ editor.form.commands.join(', ') || 'None' }}</strong></div><div><small>Events</small><strong>{{ editor.form.events.join(', ') || 'None' }}</strong></div></div>
        <div v-if="editor.form.behaviorParameters.length" class="form-grid three behavior-parameters"><div v-for="parameter in editor.form.behaviorParameters" :key="parameter.name"><label :for="`behavior-${parameter.name}`">{{ parameter.name }} <small>{{ parameter.unit }}</small></label><input :id="`behavior-${parameter.name}`" v-model.number="parameter.value" type="number" step="any" :min="parameter.minimum" /><small>{{ parameter.description }}</small></div></div>
        <div v-else class="resource-state compact">No behavior loop is attached. Custom devices change only through explicit runtime operations.</div>
      </section>
      <section class="editor-section"><div class="section-title"><span>03</span><div><h3>Datapoints</h3><p>{{ editor.form.profile === 'custom' ? 'Define the public state contract.' : 'Required by the selected built-in behavior; initial values and descriptions remain editable.' }}</p></div><button v-if="editor.form.profile === 'custom'" type="button" @click="editor.addDataPoint">Add datapoint</button></div>
        <div class="device-point-list"><article v-for="(point, index) in editor.form.dataPoints" :key="index" class="mapping-card"><div class="form-grid three">
          <div><label :for="`point-name-${index}`">Datapoint name</label><input :id="`point-name-${index}`" v-model="point.name" :readonly="editor.form.profile !== 'custom'" required /></div>
          <div><label>Data type</label><select v-model="point.dataType" :disabled="editor.form.profile !== 'custom'"><option v-for="type in ['Boolean','Int8','Int16','Int32','Int64','UInt8','UInt16','UInt32','UInt64','Float','Double','String']" :key="type">{{ type }}</option></select></div>
          <div><label>Access mode</label><select v-model="point.access" :disabled="editor.form.profile !== 'custom'"><option>Read</option><option>Write</option><option>ReadWrite</option></select></div>
          <div><label>Initial value</label><input v-model="point.initialText" /></div><div><label>Unit</label><input v-model="point.unit" /></div><div><label>Description</label><input v-model="point.description" /></div>
        </div><button v-if="editor.form.profile === 'custom'" type="button" class="danger compact-action" :disabled="editor.form.dataPoints.length === 1" @click="editor.removeDataPoint(index)">Remove datapoint</button></article></div>
      </section>
      <section class="editor-section"><div class="section-title"><span>04</span><div><h3>Protocol port bindings</h3><p>One logical device can reserve ports for multiple protocol adapters.</p></div><button type="button" @click="editor.addBinding">Add binding</button></div>
        <div v-if="editor.form.portBindings.length === 0" class="resource-state compact">No ports reserved. Add OPC UA or Modbus TCP when needed.</div>
        <div v-for="(binding, index) in editor.form.portBindings" :key="index" class="binding-row"><div><label>Protocol</label><select v-model="binding.protocol"><option value="opcua">OPC UA</option><option value="modbus">Modbus TCP</option></select></div><div><label>Port</label><input v-model.number="binding.port" type="number" min="1" max="65535" /></div><button type="button" class="danger" @click="editor.removeBinding(index)">Remove</button></div>
      </section>
      <footer class="editor-footer"><span class="hint">Server validation reports duplicate IDs, port conflicts, and invalid mappings.</span><button class="primary" type="submit" :disabled="busy">{{ busy ? 'Creating…' : 'Create device' }}</button></footer>
    </form>
  </section>
</template>
