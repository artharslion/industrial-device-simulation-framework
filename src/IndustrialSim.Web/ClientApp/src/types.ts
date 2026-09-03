export type ScalarValue = string | number | boolean | null

export interface RuntimeStatus {
  state: string
  time: string
  deviceId: string
  deviceType: string
  deterministic: boolean
  seed: number
  scenario: {
    name: string | null
    running: boolean
  }
  activeFaults: number
}

export interface ProtocolStatus {
  opcua: boolean
  modbus: boolean
}

export interface RuntimeEvent {
  timestamp?: string | { elapsed?: string }
  time?: string
  eventType?: string
  type?: string
  dataPointId?: unknown
  commandName?: unknown
  eventMetadata?: unknown
  [key: string]: unknown
}

export interface ActiveFault {
  id: string
  category: number | string
  type: string
  target?: string | null
}

export interface RuntimeSnapshot {
  state: Record<string, ScalarValue>
  runtime: RuntimeStatus
  protocols: ProtocolStatus
  events: RuntimeEvent[]
  faults: ActiveFault[]
}

export interface FaultRequest {
  id: string
  category: string
  target: string
  type: string
  metadata: Record<string, string> | null
}
