import { afterEach, describe, expect, it } from 'vitest'
import { FEATURES } from '@/shared/config/features'
import { getVisibleGenerationFlows } from '../components/GenerationFlowPicker'
import { DASHBOARD_QUICK_LINKS } from '@/modules/dashboard/utils/dashboardLayout'

const DEFAULTS = { ...FEATURES }
const BULK_FLOWS = ['fullai', 'template']
const allPermissions = { canViewPosts: true, canViewComments: true, canCreatePost: true }

describe('UI feature flags (user request 2026-10-03)', () => {
  afterEach(() => {
    Object.assign(FEATURES, DEFAULTS)
  })

  it('ships with full-AI shown, template and "Tạo từ chứng chỉ" hidden; AI image gen temporarily off', () => {
    expect(DEFAULTS).toEqual({
      aiFullImage: true,
      aiTemplate: false,
      chungChiBulk: false,
      aiImageGeneration: false,
    })
  })

  it('create page methods follow the flags', () => {
    expect(getVisibleGenerationFlows().map((o) => o.value)).toEqual(['fullai', 'media'])
    FEATURES.aiTemplate = true
    expect(getVisibleGenerationFlows().map((o) => o.value)).toEqual(['fullai', 'template', 'media'])
  })

  it('bulk page offers only AI methods and follows the flags', () => {
    expect(getVisibleGenerationFlows(BULK_FLOWS).map((o) => o.value)).toEqual(['fullai'])
    FEATURES.aiTemplate = true
    expect(getVisibleGenerationFlows(BULK_FLOWS).map((o) => o.value)).toEqual(['fullai', 'template'])
  })

  it('keeps the bulk create page reachable from the dashboard', () => {
    const links = DASHBOARD_QUICK_LINKS.filter((l) => l.visible(allPermissions)).map((l) => l.to)
    expect(links).toContain('/bulk')
  })
})

describe('"Tạo từ chứng chỉ" menu flag', () => {
  afterEach(() => {
    Object.assign(FEATURES, DEFAULTS)
  })

  async function visibleNavTargets() {
    const { NAV_GROUPS } = await import('@/app/layouts/MainLayout')
    return NAV_GROUPS.flatMap((g) => (g.children ?? [g]))
      .filter((item) => item.to && item.visible?.(allPermissions))
      .map((item) => item.to)
  }

  it('hides the sidebar item by default but keeps "Tạo hàng loạt"', async () => {
    const targets = await visibleNavTargets()
    expect(targets).not.toContain('/bulk-chung-chi')
    expect(targets).toContain('/bulk')
  })

  it('shows the sidebar item again when the flag is on', async () => {
    FEATURES.chungChiBulk = true
    expect(await visibleNavTargets()).toContain('/bulk-chung-chi')
  })
})
