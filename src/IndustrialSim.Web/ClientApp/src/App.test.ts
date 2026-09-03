import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import App from './App.vue'
import { developerConsoleApiKey } from './api'
import type { DeveloperConsoleApi } from './api'

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
    const wrapper = mount(App, { global: { provide: { [developerConsoleApiKey as symbol]: api } } })
    await vi.waitFor(() => expect(wrapper.text()).toContain('pump-001'))

    expect(wrapper.find('[aria-label="Workspace navigation"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('StateStore datapoints')
    expect(wrapper.text()).toContain('Scenario control')
    expect(wrapper.text()).toContain('Fault control')
  })
})
