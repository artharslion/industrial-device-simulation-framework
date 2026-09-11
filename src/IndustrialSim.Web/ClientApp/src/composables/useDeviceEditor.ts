import { reactive } from 'vue'
import type { BuiltInDeviceProfile, DeviceCreateRequest, ModbusMappingRequest, ScalarValue } from '../types'

export interface EditableDataPoint {
  name: string
  dataType: string
  access: string
  initialText: string
  unit: string
  description: string
}

export interface EditableBehaviorParameter {
  name: string
  value: number
  minimum: number
  unit: string
  description: string
}

const newDataPoint = (): EditableDataPoint => ({ name: '', dataType: 'Double', access: 'ReadWrite', initialText: '0', unit: '', description: '' })

export function useDeviceEditor() {
  const form = reactive({
    id: '', type: 'custom', profile: 'custom', behaviorDescription: 'No built-in behavior. State changes only through writes, scenarios, commands, or faults.', deterministic: true, seed: 1,
    dataPoints: [newDataPoint()],
    commands: [] as string[],
    events: [] as string[],
    behaviorParameters: [] as EditableBehaviorParameter[],
    portBindings: [] as Array<{ protocol: string; port: number; mappings: ModbusMappingRequest[] }>,
  })

  function addDataPoint() { form.dataPoints.push(newDataPoint()) }
  function removeDataPoint(index: number) { if (form.dataPoints.length > 1) form.dataPoints.splice(index, 1) }
  function addBinding() { form.portBindings.push({ protocol: 'opcua', port: 4840, mappings: [] }) }
  function removeBinding(index: number) { form.portBindings.splice(index, 1) }
  function addModbusMapping(index: number) {
    form.portBindings[index]?.mappings.push({ dataPoint: form.dataPoints[0]?.name ?? '', kind: 'holding', address: 0, dataType: form.dataPoints[0]?.dataType ?? 'uint16', access: 'readwrite', byteOrder: 'big', wordOrder: 'big' })
  }
  function removeModbusMapping(bindingIndex: number, mappingIndex: number) { form.portBindings[bindingIndex]?.mappings.splice(mappingIndex, 1) }

  function applyProfile(profile: BuiltInDeviceProfile | null) {
    if (!profile) {
      form.profile = 'custom'
      form.type = 'custom'
      form.behaviorDescription = 'No built-in behavior. State changes only through writes, scenarios, commands, or faults.'
      form.dataPoints.splice(0, form.dataPoints.length, newDataPoint())
      form.commands.splice(0)
      form.events.splice(0)
      form.behaviorParameters.splice(0)
      return
    }
    form.profile = profile.name
    form.type = profile.name
    form.behaviorDescription = profile.description
    form.dataPoints.splice(0, form.dataPoints.length, ...profile.dataPoints.map(point => ({
      name: point.name,
      dataType: point.dataType,
      access: point.access,
      initialText: point.initial === null ? '' : String(point.initial),
      unit: point.unit ?? '',
      description: point.description ?? '',
    })))
    form.commands.splice(0, form.commands.length, ...profile.commands)
    form.events.splice(0, form.events.length, ...profile.events)
    form.behaviorParameters.splice(0, form.behaviorParameters.length, ...profile.parameters.map(parameter => ({
      name: parameter.name,
      value: parameter.defaultValue,
      minimum: parameter.minimum,
      unit: parameter.unit ?? '',
      description: parameter.description,
    })))
  }

  function load(value: DeviceCreateRequest) {
    form.id = value.id
    form.type = value.type
    form.profile = value.behavior?.profile && value.behavior.profile !== 'none' ? value.behavior.profile : 'custom'
    form.behaviorDescription = form.profile === 'custom'
      ? 'No built-in behavior. State changes only through writes, scenarios, commands, or faults.'
      : `${form.profile} built-in behavior`
    form.deterministic = value.deterministic
    form.seed = value.seed
    form.dataPoints.splice(0, form.dataPoints.length, ...value.dataPoints.map(point => ({
      name: point.name, dataType: point.dataType, access: point.access,
      initialText: point.initial === null ? '' : String(point.initial), unit: point.unit ?? '', description: point.description ?? '',
    })))
    form.commands.splice(0, form.commands.length, ...(value.commands ?? []))
    form.events.splice(0, form.events.length, ...(value.events ?? []))
    form.behaviorParameters.splice(0, form.behaviorParameters.length, ...Object.entries(value.behavior?.parameters ?? {}).map(([name, parameter]) => ({
      name, value: Number(parameter), minimum: 0, unit: '', description: '',
    })))
    const bindings: Array<{ protocol: string; port: number; mappings: ModbusMappingRequest[] }> = []
    if (value.protocols?.opcua?.enabled) {
      const endpointPort = value.protocols.opcua.endpoint ? Number(new URL(value.protocols.opcua.endpoint.replace('opc.tcp:', 'http:')).port) : 0
      bindings.push({ protocol: 'opcua', port: value.protocols.opcua.port ?? (endpointPort || 4840), mappings: [] })
    }
    if (value.protocols?.modbus?.enabled) bindings.push({ protocol: 'modbus', port: value.protocols.modbus.port, mappings: structuredClone(value.protocols.modbus.mappings ?? []) })
    if (!bindings.length) bindings.push(...value.portBindings.map(binding => ({ ...binding, mappings: [] })))
    form.portBindings.splice(0, form.portBindings.length, ...bindings)
  }

  function scalar(row: EditableDataPoint): ScalarValue {
    const value = row.initialText.trim()
    if (row.dataType.toLowerCase() === 'string') return value
    if (row.dataType.toLowerCase() === 'boolean') {
      if (!/^(true|false)$/i.test(value)) throw new Error(`${row.name || 'Datapoint'} initial value must be true or false.`)
      return value.toLowerCase() === 'true'
    }
    const number = Number(value)
    if (!Number.isFinite(number)) throw new Error(`${row.name || 'Datapoint'} initial value must be a number.`)
    return number
  }

  function toRequest(): DeviceCreateRequest {
    if (!form.id.trim()) throw new Error('Device ID is required.')
    if (!form.type.trim()) throw new Error('Device type is required.')
    const names = form.dataPoints.map(item => item.name.trim().toLowerCase())
    if (names.some(name => !name)) throw new Error('Every datapoint needs a name.')
    if (new Set(names).size !== names.length) throw new Error('Datapoint names must be unique.')
    const invalidParameter = form.behaviorParameters.find(parameter => !Number.isFinite(Number(parameter.value)) || Number(parameter.value) < parameter.minimum)
    if (invalidParameter) throw new Error(`${invalidParameter.name} must be at least ${invalidParameter.minimum}.`)
    if (form.portBindings.some(item => item.port < 1 || item.port > 65535)) throw new Error('Protocol ports must be between 1 and 65535.')
    if (new Set(form.portBindings.map(item => item.port)).size !== form.portBindings.length) throw new Error('Protocol ports must be unique.')
    if (new Set(form.portBindings.map(item => item.protocol)).size !== form.portBindings.length) throw new Error('Only one configuration per protocol is allowed.')
    const modbus = form.portBindings.find(item => item.protocol === 'modbus')
    if (modbus && modbus.mappings.length === 0) throw new Error('Modbus requires at least one explicit mapping.')
    const opcua = form.portBindings.find(item => item.protocol === 'opcua')
    return {
      id: form.id.trim(), type: form.type.trim(), deterministic: form.deterministic, seed: Number(form.seed),
      dataPoints: form.dataPoints.map(item => ({
        name: item.name.trim(), dataType: item.dataType, access: item.access, initial: scalar(item),
        unit: item.unit.trim() || null, description: item.description.trim() || null,
      })),
      commands: [...form.commands],
      events: [...form.events],
      behavior: form.profile === 'custom'
        ? { profile: 'none', parameters: {} }
        : { profile: form.profile, parameters: Object.fromEntries(form.behaviorParameters.map(parameter => [parameter.name, Number(parameter.value)])) },
      protocols: {
        opcua: opcua ? { enabled: true, port: Number(opcua.port) } : null,
        modbus: modbus ? { enabled: true, port: Number(modbus.port), mappings: modbus.mappings.map(mapping => ({ ...mapping })) } : null,
      },
      portBindings: [],
    }
  }

  return { form, addDataPoint, removeDataPoint, addBinding, removeBinding, addModbusMapping, removeModbusMapping, applyProfile, load, toRequest }
}
