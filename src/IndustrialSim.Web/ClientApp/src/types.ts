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

export interface DeviceSummary {
  deviceId: string
  deviceType: string
  isRunning: boolean
  deterministic: boolean
  seed: number
  simulationTime: string
}

export interface DeviceDataPointRequest {
  name: string
  dataType: string
  access: string
  initial: ScalarValue
  unit?: string | null
  description?: string | null
}

export interface DeviceCreateRequest {
  id: string
  type: string
  deterministic: boolean
  seed: number
  dataPoints: DeviceDataPointRequest[]
  portBindings: Array<{ protocol: string; port: number }>
}

export interface ProtocolSummary {
  deviceId: string
  configured: Array<{ name: string; running: boolean }>
  reserved: Array<{ name: string; port: number }>
}

export interface TemplateDataPoint {
  name: string
  dataType: string
  access: string
  initial: ScalarValue
  unit?: string | null
  description?: string | null
}

export interface ProtocolMappingEntry {
  dataPoint: string
  address: string
  dataType?: string | null
  byteOrder?: string | null
  wordOrder?: string | null
}

export interface ProtocolMappingProfile {
  templateId: string
  templateVersion: string
  protocol: string
  name: string
  entries: ProtocolMappingEntry[]
}

export interface DeviceTemplateDocument {
  id: string
  version: string
  displayName: string
  deviceType: string
  description?: string | null
  tags: string[]
  dataPoints: TemplateDataPoint[]
  commands: string[]
  events: string[]
  behaviorJson: string
}

export interface TemplatePackage {
  template: DeviceTemplateDocument
  mappings: ProtocolMappingProfile[]
}

export interface ScenarioCatalogItem {
  id: string
  name: string
  yaml: string
  version: number
  editorJson: string
}

export interface UserSummary { id: string; userName: string; roles: string[] }
export interface SettingSummary { key: string; valueJson: string; version: number }
export interface SessionSummary { mode: string; authenticated: boolean; userName?: string | null; roles: string[] }
