import { nextTick } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import { useDeveloperConsole } from './useDeveloperConsole'
import type { DeveloperConsoleApi } from '../api'

const snapshot = {
  state: { speed: 900, running: true },
  runtime: {
    state: 'Running',
    time: '00:00:05',
    deviceId: 'pump-001',
    deviceType: 'pump',
    deterministic: true,
    seed: 42,
    scenario: { name: null, running: false },
    activeFaults: 0,
  },
  protocols: { opcua: true, modbus: true },
  events: [],
  faults: [],
}

function createApi(): DeveloperConsoleApi {
  return {
    getSnapshot: vi.fn().mockResolvedValue(snapshot),
    runRuntimeCommand: vi.fn().mockResolvedValue(undefined),
    tick: vi.fn().mockResolvedValue(undefined),
    runScenario: vi.fn().mockResolvedValue(undefined),
    stopScenario: vi.fn().mockResolvedValue(undefined),
    activateFault: vi.fn().mockResolvedValue(undefined),
    recoverFault: vi.fn().mockResolvedValue(undefined),
  }
}

describe('useDeveloperConsole', () => {
  it('loads one coherent runtime snapshot', async () => {
    const api = createApi()
    const consoleState = useDeveloperConsole(api)

    await consoleState.refresh()

    expect(consoleState.runtime.value.deviceId).toBe('pump-001')
    expect(consoleState.state.value.speed).toBe(900)
    expect(consoleState.scenarioYaml.value).toContain('device: pump-001')
  })

  it('refreshes after an operation and reports API errors', async () => {
    const api = createApi()
    vi.mocked(api.runRuntimeCommand).mockRejectedValueOnce(new Error('runtime rejected'))
    const consoleState = useDeveloperConsole(api)

    await consoleState.runRuntimeCommand('pause')
    await nextTick()

    expect(consoleState.error.value).toBe('runtime rejected')
    expect(api.getSnapshot).not.toHaveBeenCalled()
  })

  it('refreshes with the committed state after a successful operation', async () => {
    const api = createApi()
    const consoleState = useDeveloperConsole(api)

    await consoleState.runRuntimeCommand('pause')

    expect(api.runRuntimeCommand).toHaveBeenCalledWith('pause')
    expect(api.getSnapshot).toHaveBeenCalledOnce()
  })

  it('stops periodic refreshes when polling is disposed', async () => {
    vi.useFakeTimers()
    const api = createApi()
    const consoleState = useDeveloperConsole(api)

    consoleState.startPolling()
    await vi.advanceTimersByTimeAsync(1000)
    consoleState.stopPolling()
    await vi.advanceTimersByTimeAsync(2000)

    expect(api.getSnapshot).toHaveBeenCalledOnce()
    vi.useRealTimers()
  })
})
