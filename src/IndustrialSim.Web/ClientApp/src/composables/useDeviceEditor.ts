import { reactive } from 'vue'
import type { DeviceCreateRequest, ScalarValue } from '../types'

export interface EditableDataPoint {
  name: string
  dataType: string
  access: string
  initialText: string
  unit: string
  description: string
}

const newDataPoint = (): EditableDataPoint => ({ name: '', dataType: 'Double', access: 'ReadWrite', initialText: '0', unit: '', description: '' })

export function useDeviceEditor() {
  const form = reactive({
    id: '', type: 'custom', deterministic: true, seed: 1,
    dataPoints: [newDataPoint()],
    portBindings: [] as Array<{ protocol: string; port: number }>,
  })

  function addDataPoint() { form.dataPoints.push(newDataPoint()) }
  function removeDataPoint(index: number) { if (form.dataPoints.length > 1) form.dataPoints.splice(index, 1) }
  function addBinding() { form.portBindings.push({ protocol: 'opcua', port: 4840 }) }
  function removeBinding(index: number) { form.portBindings.splice(index, 1) }

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
    if (form.portBindings.some(item => item.port < 1 || item.port > 65535)) throw new Error('Protocol ports must be between 1 and 65535.')
    if (new Set(form.portBindings.map(item => item.port)).size !== form.portBindings.length) throw new Error('Protocol ports must be unique.')
    return {
      id: form.id.trim(), type: form.type.trim(), deterministic: form.deterministic, seed: Number(form.seed),
      dataPoints: form.dataPoints.map(item => ({
        name: item.name.trim(), dataType: item.dataType, access: item.access, initial: scalar(item),
        unit: item.unit.trim() || null, description: item.description.trim() || null,
      })),
      portBindings: form.portBindings.map(item => ({ protocol: item.protocol, port: Number(item.port) })),
    }
  }

  return { form, addDataPoint, removeDataPoint, addBinding, removeBinding, toRequest }
}
