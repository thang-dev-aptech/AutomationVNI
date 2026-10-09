import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import fs from 'node:fs'
import path from 'node:path'
import InboxFeature from '../features/inbox/InboxFeature'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { opportunityApi } from '../features/opportunities/api/opportunityApi'
import { OpportunityCard } from '../features/opportunities/components/OpportunityCard'
import { InboxList } from '../features/inbox/components/InboxList'
import crmApi from '../api/crmApi'
import { useAuthStore } from '../auth/authStore'

// Mock ở tầng HTTP (crmApi/axios) — KHÔNG mock opportunityApi.byConversation — để bắt lỗi unwrap envelope.
describe('B1 — by-conversation envelope {success:true,data:null} ở tầng HTTP', () => {
  const item = {
    id: 'conv-1', kind: 1, platform: 1, channelName: 'Page', displayName: 'Khách A', snippet: 'hi',
    lastCustomerActivityAt: '2026-10-09T08:00:00Z', status: 1, assignedUserId: null, unreadCount: 0,
    canReply: true, tags: [],
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-reviewer', email: 'r@vni.local', userName: 'Reviewer', roles: ['Reviewer'],
    })
    vi.spyOn(inboxApi, 'listChannels').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listUsers').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listTags').mockResolvedValue([])
    vi.spyOn(inboxApi, 'filter').mockResolvedValue({ items: [item], total: 1 })
    vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue({ linked: false, participant: { displayName: 'Khách A' } })
    vi.spyOn(inboxApi, 'getMessage').mockResolvedValue({
      kind: 1,
      conversation: { id: 'conv-1', participantName: 'Khách A', channelName: 'Page', canReply: true, isReplyWindowOpen: true, inboxStatus: 1, messages: [] },
      tags: [],
      replyEndpoint: '/api/PageMessage/conv-1/send',
    })
  })

  afterEach(() => vi.restoreAllMocks())

  const renderInbox = () => render(<MemoryRouter><InboxFeature /></MemoryRouter>)

  it('data:null (chưa có cơ hội) → hiện "Tạo cơ hội", không hiện "Xem cơ hội"', async () => {
    vi.spyOn(crmApi, 'get').mockImplementation(async (url) => {
      if (String(url).includes('/CrmOpportunity/by-conversation/')) return { data: { success: true, data: null } }
      return { data: { success: true, data: [] } }
    })
    expect(await opportunityApi.byConversation('message', 'conv-1')).toBeNull()

    renderInbox()

    expect(await screen.findByTestId('btn-create-opportunity')).toBeInTheDocument()
    expect(screen.queryByTestId('btn-view-opportunity')).not.toBeInTheDocument()
  })

  it('data:{id,status:1} → hiện "Xem cơ hội"', async () => {
    vi.spyOn(crmApi, 'get').mockImplementation(async (url) => {
      if (String(url).includes('/CrmOpportunity/by-conversation/'))
        return { data: { success: true, data: { id: 'opp-1', status: 1, isArchived: false } } }
      return { data: { success: true, data: [] } }
    })

    renderInbox()

    expect(await screen.findByTestId('btn-view-opportunity')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByTestId('btn-create-opportunity')).not.toBeInTheDocument())
  })
})

describe('B2 — SourceBadge inline ngoài inbox, overlay trên avatar inbox', () => {
  const css = fs.readFileSync(path.resolve(__dirname, '../modules/inbox/InboxPage.css'), 'utf8')

  it('quy tắc CSS gốc .crm-conv-source-badge KHÔNG position:absolute; chỉ modifier --overlay mới absolute', () => {
    const base = css.match(/\.crm-conv-source-badge\s*\{([^}]*)\}/)[1]
    const overlay = css.match(/\.crm-conv-source-badge--overlay\s*\{([^}]*)\}/)[1]
    expect(base).not.toMatch(/position\s*:/)
    expect(overlay).toMatch(/position\s*:\s*absolute/)
  })

  it('thẻ cơ hội có kênh: badge nằm trong .crm-opp-source-tag và không có modifier overlay', () => {
    const opp = {
      id: 'o1', title: 'Cơ hội', customerName: 'Khách', channelPlatform: 1, channelName: 'Page A', source: 2,
      stageId: 's1', status: 1, expectedValue: 0, watcherUserIds: [],
    }
    const { container } = render(<MemoryRouter><OpportunityCard opp={opp} stageColor="#3b82f6" /></MemoryRouter>)
    const badge = container.querySelector('.crm-conv-source-badge')
    expect(badge).not.toBeNull()
    expect(badge.closest('.crm-opp-source-tag')).not.toBeNull()
    expect(badge.className).not.toContain('--overlay')
  })

  it('danh sách inbox: badge trong avatar vẫn có modifier overlay', () => {
    render(<InboxList items={[{ id: 'm1', kind: 1, platform: 1, displayName: 'Khách', status: 1 }]} />)
    const badge = screen.getByTestId('conv-source-m1')
    expect(badge.closest('.crm-conv-avatar')).not.toBeNull()
    expect(badge.className).toContain('crm-conv-source-badge--overlay')
  })
})
