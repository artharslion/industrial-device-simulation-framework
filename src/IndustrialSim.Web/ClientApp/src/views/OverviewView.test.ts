import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import { platformApi } from '../api'
import { runtimeLiveConnectionKey } from '../composables/useRuntimeSignalR'
import OverviewView from './OverviewView.vue'

describe('platform overview', () => {
  it('summarizes the fleet and exposes task-oriented shortcuts', async () => {
    vi.spyOn(platformApi, 'devices').mockResolvedValue([
      { deviceId: 'running', deviceType: 'pump', isRunning: true, deterministic: true, seed: 1, simulationTime: '00:00:01' },
      { deviceId: 'stopped', deviceType: 'sensor', isRunning: false, deterministic: false, seed: 2, simulationTime: '00:00:00' },
    ])
    vi.spyOn(platformApi, 'protocols').mockResolvedValue([{ deviceId: 'running', configured: [{ name: 'opcua', running: true }], reserved: [] }])
    vi.spyOn(platformApi, 'device').mockImplementation(async id => ({
      summary: { deviceId: id, deviceType: 'custom', isRunning: id === 'running', deterministic: true, seed: 1, simulationTime: '00:00:01' },
      runtime: { state: id === 'running' ? 'Running' : 'Stopped', time: '00:00:01', deviceId: id, deviceType: 'custom', deterministic: true, seed: 1, scenario: { name: null, running: false }, activeFaults: id === 'running' ? 1 : 0 },
      state: {}, definition: { id, type: 'custom', deterministic: true, seed: 1, version: 1, dataPoints: [], commands: [], events: [], portBindings: [] }, protocols: [], scenarios: { running: false, available: [] }, faults: id === 'running' ? [{ id: 'f1', category: 'Data', type: 'Freeze' }] : [], events: [],
    }))
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: OverviewView }, { path: '/:pathMatch(.*)*', component: { template: '<div />' } }] })
    await router.push('/'); await router.isReady()
    const wrapper = mount(OverviewView, { global: { plugins: [createPinia(), router], provide: { [runtimeLiveConnectionKey as symbol]: { start: vi.fn(), stop: vi.fn() } } } })
    await flushPromises()

    expect(wrapper.text()).toContain('Platform overview')
    expect(wrapper.text()).toContain('2')
    expect(wrapper.text()).toContain('1 active fault')
    expect(wrapper.text()).toContain('Create device')
    expect(wrapper.text()).not.toContain('Scenario YAML')
  })
})
