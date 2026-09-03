import type { InjectionKey } from 'vue'
import type { FaultRequest, RuntimeSnapshot } from './types'

async function request<T>(url: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(url, options)
  const text = await response.text()
  let body: unknown = undefined

  if (text) {
    try {
      body = JSON.parse(text)
    } catch {
      body = text
    }
  }

  if (!response.ok) {
    const message = typeof body === 'object' && body !== null && 'error' in body
      ? String(body.error)
      : typeof body === 'string' && body
        ? body
        : `${response.status} ${response.statusText}`
    throw new Error(message)
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

export const developerConsoleApi: DeveloperConsoleApi = {
  async getSnapshot() {
    const [state, runtime, protocols, events, faults] = await Promise.all([
      request<RuntimeSnapshot['state']>('/api/state'),
      request<RuntimeSnapshot['runtime']>('/api/runtime'),
      request<RuntimeSnapshot['protocols']>('/api/protocols'),
      request<RuntimeSnapshot['events']>('/api/events'),
      request<RuntimeSnapshot['faults']>('/api/faults'),
    ])
    return { state, runtime, protocols, events, faults }
  },
  runRuntimeCommand: command => request(`/api/runtime/${command}`, { method: 'POST' }),
  tick: seconds => request(`/api/runtime/tick/${seconds}`, { method: 'POST' }),
  runScenario: yaml => request('/api/scenario', {
    method: 'POST',
    headers: { 'Content-Type': 'text/yaml' },
    body: yaml,
  }),
  stopScenario: () => request('/api/scenario', { method: 'DELETE' }),
  activateFault: fault => request('/api/fault', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(fault),
  }),
  recoverFault: id => request(`/api/fault/recover/${encodeURIComponent(id)}`, { method: 'POST' }),
}

export const developerConsoleApiKey: InjectionKey<DeveloperConsoleApi> = Symbol('developerConsoleApi')
