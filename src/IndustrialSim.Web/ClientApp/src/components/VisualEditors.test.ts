import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import TemplateEditor from './templates/TemplateEditor.vue'
import ScenarioEditor from './scenarios/ScenarioEditor.vue'

describe('visual modeling surfaces', () => {
  it('renders a complete device definition and separate protocol mapping workflow', async () => {
    const wrapper = mount(TemplateEditor, { global: { plugins: [createPinia()] } })
    expect(wrapper.attributes('aria-label')).toBe('Visual device template editor')
    expect(wrapper.text()).toContain('Identity and version')
    expect(wrapper.text()).toContain('Datapoints')
    expect(wrapper.text()).toContain('Protocol mapping profiles')
    await wrapper.get('button.primary').trigger('click')
    expect(wrapper.findAll('[aria-label="Datapoint name"]')).toHaveLength(2)
  })

  it('renders keyboard-accessible ordered scenario cards and YAML preview', async () => {
    const wrapper = mount(ScenarioEditor, { props: { devices: [{ deviceId: 'pump-1', deviceType: 'Pump', isRunning: false, deterministic: true, seed: 1, simulationTime: '00:00:00' }] }, global: { plugins: [createPinia()] } })
    expect(wrapper.attributes('aria-label')).toBe('Graphical scenario editor')
    expect(wrapper.text()).toContain('Execution flow')
    expect(wrapper.text()).toContain('Generated YAML')
    await wrapper.get('.flow-toolbar button').trigger('click')
    expect(wrapper.findAll('.flow-card')).toHaveLength(2)
  })
})
