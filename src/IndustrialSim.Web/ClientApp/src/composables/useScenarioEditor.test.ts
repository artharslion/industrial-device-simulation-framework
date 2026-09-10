import { describe, expect, it, vi } from 'vitest'
import { useScenarioEditor } from './useScenarioEditor'

describe('graphical scenario editor', () => {
  it('round trips YAML and preserves explicit visual order', async () => {
    const api = { saveScenario: vi.fn().mockResolvedValue({ version: 1 }), runScenario: vi.fn().mockResolvedValue(undefined) }
    const editor = useScenarioEditor(api)
    editor.name.value = 'startup'
    editor.id.value = 'startup'
    editor.targetType.value = 'pump'
    editor.addStep('set')
    Object.assign(editor.steps[0], { triggerType: 'at', triggerAmount: 0, triggerUnit: 's', dataPoint: 'speed', value: 900 })
    editor.addStep('command')
    Object.assign(editor.steps[1], { triggerType: 'after', triggerAmount: 2, triggerUnit: 's', command: 'start' })
    editor.addStep('ramp')
    Object.assign(editor.steps[2], { triggerType: 'after', triggerAmount: 1, triggerUnit: 's', dataPoint: 'speed', from: 0, to: 900, durationAmount: 4, durationUnit: 's' })
    editor.addStep('wait')
    Object.assign(editor.steps[3], { triggerType: 'after', triggerAmount: 1, triggerUnit: 's', durationAmount: 2, durationUnit: 's' })
    editor.moveStep(1, -1)

    const yaml = editor.toYaml()
    expect(yaml).toContain('target:\n    type: pump')
    expect(yaml).not.toContain('device:')
    expect(yaml.indexOf('command:')).toBeLessThan(yaml.indexOf('set:'))
    editor.importYaml(yaml)
    expect(editor.steps.map(step => step.actionType)).toEqual(['command', 'set', 'ramp', 'wait'])

    await editor.save()
    await editor.run('pump-1')
    expect(api.saveScenario).toHaveBeenCalledWith('startup', expect.objectContaining({ editorJson: expect.stringContaining('command') }))
    expect(api.runScenario).toHaveBeenCalledWith('pump-1', 'startup')
  })

  it('normalizes legacy device-bound YAML into typed editor fields', () => {
    const editor = useScenarioEditor({ saveScenario: vi.fn(), runScenario: vi.fn() })
    editor.importYaml(`scenario:
  name: legacy
  steps:
    - after: 2s
      ramp:
        device: pump-1
        datapoint: speed
        from: 0
        to: 100
        duration: 500ms
`)

    expect(editor.targetType.value).toBe('')
    expect(editor.steps[0]).toMatchObject({ device: 'pump-1', triggerAmount: 2, triggerUnit: 's', durationAmount: 500, durationUnit: 'ms' })
  })
})
