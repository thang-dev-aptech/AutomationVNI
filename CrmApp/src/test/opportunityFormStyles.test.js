import { describe, it, expect } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'

// AC d71d861a (a): không còn class crm-opp-* "mồ côi" trong OpportunityFormModal.
const featureDir = path.resolve(__dirname, '../features/opportunities')
const formJsx = path.join(featureDir, 'components/OpportunityFormModal.jsx')
const featureJsx = path.join(featureDir, 'OpportunitiesFeature.jsx')

const read = (file) => fs.readFileSync(file, 'utf8')

/** CSS được import trực tiếp (import './x.css') bởi các file JSX cho trước. */
function importedCss(jsxFiles) {
  const out = new Set()
  for (const file of jsxFiles) {
    for (const m of read(file).matchAll(/import\s+['"](\.[^'"]+\.css)['"]/g)) {
      out.add(path.resolve(path.dirname(file), m[1]))
    }
  }
  return [...out]
}

const classesUsedIn = (file) => {
  const used = new Set()
  for (const m of read(file).matchAll(/crm-opp-[a-z0-9_-]*[a-z0-9_]/g)) {
    const name = m[0]
    // Bỏ qua class do lucide-react sinh ra (nếu có trong JSX).
    if (name.startsWith('lucide')) continue
    used.add(name)
  }
  return used
}

const selectorsDefinedIn = (cssFiles) => {
  const defined = new Set()
  for (const file of cssFiles) {
    const css = read(file).replace(/\/\*[\s\S]*?\*\//g, '')
    for (const m of css.matchAll(/\.(crm-opp-[a-z0-9_-]+)/g)) defined.add(m[1])
  }
  return defined
}

describe('Modal cơ hội: không có class crm-opp-* mồ côi', () => {
  it('mọi class crm-opp-* trong OpportunityFormModal.jsx có selector trong CSS được import', () => {
    const cssFiles = importedCss([formJsx, featureJsx])
    expect(cssFiles.length).toBeGreaterThan(0)
    expect(cssFiles.some((f) => f.endsWith('OpportunityFormModal.css'))).toBe(true)
    const defined = selectorsDefinedIn(cssFiles)
    const orphans = [...classesUsedIn(formJsx)].filter((c) => !defined.has(c)).sort()
    expect(orphans).toEqual([])
  })

  it('form modal import CSS riêng của nó', () => {
    expect(importedCss([formJsx]).some((f) => f.endsWith('OpportunityFormModal.css'))).toBe(true)
  })
})
