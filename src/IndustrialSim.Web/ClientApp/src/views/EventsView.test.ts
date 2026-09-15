import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { platformApi } from '../api'
import EventsView from './EventsView.vue'

describe('EventsView', () => {
  afterEach(() => vi.restoreAllMocks())

  it('searches retained events across devices and event payloads', async () => {
    vi.spyOn(platformApi, 'devices').mockResolvedValue([
      { deviceId: 'pump-001', deviceType: 'pump', isRunning: true, deterministic: true, seed: 1, simulationTime: '00:04:15' },
    ])
    const events = vi.spyOn(platformApi, 'runtimeEvents')
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([{ sequence: 42, deviceId: 'pump-001', eventType: 'DataPointChanged', data: { dataPoint: 'alarm', newValue: true } }])

    const wrapper = mount(EventsView)
    await flushPromises()
    await wrapper.get('[aria-label="Device filter"]').setValue('pump-001')
    await wrapper.get('[aria-label="Event type filter"]').setValue('DataPointChanged')
    await wrapper.get('[aria-label="Search event payloads"]').setValue('alarm')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(events).toHaveBeenLastCalledWith({ deviceId: 'pump-001', eventType: 'DataPointChanged', search: 'alarm', limit: 200 })
    expect(wrapper.text()).toContain('pump-001')
    expect(wrapper.text()).toContain('alarm')
    expect(wrapper.text()).toContain('1 retained event')
  })
})
