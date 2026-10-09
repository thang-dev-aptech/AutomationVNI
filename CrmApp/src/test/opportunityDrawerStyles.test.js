import { describe, it, expect } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'

// AC ed1802f4 (a): không còn class crm-opp-* "mồ côi" (dùng trong drawer nhưng không có selector CSS).
const featureDir = path.resolve(__dirname, '../features/opportunities')
const drawerJsx = path.join(featureDir, 'components/OpportunityDrawer.jsx')
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
  for (const m of read(file).matchAll(/crm-opp-[a-z0-9_-]*[a-z0-9_]/g)) used.add(m[0])
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

describe('Drawer cơ hội: không có class crm-opp-* mồ côi', () => {
  it('mọi class crm-opp-* trong OpportunityDrawer.jsx có selector trong CSS được import', () => {
    const cssFiles = importedCss([drawerJsx, featureJsx])
    expect(cssFiles.length).toBeGreaterThan(0)
    const defined = selectorsDefinedIn(cssFiles)
    const orphans = [...classesUsedIn(drawerJsx)].filter((c) => !defined.has(c)).sort()
    expect(orphans).toEqual([])
  })

  it('drawer import CSS riêng của nó', () => {
    expect(importedCss([drawerJsx]).some((f) => f.endsWith('OpportunityDrawer.css'))).toBe(true)
  })
})
