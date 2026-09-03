import { reactive, ref } from 'vue'
import type { ProtocolMappingProfile, TemplatePackage } from '../types'

interface TemplateEditorApi {
  createTemplate(value: TemplatePackage): Promise<unknown>
  instantiateTemplate(id: string, version: string, value: unknown): Promise<unknown>
}

const emptyPackage = (): TemplatePackage => ({
  template: {
    id: '', version: '1.0.0', displayName: '', deviceType: 'Custom', description: '', tags: [],
    dataPoints: [], commands: [], events: [], behaviorJson: '{}',
  },
  mappings: [],
})

export function useTemplateEditor(api: TemplateEditorApi) {
  const draft = reactive<TemplatePackage>(emptyPackage())
  const errors = ref<string[]>([])
  const saving = ref(false)

  function reset(value?: TemplatePackage) {
    const next = structuredClone(value ?? emptyPackage())
    Object.assign(draft.template, next.template)
    draft.mappings.splice(0, draft.mappings.length, ...next.mappings)
    errors.value = []
  }

  function addDataPoint() {
    draft.template.dataPoints.push({ name: '', dataType: 'Double', access: 'ReadWrite', initial: 0, unit: '', description: '' })
  }

  function removeDataPoint(index: number) { draft.template.dataPoints.splice(index, 1) }

  function addMapping(protocol = 'modbus') {
    const mapping: ProtocolMappingProfile = {
      templateId: draft.template.id,
      templateVersion: draft.template.version,
      protocol,
      name: protocol === 'opcua' ? 'nodes' : 'registers',
      entries: [{ dataPoint: draft.template.dataPoints[0]?.name ?? '', address: '', dataType: null, byteOrder: 'BigEndian', wordOrder: 'HighLow' }],
    }
    draft.mappings.push(mapping)
  }

  function removeMapping(index: number) { draft.mappings.splice(index, 1) }
  function addMappingEntry(mapping: ProtocolMappingProfile) { mapping.entries.push({ dataPoint: draft.template.dataPoints[0]?.name ?? '', address: '', byteOrder: 'BigEndian', wordOrder: 'HighLow' }) }
  function removeMappingEntry(mapping: ProtocolMappingProfile, index: number) { mapping.entries.splice(index, 1) }

  function validate() {
    const next: string[] = []
    if (!/^[a-z0-9][a-z0-9.-]*$/.test(draft.template.id)) next.push('Template ID must use lowercase letters, numbers, dots, or hyphens.')
    if (!/^\d+\.\d+\.\d+$/.test(draft.template.version)) next.push('Version must use semantic version format, for example 1.0.0.')
    if (!draft.template.displayName.trim()) next.push('Display name is required.')
    if (!draft.template.deviceType.trim()) next.push('Device type is required.')
    if (draft.template.dataPoints.length === 0) next.push('Add at least one datapoint.')
    const names = draft.template.dataPoints.map(point => point.name.trim().toLowerCase())
    if (names.some(name => !name)) next.push('Every datapoint requires a name.')
    if (new Set(names).size !== names.length) next.push('Datapoint names must be unique.')
    for (const mapping of draft.mappings) {
      mapping.templateId = draft.template.id
      mapping.templateVersion = draft.template.version
      if (mapping.entries.some(entry => !names.includes(entry.dataPoint.toLowerCase()) || !entry.address.trim()))
        next.push(`Mapping ${mapping.name || mapping.protocol} has an unknown datapoint or empty address.`)
    }
    errors.value = next
    return next
  }

  async function save() {
    if (validate().length) throw new Error(errors.value[0])
    saving.value = true
    try { await api.createTemplate(draft) } finally { saving.value = false }
  }

  async function instantiate(value: { deviceId: string; deterministic: boolean; seed: number; portBindings: Array<{ protocol: string; port: number }> }) {
    if (!draft.template.id || !draft.template.version) throw new Error('Save or select a template version before instantiation.')
    await api.instantiateTemplate(draft.template.id, draft.template.version, value)
  }

  function importJson(value: string) { reset(JSON.parse(value) as TemplatePackage) }
  const exportJson = () => JSON.stringify(draft, null, 2)

  return { draft, errors, saving, reset, addDataPoint, removeDataPoint, addMapping, removeMapping, addMappingEntry, removeMappingEntry, validate, save, instantiate, importJson, exportJson }
}
