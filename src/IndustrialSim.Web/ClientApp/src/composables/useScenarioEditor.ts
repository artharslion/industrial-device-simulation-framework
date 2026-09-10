import { reactive, ref } from 'vue'
import { parse, stringify } from 'yaml'

export type ScenarioActionType = 'set' | 'ramp' | 'command' | 'wait' | 'fault'
export type ScenarioTriggerType = 'at' | 'after' | 'every' | 'when'
export type DurationUnit = 'ms' | 's' | 'm' | 'h'
export type ScenarioFaultCategory = 'data' | 'device' | 'network'
export type ConditionOperator = '==' | '>' | '<'

export interface ScenarioEditorStep {
  id: string
  triggerType: ScenarioTriggerType
  triggerValue: string
  triggerAmount: number
  triggerUnit: DurationUnit
  conditionDataPoint: string
  conditionOperator: ConditionOperator
  conditionValue: string | number | boolean
  actionType: ScenarioActionType
  device: string
  dataPoint: string
  value: string | number | boolean | null
  command: string
  from: number
  to: number
  faultCategory: ScenarioFaultCategory
  faultType: string
  protocol: string
  duration: string
  durationAmount: number
  durationUnit: DurationUnit
}

interface ScenarioEditorApi {
  saveScenario(id: string, value: { name: string; yaml: string; version: number; editorJson: string }): Promise<{ version?: number }>
  runScenario(deviceId: string, scenarioId: string): Promise<unknown>
}

let sequence = 0
const durationUnits = new Set<DurationUnit>(['ms', 's', 'm', 'h'])
const durationPattern = /^\s*(-?(?:\d+(?:\.\d+)?|\.\d+))\s*(ms|s|m|h)\s*$/i
const conditionPattern = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(==|>|<)\s*(true|false|-?[0-9]+(?:\.[0-9]+)?)\s*$/i

function parseDurationParts(value: unknown, fallbackAmount = 0, fallbackUnit: DurationUnit = 's') {
  const text = String(value ?? '').trim()
  const match = durationPattern.exec(text)
  if (match) return { amount: Number(match[1]), unit: match[2].toLowerCase() as DurationUnit }
  const timeSpan = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2}(?:\.\d+)?)$/.exec(text)
  if (timeSpan) {
    const seconds = Number(timeSpan[1] ?? 0) * 86400 + Number(timeSpan[2]) * 3600 + Number(timeSpan[3]) * 60 + Number(timeSpan[4])
    return { amount: seconds, unit: 's' as DurationUnit }
  }
  return { amount: fallbackAmount, unit: fallbackUnit }
}

function durationText(amount: number, unit: DurationUnit) {
  return `${Number.isFinite(amount) ? amount : 0}${durationUnits.has(unit) ? unit : 's'}`
}

function parseCondition(value: unknown) {
  const match = conditionPattern.exec(String(value ?? ''))
  if (!match) return { dataPoint: '', operator: '==' as ConditionOperator, value: 0 }
  const literal = match[3]
  return {
    dataPoint: match[1],
    operator: match[2] as ConditionOperator,
    value: /^(true|false)$/i.test(literal) ? literal.toLowerCase() === 'true' : Number(literal),
  }
}

const step = (actionType: ScenarioActionType): ScenarioEditorStep => ({
  id: `step-${++sequence}`,
  triggerType: 'at',
  triggerValue: '0s',
  triggerAmount: 0,
  triggerUnit: 's',
  conditionDataPoint: '',
  conditionOperator: '==',
  conditionValue: 0,
  actionType,
  device: '',
  dataPoint: '',
  value: 0,
  command: '',
  from: 0,
  to: 100,
  faultCategory: 'device',
  faultType: 'Overheat',
  protocol: '',
  duration: '5s',
  durationAmount: 5,
  durationUnit: 's',
})

function normalizeStep(value: Partial<ScenarioEditorStep>): ScenarioEditorStep {
  const normalized = Object.assign(step(value.actionType ?? 'set'), value)
  const trigger = parseDurationParts(value.triggerValue, value.triggerAmount ?? 0, value.triggerUnit ?? 's')
  normalized.triggerAmount = value.triggerAmount ?? trigger.amount
  normalized.triggerUnit = value.triggerUnit ?? trigger.unit
  const duration = parseDurationParts(value.duration, value.durationAmount ?? 5, value.durationUnit ?? 's')
  normalized.durationAmount = value.durationAmount ?? duration.amount
  normalized.durationUnit = value.durationUnit ?? duration.unit
  if (normalized.triggerType === 'when' && !normalized.conditionDataPoint) {
    const condition = parseCondition(value.triggerValue)
    normalized.conditionDataPoint = condition.dataPoint
    normalized.conditionOperator = condition.operator
    normalized.conditionValue = condition.value
  }
  return normalized
}

export function useScenarioEditor(api: ScenarioEditorApi) {
  const id = ref('')
  const name = ref('')
  const targetType = ref('')
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

  function optionalDevice(item: ScenarioEditorStep) {
    return targetType.value ? {} : { device: item.device }
  }

  function action(item: ScenarioEditorStep) {
    if (item.actionType === 'set') return { set: { ...optionalDevice(item), datapoint: item.dataPoint, value: item.value } }
    if (item.actionType === 'ramp') return { ramp: { ...optionalDevice(item), datapoint: item.dataPoint, from: item.from, to: item.to, duration: durationText(item.durationAmount, item.durationUnit) } }
    if (item.actionType === 'command') return { command: { ...optionalDevice(item), name: item.command } }
    if (item.actionType === 'wait') return { wait: { duration: durationText(item.durationAmount, item.durationUnit) } }
    const target = item.faultCategory === 'data'
      ? { datapoint: item.dataPoint }
      : item.faultCategory === 'network'
        ? { protocol: item.protocol }
        : {}
    return { fault: { ...optionalDevice(item), ...target, type: item.faultType, duration: durationText(item.durationAmount, item.durationUnit) } }
  }

  function condition(item: ScenarioEditorStep) {
    return `${item.conditionDataPoint} ${item.conditionOperator} ${String(item.conditionValue)}`
  }

  function toYaml() {
    const document = {
      scenario: {
        name: name.value || id.value,
        ...(targetType.value ? { target: { type: targetType.value } } : {}),
        steps: steps.map(item => ({
          [item.triggerType]: item.triggerType === 'when'
            ? { ...optionalDevice(item), condition: condition(item) }
            : durationText(item.triggerAmount, item.triggerUnit),
          ...action(item),
        })),
      },
    }
    return stringify(document, { lineWidth: 0 })
  }

  function importYaml(yaml: string) {
    const document = parse(yaml) as { scenario?: { name?: string; target?: { type?: string }; steps?: Array<Record<string, unknown>> } }
    if (!document?.scenario || !Array.isArray(document.scenario.steps)) throw new Error('Scenario YAML requires scenario.steps.')
    name.value = document.scenario.name ?? name.value
    targetType.value = document.scenario.target?.type ?? ''
    steps.splice(0)
    for (const raw of document.scenario.steps) {
      const triggerType = (['at', 'after', 'every', 'when'] as const).find(key => raw[key] !== undefined)
      const actionType = (['set', 'ramp', 'command', 'wait', 'fault'] as const).find(key => raw[key] !== undefined)
      if (!triggerType || !actionType) throw new Error('Each visual step needs one supported trigger and action.')
      const next = step(actionType)
      next.triggerType = triggerType
      const trigger = raw[triggerType] as string | { device?: string; condition?: string }
      if (triggerType === 'when' && typeof trigger === 'object') {
        next.device = trigger.device ?? ''
        next.triggerValue = trigger.condition ?? ''
        const parsedCondition = parseCondition(trigger.condition)
        next.conditionDataPoint = parsedCondition.dataPoint
        next.conditionOperator = parsedCondition.operator
        next.conditionValue = parsedCondition.value
      } else {
        next.triggerValue = String(trigger)
        const parsedDuration = parseDurationParts(trigger)
        next.triggerAmount = parsedDuration.amount
        next.triggerUnit = parsedDuration.unit
      }
      const value = raw[actionType] as Record<string, unknown>
      next.device = String(value.device ?? next.device)
      if (actionType === 'set') { next.dataPoint = String(value.datapoint ?? ''); next.value = value.value as ScenarioEditorStep['value'] }
      if (actionType === 'ramp') { next.dataPoint = String(value.datapoint ?? ''); next.from = Number(value.from ?? 0); next.to = Number(value.to ?? 0) }
      if (actionType === 'command') next.command = String(value.name ?? '')
      if (actionType === 'fault') {
        next.dataPoint = String(value.datapoint ?? '')
        next.faultType = String(value.type ?? '')
        next.protocol = String(value.protocol ?? '')
        next.faultCategory = next.protocol ? 'network' : next.dataPoint ? 'data' : 'device'
      }
      if (['ramp', 'wait', 'fault'].includes(actionType)) {
        next.duration = String(value.duration ?? '5s')
        const parsedDuration = parseDurationParts(value.duration, 5)
        next.durationAmount = parsedDuration.amount
        next.durationUnit = parsedDuration.unit
      }
      steps.push(next)
    }
  }

  function load(value: { id: string; name: string; yaml: string; version: number; editorJson?: string }) {
    id.value = value.id
    name.value = value.name
    version.value = value.version
    importYaml(value.yaml)
    try {
      const editor = JSON.parse(value.editorJson || '{}') as { targetType?: string; steps?: Partial<ScenarioEditorStep>[] }
      if (editor.targetType && !targetType.value) targetType.value = editor.targetType
      if (editor.steps?.length === steps.length) steps.splice(0, steps.length, ...editor.steps.map(normalizeStep))
    } catch { /* executable YAML remains authoritative */ }
  }

  function validate() {
    const next: string[] = []
    if (!id.value.trim()) next.push('Scenario ID is required.')
    if (!name.value.trim()) next.push('Scenario name is required.')
    if (!targetType.value.trim()) next.push('Choose a reference device to define the reusable target type.')
    if (!steps.length) next.push('Add at least one scenario step.')
    for (const item of steps) {
      if (item.triggerType === 'when') {
        if (!item.conditionDataPoint.trim()) next.push('When triggers require a datapoint condition.')
      } else if (!Number.isFinite(item.triggerAmount) || item.triggerAmount < 0 || (item.triggerType === 'every' && item.triggerAmount <= 0)) {
        next.push(`${item.triggerType} requires ${item.triggerType === 'every' ? 'a positive' : 'a non-negative'} time value.`)
      }
      if (item.actionType === 'set' && !item.dataPoint.trim()) next.push('Set actions require a datapoint.')
      if (item.actionType === 'ramp' && (!item.dataPoint.trim() || !Number.isFinite(item.durationAmount) || item.durationAmount <= 0)) next.push('Ramp actions require a numeric datapoint and positive duration.')
      if (item.actionType === 'command' && !item.command.trim()) next.push('Command actions require a command name.')
      if (item.actionType === 'wait' && (!Number.isFinite(item.durationAmount) || item.durationAmount < 0)) next.push('Wait actions require a non-negative duration.')
      if (item.actionType === 'fault') {
        if (item.faultCategory === 'data' && !item.dataPoint.trim()) next.push('Data fault actions require a datapoint.')
        if (item.faultCategory === 'network' && !item.protocol.trim()) next.push('Network fault actions require a configured protocol.')
        if (!item.faultType.trim()) next.push('Fault actions require a fault type.')
        if (!Number.isFinite(item.durationAmount) || item.durationAmount < 0) next.push('Fault duration cannot be negative.')
      }
    }
    errors.value = [...new Set(next)]
    return errors.value
  }

  async function save() {
    if (validate().length) throw new Error(errors.value[0])
    saving.value = true
    try {
      const result = await api.saveScenario(id.value, { name: name.value, yaml: toYaml(), version: version.value, editorJson: JSON.stringify({ targetType: targetType.value, steps }) })
      if (typeof result.version === 'number') version.value = result.version
    } finally { saving.value = false }
  }

  const run = (deviceId: string) => api.runScenario(deviceId, id.value)
  return { id, name, targetType, version, steps, errors, saving, addStep, removeStep, moveStep, toYaml, importYaml, load, validate, save, run }
}
