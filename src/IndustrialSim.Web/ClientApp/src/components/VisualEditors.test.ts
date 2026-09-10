import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { platformApi } from '../api'
import TemplateEditor from './templates/TemplateEditor.vue'
import ScenarioEditor from './scenarios/ScenarioEditor.vue'
import DeviceCreateDialog from './devices/DeviceCreateDialog.vue'

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
    const details = vi.spyOn(platformApi, 'device').mockResolvedValue({
      summary: { deviceId: 'pump-1', deviceType: 'Pump', isRunning: false, deterministic: true, seed: 1, simulationTime: '00:00:00' },
      runtime: { state: 'Stopped', time: '00:00:00', deviceId: 'pump-1', deviceType: 'Pump', deterministic: true, seed: 1, scenario: { name: null, running: false }, activeFaults: 0 },
      state: { speed: 0, running: false },
      definition: { id: 'pump-1', type: 'Pump', deterministic: true, seed: 1, version: 1, dataPoints: [{ name: 'speed', dataType: 'Int32', access: 'ReadWrite', initial: 0 }, { name: 'running', dataType: 'Boolean', access: 'Read', initial: false }], commands: ['start', 'stop'], events: [], portBindings: [{ protocol: 'modbus', port: 5020 }] },
      protocols: [], scenarios: { running: false, available: [] }, faults: [], events: [],
    })
    const wrapper = mount(ScenarioEditor, { props: { devices: [{ deviceId: 'pump-1', deviceType: 'Pump', isRunning: false, deterministic: true, seed: 1, simulationTime: '00:00:00' }] }, global: { plugins: [createPinia()] } })
    await vi.waitFor(() => expect(wrapper.get('[aria-label="Reference device"]').element).toHaveProperty('value', 'pump-1'))
    expect(wrapper.attributes('aria-label')).toBe('Graphical scenario editor')
    expect(wrapper.text()).toContain('Execution flow')
    expect(wrapper.text()).toContain('Generated YAML')
    expect(wrapper.text()).toContain('2 datapoints · 2 commands · 1 protocols')
    expect(wrapper.findAll('.flow-card label').map(label => label.text())).not.toContain('Device')
    await wrapper.get('.flow-toolbar button').trigger('click')
    expect(wrapper.findAll('.flow-card')).toHaveLength(2)
    details.mockRestore()
  })

  it('guides built-in device creation with explicit behavior defaults', async () => {
    const profiles = vi.spyOn(platformApi, 'deviceProfiles').mockResolvedValue([{
      name: 'pump', displayName: 'Pump', description: 'Accelerates, heats, and derives pressure.',
      dataPoints: [
        { name: 'speed', dataType: 'Int32', access: 'ReadWrite', initial: 0, unit: 'rpm' },
        { name: 'temperature', dataType: 'Double', access: 'Read', initial: 25, unit: '°C' },
      ],
      commands: ['start', 'stop'], events: ['PumpStarted'],
      parameters: [{ name: 'ratedSpeed', defaultValue: 1450, minimum: 0, unit: 'rpm', description: 'Target speed.' }],
    }])
    const wrapper = mount(DeviceCreateDialog)
    await vi.waitFor(() => expect(wrapper.get('#device-profile').findAll('option')).toHaveLength(2))
    await wrapper.get('#device-profile').setValue('pump')

    expect(wrapper.get('#device-type').element).toHaveProperty('value', 'pump')
    expect(wrapper.text()).toContain('Accelerates, heats, and derives pressure.')
    expect(wrapper.text()).toContain('start, stop')
    expect(wrapper.findAll('.mapping-card')).toHaveLength(2)
    expect(wrapper.get('#behavior-ratedSpeed').element).toHaveProperty('value', '1450')
    profiles.mockRestore()
  })
})
