import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import CrawlInboxPage from '../pages/CrawlInboxPage'
import { CRAWLED_ARTICLE_STATUS } from '../constants/crawlConstants'

const h = vi.hoisted(() => ({
  summary: undefined,
  mutation: () => ({ mutateAsync: () => Promise.resolve({}), isPending: false }),
}))

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({ canApproveCrawl: true, canManageCrawlSources: true }),
}))
vi.mock('../components/CrawlSourceModal', () => ({ default: () => null }))
vi.mock('../components/ApproveArticleModal', () => ({ default: () => null }))
vi.mock('../hooks/useCrawl', () => ({
  useCrawledArticles: () => ({
    data: {
      items: [
        { id: 'a-pending', title: 'Tin chờ duyệt', status: CRAWLED_ARTICLE_STATUS.PENDING, qualityScore: 90 },
        { id: 'a-dup', title: 'Tin bị đánh trùng', status: CRAWLED_ARTICLE_STATUS.DUPLICATE, qualityScore: 80 },
      ],
      total: 2,
    },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useCrawlSummary: () => ({ data: h.summary }),
  useCrawlPipelineState: () => ({ data: { enabled: true }, isLoading: false, isError: false }),
  useSetCrawlPipelineEnabled: h.mutation,
  useApproveArticle: h.mutation,
  useRejectArticle: h.mutation,
  useMarkNotDuplicate: h.mutation,
  useRededupArticle: h.mutation,
  useSweepAutoApprove: h.mutation,
}))

const OFF_NOTE = /Đăng lên trang tin tức đang tắt/

function renderPage() {
  render(<MemoryRouter><CrawlInboxPage /></MemoryRouter>)
}

describe('NEWS-PUBLISH-OFF-01 website-publish-off-ui-test (AC 8bc9eab2): trang Tin đã cào', () => {
  beforeEach(() => { h.summary = undefined })

  it('hides every "put on the website" action and explains why when website publishing is off', () => {
    h.summary = { websitePublishEnabled: false, twoGateFlow: true, byStatus: {} }
    renderPage()

    expect(screen.queryByRole('button', { name: 'Duyệt & đưa lên web' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Quét tồn đọng' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(OFF_NOTE)
    // Các việc không đưa lên web vẫn làm được.
    expect(screen.getByRole('button', { name: 'Loại' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Không trùng' })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Chấm lại trùng' })).toHaveLength(2)
  })

  it.each([
    ['website publishing on', { websitePublishEnabled: true, twoGateFlow: true, byStatus: {} }],
    ['summary not loaded yet', undefined],
    ['old fan-out flow (TwoGateFlow off) — Duyệt does not touch the website', {
      websitePublishEnabled: false, twoGateFlow: false, byStatus: {},
    }],
  ])('keeps the usual actions (%s)', (_label, summary) => {
    h.summary = summary
    renderPage()

    expect(screen.getAllByRole('button', { name: 'Duyệt & đưa lên web' })).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Quét tồn đọng' })).toBeInTheDocument()
    expect(screen.queryByText(OFF_NOTE)).not.toBeInTheDocument()
  })
})
