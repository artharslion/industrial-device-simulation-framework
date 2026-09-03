import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { platformApi } from '../api'
import type { SessionSummary } from '../types'

export const useWorkspaceStore = defineStore('workspace', () => {
  const selectedDeviceId = ref('')
  const session = ref<SessionSummary>({ mode: 'Disabled', authenticated: true, userName: 'local-developer', roles: ['Admin'] })
  const notice = ref('')
  const sessionLoaded = ref(false)
  const isAdmin = computed(() => session.value.roles.some(role => role.toLowerCase() === 'admin'))
  const canOperate = computed(() => isAdmin.value || session.value.roles.some(role => role.toLowerCase() === 'operator'))

  async function loadSession() {
    try { session.value = await platformApi.session() } catch { /* keep disabled-mode presentation defaults */ } finally { sessionLoaded.value = true }
  }

  function notify(message: string) {
    notice.value = message
    window.setTimeout(() => { if (notice.value === message) notice.value = '' }, 3200)
  }

  return { selectedDeviceId, session, sessionLoaded, notice, isAdmin, canOperate, loadSession, notify }
})
