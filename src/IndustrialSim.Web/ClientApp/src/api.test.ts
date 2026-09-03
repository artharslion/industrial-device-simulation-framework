import { afterEach, describe, expect, it, vi } from 'vitest'
import { developerConsoleApi } from './api'

function response(body: unknown, status = 200, contentType = 'application/json') {
  return new Response(body === undefined ? undefined : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': contentType },
  })
}

describe('developerConsoleApi', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('prefers the versioned API and composes a coherent snapshot', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response([{ deviceId: 'pump-v1' }]))
      .mockResolvedValueOnce(response({ speed: 42 }))
      .mockResolvedValueOnce(response({ state: 'Running', time: '00:00:01', deviceId: 'pump-v1', deviceType: 'pump', deterministic: true, seed: 7, scenario: { name: null, running: false }, activeFaults: 0 }))
      .mockResolvedValueOnce(response([{ deviceId: 'pump-v1', configured: [{ name: 'opcua', running: true }], reserved: [{ name: 'modbus', port: 5020 }] }]))
      .mockResolvedValueOnce(response([]))
      .mockResolvedValueOnce(response([]))
    vi.stubGlobal('fetch', fetchMock)

    const snapshot = await developerConsoleApi.getSnapshot()

    expect(snapshot.runtime.deviceId).toBe('pump-v1')
    expect(snapshot.protocols).toEqual({ opcua: true, modbus: true })
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/devices', {})
  })

  it('falls back to the legacy API for one compatibility cycle', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response({ title: 'Missing', detail: 'v1 unavailable', errorCode: 'missing' }, 404, 'application/problem+json'))
      .mockResolvedValueOnce(response({ speed: 1 }))
      .mockResolvedValueOnce(response({ state: 'Stopped', time: '00:00:00', deviceId: 'legacy', deviceType: 'pump', deterministic: true, seed: 1, scenario: { name: null, running: false }, activeFaults: 0 }))
      .mockResolvedValueOnce(response({ opcua: false, modbus: false }))
      .mockResolvedValueOnce(response([]))
      .mockResolvedValueOnce(response([]))
    vi.stubGlobal('fetch', fetchMock)

    expect((await developerConsoleApi.getSnapshot()).runtime.deviceId).toBe('legacy')
    expect(fetchMock).toHaveBeenCalledWith('/api/state', {})
  })

  it('surfaces RFC Problem Details and stable error codes', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ title: 'Rejected', detail: 'No write access', errorCode: 'stateWriteRejected' }, 400, 'application/problem+json')))

    await expect(developerConsoleApi.runRuntimeCommand('pause')).rejects.toEqual(
      expect.objectContaining({ message: 'No write access', errorCode: 'stateWriteRejected', status: 400 }),
    )
  })
})
