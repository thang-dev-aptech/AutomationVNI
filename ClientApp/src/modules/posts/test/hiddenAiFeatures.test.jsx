import { afterEach, describe, expect, it } from 'vitest'
import { FEATURES } from '@/shared/config/features'
import { getVisibleGenerationFlows } from '../components/GenerationFlowPicker'
import { DASHBOARD_QUICK_LINKS } from '@/modules/dashboard/utils/dashboardLayout'

const DEFAULTS = { ...FEATURES }
const BULK_FLOWS = ['fullai', 'template']
const allPermissions = { canViewPosts: true, canViewComments: true, canCreatePost: true }

describe('hiding "Sinh toàn bộ bằng AI" (user request 2026-10-02)', () => {
  afterEach(() => {
    Object.assign(FEATURES, DEFAULTS)
  })

  it('ships with the full-AI method hidden', () => {
    expect(DEFAULTS).toEqual({ aiFullImage: false })
  })

  it('create page: hides full-AI by default, shows it again when the flag is on', () => {
    expect(getVisibleGenerationFlows().map((o) => o.value)).toEqual(['template', 'media'])
    FEATURES.aiFullImage = true
    expect(getVisibleGenerationFlows().map((o) => o.value)).toEqual(['fullai', 'template', 'media'])
  })

  it('bulk page: only AI flows are offered (no media option), full-AI hidden by default', () => {
    expect(getVisibleGenerationFlows(BULK_FLOWS).map((o) => o.value)).toEqual(['template'])
    FEATURES.aiFullImage = true
    expect(getVisibleGenerationFlows(BULK_FLOWS).map((o) => o.value)).toEqual(['fullai', 'template'])
  })

  it('keeps the bulk create page reachable from the dashboard', () => {
    const links = DASHBOARD_QUICK_LINKS.filter((l) => l.visible(allPermissions)).map((l) => l.to)
    expect(links).toContain('/bulk')
  })
})
