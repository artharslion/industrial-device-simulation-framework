import { describe, expect, it } from 'vitest'
import { useDeviceEditor } from './useDeviceEditor'

describe('device editor', () => {
  it('edits dynamic datapoints and converts typed initial values', () => {
    const editor = useDeviceEditor()
    editor.form.id = 'sensor-2'
    editor.form.type = 'sensor'
    editor.form.dataPoints[0]!.name = 'temperature'
    editor.form.dataPoints[0]!.dataType = 'Double'
    editor.form.dataPoints[0]!.initialText = '21.5'
    editor.addDataPoint()
    editor.form.dataPoints[1]!.name = 'enabled'
    editor.form.dataPoints[1]!.dataType = 'Boolean'
    editor.form.dataPoints[1]!.initialText = 'true'
    editor.addBinding()
    editor.form.portBindings[0]!.protocol = 'opcua'
    editor.form.portBindings[0]!.port = 4841

    expect(editor.toRequest()).toMatchObject({
      id: 'sensor-2',
      dataPoints: [{ name: 'temperature', initial: 21.5 }, { name: 'enabled', initial: true }],
      portBindings: [{ protocol: 'opcua', port: 4841 }],
    })
    editor.removeDataPoint(1)
    expect(editor.form.dataPoints).toHaveLength(1)
  })

  it('rejects duplicate datapoints and invalid numeric values', () => {
    const editor = useDeviceEditor()
    editor.form.id = 'device-1'
    editor.form.type = 'custom'
    editor.form.dataPoints[0]!.name = 'speed'
    editor.form.dataPoints[0]!.dataType = 'Int32'
    editor.form.dataPoints[0]!.initialText = 'not-a-number'
    editor.addDataPoint()
    editor.form.dataPoints[1]!.name = 'speed'
    expect(() => editor.toRequest()).toThrow(/unique|number/i)
  })
})
