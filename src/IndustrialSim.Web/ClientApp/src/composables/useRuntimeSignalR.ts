import { HubConnectionBuilder } from '@microsoft/signalr'
import type { InjectionKey } from 'vue'

export interface RuntimeStreamEvent {
  sequence: number
  deviceId: string
  dataPoint: string
  value: unknown
  simulationTime?: string
}

export interface RuntimeLiveConnection {
  start(onEvent: (event: RuntimeStreamEvent) => void, onDisconnected: () => void, onReconnected: () => void): Promise<void>
  stop(): Promise<void>
}

interface ConnectionLike {
  onreconnecting(callback: () => void): void
  onreconnected(callback: () => void): void
  stream<T>(methodName: string): { subscribe(observer: { next(value: T): void; error(error: unknown): void; complete(): void }): { dispose(): void } | undefined }
  start(): Promise<void>
  stop(): Promise<void>
}

interface BuilderLike {
  withUrl(url: string): BuilderLike
  withAutomaticReconnect(): BuilderLike
  build(): ConnectionLike
}

export function createRuntimeLiveConnection(builderFactory: () => BuilderLike = () => new HubConnectionBuilder() as unknown as BuilderLike): RuntimeLiveConnection {
  let connection: ConnectionLike | undefined
  let streamSubscription: { dispose(): void } | undefined
  return {
    async start(onEvent, onDisconnected, onReconnected) {
      connection = builderFactory().withUrl('/hubs/runtime').withAutomaticReconnect().build()
      connection.onreconnecting(onDisconnected)
      connection.onreconnected(onReconnected)
      await connection.start()
      streamSubscription = connection.stream<RuntimeStreamEvent>('Stream').subscribe({
        next: onEvent,
        error: () => onDisconnected(),
        complete: () => onDisconnected(),
      })
    },
    async stop() {
      streamSubscription?.dispose()
      streamSubscription = undefined
      if (connection) await connection.stop()
      connection = undefined
    },
  }
}

export const runtimeLiveConnection = createRuntimeLiveConnection()
export const runtimeLiveConnectionKey: InjectionKey<RuntimeLiveConnection> = Symbol('runtimeLiveConnection')
