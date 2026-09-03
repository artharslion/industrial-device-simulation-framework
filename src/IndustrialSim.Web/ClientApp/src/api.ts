import type { InjectionKey } from 'vue'
import type { FaultRequest, ProtocolStatus, RuntimeSnapshot } from './types'

interface ProblemDetails { title?: string; detail?: string; errorCode?: string; error?: unknown }
interface DeviceSummary { deviceId: string }
interface ProtocolSummary {
  deviceId: string
  configured: Array<{ name: string; running: boolean }>
  reserved: Array<{ name: string; port: number }>
}

export class ApiProblemError extends Error {
  constructor(message: string, public readonly status: number, public readonly errorCode?: string) {
    super(message)
    this.name = 'ApiProblemError'
  }
}

async function request<T>(url: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(url, options)
  const text = await response.text()
  let body: unknown = undefined
  if (text) {
    try { body = JSON.parse(text) } catch { body = text }
  }
  if (!response.ok) {
    const problem = typeof body === 'object' && body !== null ? body as ProblemDetails : undefined
    const message = problem?.detail
      ?? (problem?.error === undefined ? undefined : String(problem.error))
      ?? (typeof body === 'string' && body ? body : `${response.status} ${response.statusText}`)
    throw new ApiProblemError(message, response.status, problem?.errorCode)
  }
  return body as T
}

export interface DeveloperConsoleApi {
  getSnapshot(): Promise<RuntimeSnapshot>
  runRuntimeCommand(command: 'start' | 'pause' | 'stop' | 'reset'): Promise<void>
  tick(seconds: number): Promise<void>
  runScenario(yaml: string): Promise<void>
  stopScenario(): Promise<void>
  activateFault(fault: FaultRequest): Promise<void>
  recoverFault(id: string): Promise<void>
}

let activeDeviceId: string | undefined

async function getV1Snapshot(): Promise<RuntimeSnapshot> {
  const devices = await request<DeviceSummary[]>('/api/v1/devices')
  const deviceId = devices[0]?.deviceId
  if (!deviceId) throw new ApiProblemError('No simulations are registered.', 404, 'simulationNotFound')
  activeDeviceId = deviceId
  const [state, runtime, protocolRows, events, faults] = await Promise.all([
    request<RuntimeSnapshot['state']>(`/api/v1/devices/${encodeURIComponent(deviceId)}/state`),
    request<RuntimeSnapshot['runtime']>(`/api/v1/devices/${encodeURIComponent(deviceId)}/runtime`),
    request<ProtocolSummary[]>('/api/v1/protocols'),
    request<RuntimeSnapshot['events']>(`/api/v1/devices/${encodeURIComponent(deviceId)}/events`),
    request<RuntimeSnapshot['faults']>(`/api/v1/devices/${encodeURIComponent(deviceId)}/faults`),
  ])
  const row = protocolRows.find(item => item.deviceId === deviceId)
  const protocols: ProtocolStatus = { opcua: false, modbus: false }
  for (const protocol of row?.configured ?? []) {
    if (protocol.name.toLowerCase() === 'opcua') protocols.opcua = protocol.running
    if (protocol.name.toLowerCase() === 'modbus') protocols.modbus = protocol.running
  }
  for (const protocol of row?.reserved ?? []) {
    if (protocol.name.toLowerCase() === 'opcua') protocols.opcua = true
    if (protocol.name.toLowerCase() === 'modbus') protocols.modbus = true
  }
  return { state, runtime, protocols, events, faults }
}

async function getLegacySnapshot(): Promise<RuntimeSnapshot> {
  const [state, runtime, protocols, events, faults] = await Promise.all([
    request<RuntimeSnapshot['state']>('/api/state'),
    request<RuntimeSnapshot['runtime']>('/api/runtime'),
    request<RuntimeSnapshot['protocols']>('/api/protocols'),
    request<RuntimeSnapshot['events']>('/api/events'),
    request<RuntimeSnapshot['faults']>('/api/faults'),
  ])
  activeDeviceId = runtime.deviceId
  return { state, runtime, protocols, events, faults }
}

async function deviceId() {
  if (!activeDeviceId) await getV1Snapshot()
  return activeDeviceId!
}

export const developerConsoleApi: DeveloperConsoleApi = {
  async getSnapshot() {
    try { return await getV1Snapshot() }
    catch (error) {
      if (error instanceof ApiProblemError && error.status !== 404) throw error
      return getLegacySnapshot()
    }
  },
  runRuntimeCommand: async command => request(`/api/v1/devices/${encodeURIComponent(await deviceId())}/${command}`, { method: 'POST' }),
  tick: async seconds => request(`/api/v1/devices/${encodeURIComponent(await deviceId())}/tick/${seconds}`, { method: 'POST' }),
  runScenario: async yaml => request(`/api/v1/devices/${encodeURIComponent(await deviceId())}/scenario`, {
    method: 'POST', headers: { 'Content-Type': 'text/yaml' }, body: yaml,
  }),
  stopScenario: async () => request(`/api/v1/devices/${encodeURIComponent(await deviceId())}/scenario`, { method: 'DELETE' }),
  activateFault: async fault => request(`/api/v1/devices/${encodeURIComponent(await deviceId())}/faults`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(fault),
  }),
  recoverFault: async id => request(`/api/v1/devices/${encodeURIComponent(await deviceId())}/faults/${encodeURIComponent(id)}/recover`, { method: 'POST' }),
}

export const developerConsoleApiKey: InjectionKey<DeveloperConsoleApi> = Symbol('developerConsoleApi')
