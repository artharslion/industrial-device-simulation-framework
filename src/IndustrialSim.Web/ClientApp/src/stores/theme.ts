import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

export type ThemePreference = 'light' | 'dark' | 'system'

const storageKey = 'industrial-sim-theme'
const isPreference = (value: string | null): value is ThemePreference => value === 'light' || value === 'dark' || value === 'system'

export const useThemeStore = defineStore('theme', () => {
  const stored = localStorage.getItem(storageKey)
  const preference = ref<ThemePreference>(isPreference(stored) ? stored : 'system')
  const systemDark = ref(false)
  let media: MediaQueryList | undefined

  const resolved = computed<'light' | 'dark'>(() => preference.value === 'system'
    ? (systemDark.value ? 'dark' : 'light')
    : preference.value)

  function apply() {
    document.documentElement.dataset.theme = resolved.value
    document.documentElement.style.colorScheme = resolved.value
  }

  function initialize() {
    media = window.matchMedia('(prefers-color-scheme: dark)')
    systemDark.value = media.matches
    media.addEventListener('change', onSystemChange)
    apply()
  }

  function onSystemChange(event?: MediaQueryListEvent | { matches: boolean }) {
    systemDark.value = event?.matches ?? media?.matches ?? false
    if (preference.value === 'system') apply()
  }

  function setPreference(value: ThemePreference) {
    preference.value = value
    localStorage.setItem(storageKey, value)
    apply()
  }

  return { preference, resolved, initialize, setPreference }
})
