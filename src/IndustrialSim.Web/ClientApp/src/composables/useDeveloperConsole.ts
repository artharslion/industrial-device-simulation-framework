import { computed, reactive, ref } from 'vue'
import type { DeveloperConsoleApi } from '../api'
import type { ActiveFault, ProtocolStatus, RuntimeEvent, RuntimeStatus, ScalarValue } from '../types'

const defaultScenario = `scenario:
  name: operator-sequence
  steps:
    - at: 0s
      set:
        device: pump-001
        datapoint: speed
        value: 900`

const emptyRuntime: RuntimeStatus = {
  state: 'Loading',
  time: '00:00:00',
  deviceId: 'Connecting…',
  deviceType: 'runtime discovery',
  deterministic: false,
  seed: 0,
  scenario: { name: null, running: false },
  activeFaults: 0,
}

export function useDeveloperConsole(api: DeveloperConsoleApi) {
  const state = ref<Record<string, ScalarValue>>({})
  const runtime = ref<RuntimeStatus>({ ...emptyRuntime })
  const protocols = ref<ProtocolStatus>({ opcua: false, modbus: false })
  const events = ref<RuntimeEvent[]>([])
  const faults = ref<ActiveFault[]>([])
  const scenarioYaml = ref(defaultScenario)
  const faultForm = reactive({ category: 'Data', type: 'spike', target: 'speed', parameter: '25' })
  const lastSync = ref('Connecting')
  const error = ref('')
  const loading = ref(true)
  const pendingAction = ref<string | null>(null)
  let pollingHandle: number | undefined
  let resolvedScenarioDevice = false
  let refreshInFlight: Promise<void> | undefined

  const activity = computed(() => runtime.value.scenario.running
    ? `Scenario: ${runtime.value.scenario.name ?? 'active'}`
    : runtime.value.activeFaults > 0
      ? `${runtime.value.activeFaults} Fault${runtime.value.activeFaults === 1 ? '' : 's'} Active`
      : 'Idle')

  async function refresh(force = false) {
    if (refreshInFlight) {
      if (!force) return refreshInFlight
      await refreshInFlight
    }
    refreshInFlight = (async () => {
      try {
        const snapshot = await api.getSnapshot()
        state.value = snapshot.state
        runtime.value = snapshot.runtime
        protocols.value = snapshot.protocols
        events.value = snapshot.events
        faults.value = snapshot.faults
        lastSync.value = `Synced ${new Date().toLocaleTimeString([], { hour12: false })}`
        if (!resolvedScenarioDevice) {
          scenarioYaml.value = scenarioYaml.value.replaceAll('pump-001', snapshot.runtime.deviceId)
          resolvedScenarioDevice = true
        }
        error.value = ''
      } catch (cause) {
        error.value = cause instanceof Error ? cause.message : String(cause)
      } finally {
        loading.value = false
        refreshInFlight = undefined
      }
    })()
    return refreshInFlight
  }

  async function execute(name: string, operation: () => Promise<void>) {
    pendingAction.value = name
    error.value = ''
    try {
      await operation()
      await refresh(true)
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : String(cause)
    } finally {
      pendingAction.value = null
    }
  }

  const runRuntimeCommand = (command: 'start' | 'pause' | 'stop' | 'reset') =>
    execute(`runtime-${command}`, () => api.runRuntimeCommand(command))
  const tick = () => execute('tick', () => api.tick(1))
  const runScenario = () => execute('scenario-run', () => api.runScenario(scenarioYaml.value))
  const stopScenario = () => execute('scenario-stop', () => api.stopScenario())
  const recoverFault = (id: string) => execute(`fault-recover-${id}`, () => api.recoverFault(id))
  const activateFault = () => execute('fault-activate', () => api.activateFault({
    id: `ui-${Date.now()}`,
    category: faultForm.category,
    target: faultForm.target,
    type: faultForm.type,
    metadata: faultForm.parameter ? { parameter: faultForm.parameter } : null,
  }))

  function startPolling() {
    if (pollingHandle === undefined) pollingHandle = window.setInterval(refresh, 1000)
  }

  function stopPolling() {
    if (pollingHandle !== undefined) window.clearInterval(pollingHandle)
    pollingHandle = undefined
  }

  return {
    state,
    runtime,
    protocols,
    events,
    faults,
    scenarioYaml,
    faultForm,
    lastSync,
    error,
    loading,
    pendingAction,
    activity,
    refresh,
    startPolling,
    stopPolling,
    runRuntimeCommand,
    tick,
    runScenario,
    stopScenario,
    activateFault,
    recoverFault,
  }
}
