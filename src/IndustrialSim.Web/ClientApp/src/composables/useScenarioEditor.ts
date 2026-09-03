import { reactive, ref } from 'vue'
import { parse, stringify } from 'yaml'

export type ScenarioActionType = 'set' | 'command' | 'fault'
export type ScenarioTriggerType = 'at' | 'after' | 'every' | 'when'

export interface ScenarioEditorStep {
  id: string
  triggerType: ScenarioTriggerType
  triggerValue: string
  actionType: ScenarioActionType
  device: string
  dataPoint: string
  value: string | number | boolean | null
  command: string
  faultType: string
  protocol: string
  duration: string
}

interface ScenarioEditorApi {
  saveScenario(id: string, value: { name: string; yaml: string; version: number; editorJson: string }): Promise<{ version?: number }>
  runScenario(deviceId: string, scenarioId: string): Promise<unknown>
}

let sequence = 0
const step = (actionType: ScenarioActionType): ScenarioEditorStep => ({
  id: `step-${++sequence}`, triggerType: 'at', triggerValue: '0s', actionType,
  device: '', dataPoint: '', value: 0, command: '', faultType: 'spike', protocol: '', duration: '',
})

export function useScenarioEditor(api: ScenarioEditorApi) {
  const id = ref('')
  const name = ref('')
  const version = ref(0)
  const steps = reactive<ScenarioEditorStep[]>([])
  const errors = ref<string[]>([])
  const saving = ref(false)

  const addStep = (actionType: ScenarioActionType = 'set') => steps.push(step(actionType))
  const removeStep = (index: number) => steps.splice(index, 1)
  function moveStep(index: number, delta: number) {
    const target = index + delta
    if (target < 0 || target >= steps.length) return
    const [item] = steps.splice(index, 1)
    steps.splice(target, 0, item)
  }

  function action(item: ScenarioEditorStep) {
    if (item.actionType === 'set') return { set: { device: item.device, datapoint: item.dataPoint, value: item.value } }
    if (item.actionType === 'command') return { command: { device: item.device, name: item.command } }
    return { fault: { device: item.device || undefined, protocol: item.protocol || undefined, type: item.faultType, duration: item.duration || undefined } }
  }

  function toYaml() {
    const document = {
      scenario: {
        name: name.value || id.value,
        steps: steps.map(item => ({
          [item.triggerType]: item.triggerType === 'when'
            ? { device: item.device, condition: item.triggerValue }
            : item.triggerValue,
          ...action(item),
        })),
      },
    }
    return stringify(document, { lineWidth: 0 })
  }

  function importYaml(yaml: string) {
    const document = parse(yaml) as { scenario?: { name?: string; steps?: Array<Record<string, unknown>> } }
    if (!document?.scenario || !Array.isArray(document.scenario.steps)) throw new Error('Scenario YAML requires scenario.steps.')
    name.value = document.scenario.name ?? name.value
    steps.splice(0)
    for (const raw of document.scenario.steps) {
      const triggerType = (['at', 'after', 'every', 'when'] as const).find(key => raw[key] !== undefined)
      const actionType = (['set', 'command', 'fault'] as const).find(key => raw[key] !== undefined)
      if (!triggerType || !actionType) throw new Error('Each visual step needs one supported trigger and action.')
      const next = step(actionType)
      next.triggerType = triggerType
      const trigger = raw[triggerType] as string | { device?: string; condition?: string }
      if (triggerType === 'when' && typeof trigger === 'object') { next.device = trigger.device ?? ''; next.triggerValue = trigger.condition ?? '' }
      else next.triggerValue = String(trigger)
      const value = raw[actionType] as Record<string, unknown>
      next.device = String(value.device ?? next.device)
      if (actionType === 'set') { next.dataPoint = String(value.datapoint ?? ''); next.value = value.value as ScenarioEditorStep['value'] }
      if (actionType === 'command') next.command = String(value.name ?? '')
      if (actionType === 'fault') { next.faultType = String(value.type ?? ''); next.protocol = String(value.protocol ?? ''); next.duration = String(value.duration ?? '') }
      steps.push(next)
    }
  }

  function load(value: { id: string; name: string; yaml: string; version: number; editorJson?: string }) {
    id.value = value.id; name.value = value.name; version.value = value.version
    try {
      const editor = JSON.parse(value.editorJson || '{}') as { steps?: ScenarioEditorStep[] }
      if (editor.steps?.length) { steps.splice(0, steps.length, ...editor.steps); return }
    } catch { /* fall back to the public YAML document */ }
    importYaml(value.yaml)
  }

  function validate() {
    const next: string[] = []
    if (!id.value.trim()) next.push('Scenario ID is required.')
    if (!name.value.trim()) next.push('Scenario name is required.')
    if (!steps.length) next.push('Add at least one scenario step.')
    for (const item of steps) {
      if (!item.triggerValue.trim()) next.push('Every step requires a trigger value.')
      if (item.actionType !== 'fault' && !item.device.trim()) next.push('Set and command actions require a device.')
      if (item.actionType === 'set' && !item.dataPoint.trim()) next.push('Set actions require a datapoint.')
      if (item.actionType === 'command' && !item.command.trim()) next.push('Command actions require a command name.')
      if (item.actionType === 'fault' && !item.device.trim() && !item.protocol.trim()) next.push('Fault actions require a device or protocol target.')
    }
    errors.value = [...new Set(next)]
    return errors.value
  }

  async function save() {
    if (validate().length) throw new Error(errors.value[0])
    saving.value = true
    try {
      const result = await api.saveScenario(id.value, { name: name.value, yaml: toYaml(), version: version.value, editorJson: JSON.stringify({ steps }) })
      if (typeof result.version === 'number') version.value = result.version
    } finally { saving.value = false }
  }

  const run = (deviceId: string) => api.runScenario(deviceId, id.value)
  return { id, name, version, steps, errors, saving, addStep, removeStep, moveStep, toYaml, importYaml, load, validate, save, run }
}
