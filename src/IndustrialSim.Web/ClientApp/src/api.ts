import type { InjectionKey } from 'vue'
import type {
  BuiltInDeviceProfile, DeviceCreateRequest, DeviceDetails, DeviceSummary, DeviceTemplateDocument, EffectiveSetting, FaultRequest, ProtocolStatus, ProtocolSummary,
  ScenarioCatalogItem, SessionSummary, SettingSummary, TemplatePackage, UserSummary, RuntimeSnapshot, RuntimeStatus, ScalarValue,
} from './types'

interface ProblemDetails { title?: string; detail?: string; errorCode?: string; error?: unknown }

export class ApiProblemError extends Error {
  constructor(message: string, public readonly status: number, public readonly errorCode?: string) {
    super(message)
    this.name = 'ApiProblemError'
  }
}

export async function request<T>(url: string, options: RequestInit = {}): Promise<T> {
  const accessToken = sessionStorage.getItem('industrial-sim-access-token')
  if (accessToken) {
    const headers = new Headers(options.headers)
    if (!headers.has('Authorization')) headers.set('Authorization', `Bearer ${accessToken}`)
    options = { ...options, headers }
  }
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

const json = (method: string, body?: unknown): RequestInit => ({
  method,
  headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
  body: body === undefined ? undefined : JSON.stringify(body),
})

export const platformApi = {
  session: () => request<SessionSummary>('/api/v1/auth/session'),
  devices: () => request<DeviceSummary[]>('/api/v1/devices'),
  deviceProfiles: () => request<BuiltInDeviceProfile[]>('/api/v1/device-profiles'),
  createDevice: (value: DeviceCreateRequest) => request<DeviceSummary>('/api/v1/devices', json('POST', value)),
  device: (id: string) => request<DeviceDetails>(`/api/v1/devices/${encodeURIComponent(id)}`),
  updateDevice: (id: string, value: DeviceCreateRequest) => request(`/api/v1/devices/${encodeURIComponent(id)}`, json('PUT', value)),
  deleteDevice: (id: string) => request<void>(`/api/v1/devices/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  lifecycle: (id: string, operation: string) => request<RuntimeStatus>(`/api/v1/devices/${encodeURIComponent(id)}/${operation}`, { method: 'POST' }),
  tick: (id: string, seconds: number) => request(`/api/v1/devices/${encodeURIComponent(id)}/tick/${seconds}`, { method: 'POST' }),
  writeState: (id: string, point: string, value: ScalarValue) => request(`/api/v1/devices/${encodeURIComponent(id)}/state/${encodeURIComponent(point)}`, json('PUT', value)),
  activateDeviceFault: (id: string, value: FaultRequest) => request(`/api/v1/devices/${encodeURIComponent(id)}/faults`, json('POST', value)),
  recoverDeviceFault: (id: string, faultId: string) => request(`/api/v1/devices/${encodeURIComponent(id)}/faults/${encodeURIComponent(faultId)}/recover`, { method: 'POST' }),
  protocols: () => request<ProtocolSummary[]>('/api/v1/protocols'),
  templates: (query = '') => request<DeviceTemplateDocument[]>(`/api/v1/templates${query ? `?q=${encodeURIComponent(query)}` : ''}`),
  template: (id: string, version: string) => request<TemplatePackage>(`/api/v1/templates/${encodeURIComponent(id)}/${encodeURIComponent(version)}`),
  createTemplate: (value: TemplatePackage) => request<TemplatePackage>('/api/v1/templates', json('POST', value)),
  deleteTemplate: (id: string, version: string) => request<void>(`/api/v1/templates/${encodeURIComponent(id)}/${encodeURIComponent(version)}`, { method: 'DELETE' }),
  instantiateTemplate: (id: string, version: string, value: unknown) => request(`/api/v1/templates/${encodeURIComponent(id)}/${encodeURIComponent(version)}/instantiate`, json('POST', value)),
  scenarios: () => request<ScenarioCatalogItem[]>('/api/v1/scenarios'),
  scenario: (id: string) => request<ScenarioCatalogItem>(`/api/v1/scenarios/${encodeURIComponent(id)}`),
  saveScenario: (id: string, value: Omit<ScenarioCatalogItem, 'id'>) => request<ScenarioCatalogItem>(`/api/v1/scenarios/${encodeURIComponent(id)}`, json('PUT', value)),
  importScenario: (value: { id: string; name: string; yaml: string; editorJson: string }) => request<ScenarioCatalogItem>('/api/v1/scenarios/import', json('POST', value)),
  exportScenario: (id: string) => request<string>(`/api/v1/scenarios/${encodeURIComponent(id)}/export`),
  runScenario: (deviceId: string, scenarioId: string) => request(`/api/v1/devices/${encodeURIComponent(deviceId)}/scenarios/${encodeURIComponent(scenarioId)}/start`, { method: 'POST' }),
  users: () => request<UserSummary[]>('/api/v1/users'),
  bootstrap: (value: { userName: string; password: string }) => request<UserSummary>('/api/v1/auth/bootstrap', json('POST', value)),
  async login(value: { userName: string; password: string }) {
    const response = await request<{ accessToken: string }>('/api/v1/auth/login', json('POST', value))
    sessionStorage.setItem('industrial-sim-access-token', response.accessToken)
    return response
  },
  createUser: (value: { userName: string; password: string; role: string }) => request<UserSummary>('/api/v1/users', json('POST', value)),
  updateUserRole: (userName: string, role: string) => request<void>(`/api/v1/users/${encodeURIComponent(userName)}/role`, json('PUT', { role })),
  deleteUser: (userName: string) => request<void>(`/api/v1/users/${encodeURIComponent(userName)}`, { method: 'DELETE' }),
  changePassword: (value: { currentPassword: string; newPassword: string }) => request<void>('/api/v1/auth/password', json('POST', value)),
  settings: () => request<SettingSummary[]>('/api/v1/settings'),
  effectiveSettings: () => request<EffectiveSetting[]>('/api/v1/settings/effective'),
  saveSetting: (key: string, value: { type: string; value: unknown; version: number }) => request<SettingSummary & { type: string; source: string }>(`/api/v1/settings/${encodeURIComponent(key)}`, json('PUT', value)),
}
