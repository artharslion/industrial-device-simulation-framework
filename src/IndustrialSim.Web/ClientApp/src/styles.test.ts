import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const styles = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8')

describe('theme surface styles', () => {
  it('defines light-mode surfaces for dense operational and editor content', () => {
    const lightTheme = styles.match(/:root\[data-theme="light"\]\s*\{([\s\S]*?)\n\}/)?.[1] ?? ''

    expect(lightTheme).toContain('--surface-inset: #f7f7f9')
    expect(lightTheme).toContain('--surface-code: #f6f6fa')
    expect(lightTheme).toContain('--surface-muted: #fafafd')
    expect(lightTheme).toContain('--flow-node: #efedfb')
  })

  it('uses theme tokens on the previously dark-only console surfaces', () => {
    expect(styles).toMatch(/\.event-terminal\s*\{[^}]*background: var\(--surface-inset\)/)
    expect(styles).toMatch(/\.entity-card dl div\s*\{[^}]*background: var\(--surface-inset\)/)
    expect(styles).toMatch(/\.editable-table\s*\{[^}]*background: var\(--surface-inset\)/)
    expect(styles).toMatch(/\.editor-footer\s*\{[^}]*background: var\(--surface-inset\)/)
    expect(styles).toMatch(/\.flow-card-body\s*\{[^}]*background: var\(--surface-overlay\)/)
    expect(styles).toMatch(/\.yaml-preview\s*\{[^}]*background: var\(--surface-code\)/)
    expect(styles).toContain('.list-panel > .resource-state.compact')
  })
})
