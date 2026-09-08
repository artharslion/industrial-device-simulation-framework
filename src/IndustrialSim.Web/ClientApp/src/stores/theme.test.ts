import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useThemeStore } from './theme'

describe('theme store', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.removeAttribute('data-theme')
    setActivePinia(createPinia())
  })

  it('defaults to system, persists explicit choices, and applies the resolved theme', () => {
    const listeners: Array<() => void> = []
    let dark = true
    vi.stubGlobal('matchMedia', vi.fn(() => ({
      get matches() { return dark },
      addEventListener: (_: string, listener: () => void) => listeners.push(listener),
      removeEventListener: vi.fn(),
    })))

    const theme = useThemeStore()
    theme.initialize()
    expect(theme.preference).toBe('system')
    expect(document.documentElement.dataset.theme).toBe('dark')

    theme.setPreference('light')
    expect(localStorage.getItem('industrial-sim-theme')).toBe('light')
    expect(document.documentElement.dataset.theme).toBe('light')

    theme.setPreference('system')
    dark = false
    listeners[0]?.()
    expect(document.documentElement.dataset.theme).toBe('light')
  })
})
