import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import { platformApi } from '../api'
import { runtimeLiveConnectionKey } from '../composables/useRuntimeSignalR'
import DeviceDetailsView from './DeviceDetailsView.vue'

describe('device details', () => {
  it('loads all detail sections and prevents structural editing while running', async () => {
    vi.spyOn(platformApi, 'device').mockResolvedValue({
      summary: { deviceId: 'pump-1', deviceType: 'pump', isRunning: true, deterministic: true, seed: 4, simulationTime: '00:00:01' },
      runtime: { state: 'Running', time: '00:00:01', deviceId: 'pump-1', deviceType: 'pump', deterministic: true, seed: 4, scenario: { name: null, running: false }, activeFaults: 0 },
      state: { speed: 10 },
      definition: { id: 'pump-1', type: 'pump', deterministic: true, seed: 4, version: 1, dataPoints: [{ name: 'speed', dataType: 'Int32', access: 'ReadWrite', initial: 0 }], commands: ['start', 'stop'], events: [], portBindings: [{ protocol: 'modbus', port: 5020 }] },
      protocols: [], scenarios: { running: false, available: [] }, faults: [], events: [],
    })
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/devices', component: { template: '<div />' } }, { path: '/devices/:deviceId', component: DeviceDetailsView }] })
    await router.push('/devices/pump-1'); await router.isReady()
    const wrapper = mount(DeviceDetailsView, { global: { plugins: [createPinia(), router], provide: { [runtimeLiveConnectionKey as symbol]: { start: vi.fn(), stop: vi.fn() } } } })
    await flushPromises()

    expect(wrapper.findAll('.detail-tabs button').map(item => item.text())).toEqual(['overview', 'state', 'definition', 'protocols', 'scenarios', 'faults', 'events'])
    await wrapper.findAll('.detail-tabs button')[2]!.trigger('click')
    expect(wrapper.get('button').text()).not.toBe('Save and replace host')
    expect(wrapper.get('button:disabled').text()).toBe('Edit definition')
  })
})
