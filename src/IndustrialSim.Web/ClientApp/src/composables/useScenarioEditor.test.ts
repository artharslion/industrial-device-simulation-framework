import { describe, expect, it, vi } from 'vitest'
import { useScenarioEditor } from './useScenarioEditor'

describe('graphical scenario editor', () => {
  it('round trips YAML and preserves explicit visual order', async () => {
    const api = { saveScenario: vi.fn().mockResolvedValue({ version: 1 }), runScenario: vi.fn().mockResolvedValue(undefined) }
    const editor = useScenarioEditor(api)
    editor.name.value = 'startup'
    editor.id.value = 'startup'
    editor.addStep('set')
    Object.assign(editor.steps[0], { triggerType: 'at', triggerValue: '0s', device: 'pump-1', dataPoint: 'speed', value: 900 })
    editor.addStep('command')
    Object.assign(editor.steps[1], { triggerType: 'after', triggerValue: '2s', device: 'pump-1', command: 'start' })
    editor.addStep('ramp')
    Object.assign(editor.steps[2], { triggerType: 'after', triggerValue: '1s', device: 'pump-1', dataPoint: 'speed', from: 0, to: 900, duration: '4s' })
    editor.addStep('wait')
    Object.assign(editor.steps[3], { triggerType: 'after', triggerValue: '1s', duration: '2s' })
    editor.moveStep(1, -1)

    const yaml = editor.toYaml()
    expect(yaml.indexOf('command:')).toBeLessThan(yaml.indexOf('set:'))
    editor.importYaml(yaml)
    expect(editor.steps.map(step => step.actionType)).toEqual(['command', 'set', 'ramp', 'wait'])

    await editor.save()
    await editor.run('pump-1')
    expect(api.saveScenario).toHaveBeenCalledWith('startup', expect.objectContaining({ editorJson: expect.stringContaining('command') }))
    expect(api.runScenario).toHaveBeenCalledWith('pump-1', 'startup')
  })
})
