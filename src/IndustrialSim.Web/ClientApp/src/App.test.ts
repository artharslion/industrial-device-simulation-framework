import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import App from './App.vue'
import { developerConsoleApiKey, platformApi } from './api'
import type { DeveloperConsoleApi } from './api'
import { runtimeLiveConnectionKey } from './composables/useRuntimeSignalR'
import ConsoleLayout from './layouts/ConsoleLayout.vue'
import { routes } from './router'

const api: DeveloperConsoleApi = {
  getSnapshot: vi.fn().mockResolvedValue({
    state: { speed: 900 },
    runtime: { state: 'Running', time: '00:00:01', deviceId: 'pump-001', deviceType: 'pump', deterministic: true, seed: 1, scenario: { name: null, running: false }, activeFaults: 0 },
    protocols: { opcua: true, modbus: true },
    events: [],
    faults: [],
  }),
  runRuntimeCommand: vi.fn().mockResolvedValue(undefined),
  tick: vi.fn().mockResolvedValue(undefined),
  runScenario: vi.fn().mockResolvedValue(undefined),
  stopScenario: vi.fn().mockResolvedValue(undefined),
  activateFault: vi.fn().mockResolvedValue(undefined),
  recoverFault: vi.fn().mockResolvedValue(undefined),
}

describe('developer console', () => {
  it('renders the operational workspace and runtime controls', async () => {
    vi.spyOn(platformApi, 'devices').mockResolvedValue([{ deviceId: 'pump-001', deviceType: 'pump', isRunning: true, deterministic: true, seed: 1, simulationTime: '00:00:01' }])
    vi.spyOn(platformApi, 'protocols').mockResolvedValue([])
    vi.spyOn(platformApi, 'device').mockResolvedValue({ summary: { deviceId: 'pump-001', deviceType: 'pump', isRunning: true, deterministic: true, seed: 1, simulationTime: '00:00:01' }, runtime: { state: 'Running', time: '00:00:01', deviceId: 'pump-001', deviceType: 'pump', deterministic: true, seed: 1, scenario: { name: null, running: false }, activeFaults: 0 }, state: { speed: 900 }, definition: { id: 'pump-001', type: 'pump', deterministic: true, seed: 1, version: 1, dataPoints: [], portBindings: [] }, protocols: [], scenarios: { running: false, available: [] }, faults: [], events: [] })
    const live = { start: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined) }
    const testRouter = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: ConsoleLayout, children: routes }] })
    await testRouter.push('/'); await testRouter.isReady()
    const wrapper = mount(App, { global: { plugins: [createPinia(), testRouter], provide: { [developerConsoleApiKey as symbol]: api, [runtimeLiveConnectionKey as symbol]: live } } })
    await vi.waitFor(() => expect(wrapper.text()).toContain('pump-001'))

    expect(wrapper.find('[aria-label="Workspace navigation"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Platform overview')
    expect(wrapper.text()).toContain('Create device')
    expect(wrapper.text()).not.toContain('Scenario YAML')
    expect(wrapper.findAll('.nav-item')).toHaveLength(8)
  })
})
