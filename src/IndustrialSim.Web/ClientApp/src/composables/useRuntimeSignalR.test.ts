import { describe, expect, it, vi } from 'vitest'
import { createRuntimeLiveConnection } from './useRuntimeSignalR'

describe('runtime SignalR connection', () => {
  it('uses automatic reconnect and forwards ordered stream events', async () => {
    const callbacks: Record<string, (...args: unknown[]) => void> = {}
    const subscribe = vi.fn()
    const connection = {
      onreconnecting: vi.fn((callback: (...args: unknown[]) => void) => { callbacks.reconnecting = callback }),
      onreconnected: vi.fn((callback: (...args: unknown[]) => void) => { callbacks.reconnected = callback }),
      stream: vi.fn(() => ({ subscribe })),
      start: vi.fn().mockResolvedValue(undefined),
      stop: vi.fn().mockResolvedValue(undefined),
    }
    const builder = {
      withUrl: vi.fn().mockReturnThis(),
      withAutomaticReconnect: vi.fn().mockReturnThis(),
      build: vi.fn(() => connection),
    }
    const live = createRuntimeLiveConnection(() => builder)
    const onEvent = vi.fn()
    const onDisconnected = vi.fn()
    const onReconnected = vi.fn()

    await live.start(onEvent, onDisconnected, onReconnected)
    const observer = subscribe.mock.calls[0][0]
    observer.next({ sequence: 1, deviceId: 'pump', dataPoint: 'speed', value: 1 })
    observer.next({ sequence: 2, deviceId: 'pump', dataPoint: 'speed', value: 2 })
    callbacks.reconnecting()
    callbacks.reconnected()

    expect(builder.withAutomaticReconnect).toHaveBeenCalledOnce()
    expect(onEvent.mock.calls.map(call => call[0].sequence)).toEqual([1, 2])
    expect(onDisconnected).toHaveBeenCalledOnce()
    expect(onReconnected).toHaveBeenCalledOnce()
  })
})
