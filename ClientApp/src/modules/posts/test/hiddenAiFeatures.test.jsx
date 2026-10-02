import { afterEach, describe, expect, it } from 'vitest'
import { FEATURES } from '@/shared/config/features'
import { getVisibleGenerationFlows } from '../components/GenerationFlowPicker'
import { DASHBOARD_QUICK_LINKS } from '@/modules/dashboard/utils/dashboardLayout'

const DEFAULTS = { ...FEATURES }
const allPermissions = { canViewPosts: true, canViewComments: true, canCreatePost: true }

describe('feature flags hiding AI image creation and bulk create (2026-10-02)', () => {
  afterEach(() => {
    Object.assign(FEATURES, DEFAULTS)
  })

  it('ships with AI image creation and bulk create hidden', () => {
    expect(DEFAULTS).toEqual({ aiFullImage: false, bulkCreate: false })
  })

  it('hides the full-AI method by default and shows it again when the flag is on', () => {
    expect(getVisibleGenerationFlows().map((o) => o.value)).toEqual(['template', 'media'])
    FEATURES.aiFullImage = true
    expect(getVisibleGenerationFlows().map((o) => o.value)).toEqual(['fullai', 'template', 'media'])
  })

  it('hides the bulk quick link by default and shows it again when the flag is on', () => {
    const visible = () => DASHBOARD_QUICK_LINKS.filter((l) => l.visible(allPermissions)).map((l) => l.to)
    expect(visible()).not.toContain('/bulk')
    FEATURES.bulkCreate = true
    expect(visible()).toContain('/bulk')
  })
})
