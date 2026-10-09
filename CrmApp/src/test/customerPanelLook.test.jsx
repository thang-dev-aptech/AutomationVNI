import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import fs from 'node:fs'
import path from 'node:path'
import { CustomerPanel } from '../features/inbox/components/CustomerPanel'
import { inboxApi } from '../features/inbox/api/inboxApi'

const read = (rel) => fs.readFileSync(path.resolve(__dirname, rel), 'utf8')

const profileWith = (identities, participant = { displayName: 'Khách', externalId: 'ext-1' }) => ({
  linked: true,
  participant,
  customer: { id: 'c1', displayName: 'Khách', phoneE164: '+84900000000', email: null, tagIds: [], identities },
  stats: {},
  media: [],
  activities: [],
})

const renderPanel = async (profile, item = { id: 'conv-1', kind: 2 }) => {
  vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue(profile)
  const view = render(<MemoryRouter><CustomerPanel item={item} /></MemoryRouter>)
  await waitFor(() => expect(screen.getByTestId('customer-identities-list')).toBeInTheDocument())
  return view
}

describe('AC 48a82d2f — CustomerPanel: logo kênh, section phẳng, hero gradient', () => {
  beforeEach(() => vi.restoreAllMocks())
  afterEach(() => vi.restoreAllMocks())

  it('(a) map SocialPlatform đúng enum: 1 Facebook, 2 LinkedIn, 3 Instagram, 4 TikTok, 5 Threads, lạ → icon chung', async () => {
    await renderPanel(profileWith([
      { platform: 1, channelName: 'P1', externalId: 'a' },
      { platform: 2, channelName: 'P2', externalId: 'b' },
      { platform: 3, channelName: 'P3', externalId: 'c' },
      { platform: 4, channelName: 'P4', externalId: 'd' },
      { platform: 5, channelName: 'P5', externalId: 'e' },
      { platform: 99, channelName: 'P6', externalId: 'f' },
    ]))
    const keyOf = (i) => screen.getByTestId(`identity-item-${i}`).querySelector('svg').getAttribute('data-testid')
    expect([0, 1, 2, 3, 4, 5].map(keyOf)).toEqual([
      'platform-logo-facebook',
      'platform-logo-linkedin',
      'platform-logo-instagram',
      'platform-logo-tiktok',
      'platform-logo-threads',
      'platform-logo-generic',
    ])
    expect(screen.getByTestId('customer-identities-list').textContent).not.toMatch(/[📘📸🎵▶🧵💬🌐]/u)
  })

  it('(a) identity Facebook của hội thoại tin nhắn → Messenger; hội thoại bình luận → Facebook', async () => {
    const identities = [{ platform: 1, channelName: 'Page', externalId: 'ext-1' }]
    const { unmount } = await renderPanel(profileWith(identities), { id: 'conv-1', kind: 1 })
    expect(screen.getByTestId('identity-item-0').querySelector('svg')).toHaveAttribute('data-testid', 'platform-logo-messenger')
    unmount()
    await renderPanel(profileWith(identities), { id: 'conv-2', kind: 2 })
    expect(screen.getByTestId('identity-item-0').querySelector('svg')).toHaveAttribute('data-testid', 'platform-logo-facebook')
  })

  it('(b) SourceBadge và CustomerPanel dùng chung module PlatformLogo; không có bản SVG trùng', () => {
    const badge = read('../features/inbox/components/SourceBadge.jsx')
    const panel = read('../features/inbox/components/CustomerPanel.jsx')
    expect(badge).toMatch(/from '\.\/PlatformLogo'/)
    expect(panel).toMatch(/from '\.\/PlatformLogo'/)
    expect(badge).not.toMatch(/<svg|<circle|<path/)
    expect(panel).not.toMatch(/<svg|<circle|<path/)
    expect(panel).not.toMatch(/getPlatformInfo/)
  })

  it('(c) section không còn kiểu card; hero có gradient bằng biến CSS; đủ 5 mục đúng thứ tự', async () => {
    await renderPanel(profileWith([{ platform: 1, externalId: 'a' }]))
    expect(screen.getByTestId('customer-panel-hero')).toBeInTheDocument()

    const css = read('../features/inbox/components/CustomerPanel.css')
    const section = css.match(/\.crm-customer-section\s*\{([^}]*)\}/)[1]
    expect(section).toMatch(/border\s*:\s*none/)
    expect(section).toMatch(/border-radius\s*:\s*0/)
    expect(section).toMatch(/border-bottom\s*:/)
    const hero = css.match(/\.crm-customer-profile-hero\s*\{([^}]*)\}/)[1]
    expect(hero).toMatch(/background\s*:\s*var\(--crm-hero-gradient/)
    expect(read('../index.css')).toMatch(/--crm-hero-gradient\s*:\s*linear-gradient/)

    const order = ['info', 'channels', 'stats', 'media', 'activities']
      .map((k) => screen.getByTestId(`customer-section-${k}`))
    for (let i = 1; i < order.length; i += 1) {
      expect(order[i - 1].compareDocumentPosition(order[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    }
  })
})
