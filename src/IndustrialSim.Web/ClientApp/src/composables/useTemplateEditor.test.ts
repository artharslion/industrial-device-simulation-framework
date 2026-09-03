import { describe, expect, it, vi } from 'vitest'
import { useTemplateEditor } from './useTemplateEditor'

describe('visual template editor', () => {
  it('edits datapoints and mappings and saves an immutable version', async () => {
    const api = { createTemplate: vi.fn().mockResolvedValue(undefined), instantiateTemplate: vi.fn().mockResolvedValue(undefined) }
    const editor = useTemplateEditor(api)
    editor.draft.template.id = 'pump'
    editor.draft.template.version = '1.0.0'
    editor.draft.template.displayName = 'Pump'
    editor.draft.template.deviceType = 'Pump'
    editor.addDataPoint()
    editor.draft.template.dataPoints[0].name = 'speed'
    editor.addMapping('modbus')
    editor.draft.mappings[0].entries[0].dataPoint = 'speed'
    editor.draft.mappings[0].entries[0].address = '40001'

    expect(editor.validate()).toEqual([])
    await editor.save()
    await editor.instantiate({ deviceId: 'pump-7', deterministic: true, seed: 7, portBindings: [] })

    expect(api.createTemplate).toHaveBeenCalledWith(editor.draft)
    expect(api.instantiateTemplate).toHaveBeenCalledWith('pump', '1.0.0', expect.objectContaining({ deviceId: 'pump-7' }))
  })
})
