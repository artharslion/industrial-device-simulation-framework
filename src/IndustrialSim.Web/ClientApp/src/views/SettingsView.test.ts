import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { platformApi } from '../api'
import SettingsView from './SettingsView.vue'

describe('settings view', () => {
  it('separates effective configuration from editable persisted settings', async () => {
    vi.spyOn(platformApi, 'settings').mockResolvedValue([{ key: 'ui.refreshSeconds', valueJson: '3', version: 1 }])
    vi.spyOn(platformApi, 'effectiveSettings').mockResolvedValue([{ key: 'Auth.Mode', value: 'Disabled', type: 'String', source: 'Effective runtime configuration' }])
    const wrapper = mount(SettingsView, { global: { plugins: [createPinia()] } })
    await flushPromises()
    expect(wrapper.text()).toContain('Effective runtime configuration')
    expect(wrapper.text()).toContain('Persisted control-plane settings')
    expect(wrapper.text()).toContain('revision 1')
    await wrapper.get('button.primary').trigger('click')
    expect(wrapper.text()).toContain('Add setting')
    expect(wrapper.text()).toContain('String')
    expect(wrapper.text()).toContain('JSON')
  })
})
