import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import CrmLayout from '../layouts/CrmLayout'
import InboxFeature from '../features/inbox/InboxFeature'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { useAuthStore } from '../auth/authStore'

describe('AC e68ba436 & AC 34bde757 — CrmApp Inbox Layout & Paging', () => {
  const createMockItems = (startIndex, count) => {
    return Array.from({ length: count }, (_, i) => {
      const num = startIndex + i
      return {
        id: `item-${num}`,
        kind: 1, // message
        socialChannelId: '00000000-0000-0000-0000-000000000001',
        channelName: 'VNI Fanpage Tuyển sinh',
        displayName: `Khách hàng ${num}`,
        snippet: `Nội dung tin nhắn ${num}`,
        lastCustomerActivityAt: '2026-10-07T03:15:00Z',
        status: 1,
        assignedUserId: null,
        assignedTo: null,
        unreadCount: 0,
        canReply: true,
        tags: [],
      }
    })
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-reviewer',
      email: 'reviewer@vni.local',
      userName: 'Reviewer Care',
      roles: ['Reviewer'],
    })

    vi.spyOn(inboxApi, 'listUsers').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listTags').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listChannels').mockResolvedValue([])
  })

  afterEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
  })

  // =========================================================================
  // AC e68ba436 (a)(c) — Inbox full-screen layout & 3 columns
  // =========================================================================
  describe('AC e68ba436 — Full-screen 3-column Layout', () => {
    it('(a) /inbox main has full modifier (crm-content--full), while /customers keeps standard crm-content', () => {
      // 1. Render at /inbox
      const { unmount } = render(
        <MemoryRouter initialEntries={['/inbox']}>
          <Routes>
            <Route element={<CrmLayout />}>
              <Route path="/inbox" element={<div data-testid="inbox-page-dummy">Inbox</div>} />
              <Route path="/customers" element={<div data-testid="customers-page-dummy">Customers</div>} />
            </Route>
          </Routes>
        </MemoryRouter>,
      )

      const mainInbox = screen.getByTestId('crm-main-content')
      expect(mainInbox).toHaveClass('crm-content')
      expect(mainInbox).toHaveClass('crm-content--full')

      unmount()

      // 2. Render at /customers
      render(
        <MemoryRouter initialEntries={['/customers']}>
          <Routes>
            <Route element={<CrmLayout />}>
              <Route path="/inbox" element={<div data-testid="inbox-page-dummy">Inbox</div>} />
              <Route path="/customers" element={<div data-testid="customers-page-dummy">Customers</div>} />
            </Route>
          </Routes>
        </MemoryRouter>,
      )

      const mainCustomers = screen.getByTestId('crm-main-content')
      expect(mainCustomers).toHaveClass('crm-content')
      expect(mainCustomers).not.toHaveClass('crm-content--full')
    })

    it('(b) renders all 3 regions: conversation-list, inbox-detail-pane, and customer-panel', async () => {
      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: createMockItems(1, 5),
        total: 5,
      })

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('conversation-list')).toBeInTheDocument()
        expect(screen.getByTestId('inbox-detail-pane')).toBeInTheDocument()
        expect(screen.getByTestId('customer-panel')).toBeInTheDocument()
      })
    })

    it('(c) collapses and re-expands customer-panel when clicking toggle button, persisting in localStorage', async () => {
      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: createMockItems(1, 5),
        total: 5,
      })

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('customer-panel')).toBeInTheDocument()
      })

      // Click collapse button
      const collapseBtn = screen.getByTestId('toggle-customer-panel')
      fireEvent.click(collapseBtn)

      // customer-panel is hidden
      expect(screen.queryByTestId('customer-panel')).not.toBeInTheDocument()
      expect(localStorage.getItem('crm_customer_panel_open')).toBe('false')

      // Click open button
      const openBtn = screen.getByTestId('toggle-customer-panel')
      fireEvent.click(openBtn)

      // customer-panel is visible again
      expect(screen.getByTestId('customer-panel')).toBeInTheDocument()
      expect(localStorage.getItem('crm_customer_panel_open')).toBe('true')
    })

    it('(d) viewport layout: 3 independent scroll regions, composer at bottom of chat pane, and 390px mobile view toggles', async () => {
      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: createMockItems(1, 5),
        total: 5,
      })

      // Simulate 1440x900 desktop viewport
      window.innerWidth = 1440
      window.innerHeight = 900

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('conversation-list')).toBeInTheDocument()
        expect(screen.getByTestId('inbox-detail-pane')).toBeInTheDocument()
        expect(screen.getByTestId('customer-panel')).toBeInTheDocument()
      })

      // Composer (reply-form) is inside chat pane footer
      const detailPane = screen.getByTestId('inbox-detail-pane')
      const replyForm = screen.getByTestId('reply-form')
      expect(detailPane).toContainElement(replyForm)

      // Mobile 390px viewport: click conv item to switch to detail, and back button returns to list
      window.innerWidth = 390
      window.innerHeight = 844

      fireEvent.click(screen.getByTestId('conv-item-item-1'))
      await waitFor(() => {
        expect(screen.getByTestId('btn-back-to-list')).toBeInTheDocument()
      })

      fireEvent.click(screen.getByTestId('btn-back-to-list'))
      expect(screen.getByTestId('conversation-list')).toBeInTheDocument()
    })
  })

  // =========================================================================
  // AC 34bde757 (a)-(e) — Inbox List Paging (Index/Size, Total=75)
  // =========================================================================
  describe('AC 34bde757 — Inbox List Paging (Total=75, Size=30)', () => {
    it('(a) first request sends {index: 1, size: 30} and DOES NOT have pageSize', async () => {
      const filterSpy = vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: createMockItems(1, 30),
        total: 75,
      })

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(filterSpy).toHaveBeenCalled()
      })

      const firstCallArg = filterSpy.mock.calls[0][0]
      expect(firstCallArg).toMatchObject({ index: 1, size: 30 })
      expect(firstCallArg).not.toHaveProperty('pageSize')
    })

    it('(b) scrolls near end or clicks "Tải thêm" to call index=2 and index=3, has 75 unique items without duplicates, stops at Total and displays "75 / 75"', async () => {
      // Provide an overlapping item in page 2 to test deduplication by (kind, id)
      const page1 = createMockItems(1, 30)
      const page2WithOverlap = [page1[29], ...createMockItems(31, 30)] // 31 items with 1 overlap
      const page3 = createMockItems(61, 15)

      const filterSpy = vi.spyOn(inboxApi, 'filter').mockImplementation(async (req) => {
        if (req.index === 1) {
          return { items: page1, total: 75 }
        }
        if (req.index === 2) {
          return { items: page2WithOverlap, total: 75 }
        }
        if (req.index === 3) {
          return { items: page3, total: 75 }
        }
        return { items: [], total: 75 }
      })

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      // Page 1 loaded (30 items)
      await waitFor(() => {
        expect(screen.getByTestId('conv-item-item-1')).toBeInTheDocument()
        expect(screen.getByTestId('conv-item-item-30')).toBeInTheDocument()
      })
      expect(screen.getByTestId('conv-list-footer')).toHaveTextContent('30 / 75')

      // Click "Tải thêm" -> calls index=2
      const loadMoreBtn = screen.getByTestId('btn-load-more')
      fireEvent.click(loadMoreBtn)

      await waitFor(() => {
        expect(filterSpy).toHaveBeenCalledWith(expect.objectContaining({ index: 2, size: 30 }))
        expect(screen.getByTestId('conv-item-item-60')).toBeInTheDocument()
      })
      // Overlap item-30 is deduped; total unique items = 60
      expect(screen.getByTestId('conv-list-footer')).toHaveTextContent('60 / 75')

      // Click "Tải thêm" again -> calls index=3
      fireEvent.click(screen.getByTestId('btn-load-more'))

      await waitFor(() => {
        expect(filterSpy).toHaveBeenCalledWith(expect.objectContaining({ index: 3, size: 30 }))
        expect(screen.getByTestId('conv-item-item-75')).toBeInTheDocument()
      })

      // Total 75 items loaded; deduplication confirmed
      expect(screen.getByTestId('conv-list-footer')).toHaveTextContent('75 / 75')
      expect(screen.queryByTestId('btn-load-more')).not.toBeInTheDocument()
      expect(screen.queryByTestId('inbox-scroll-sentinel')).not.toBeInTheDocument()
      expect(filterSpy).toHaveBeenCalledTimes(3)
    })

    it('(c) changing filters or keyword resets to index=1 and replaces list', async () => {
      const page1 = createMockItems(1, 30)
      const filteredItem = {
        id: 'filtered-1',
        kind: 1,
        displayName: 'Nguyễn Tìm Kiếm',
        snippet: 'Nội dung tìm thấy',
        lastCustomerActivityAt: '2026-10-07T03:15:00Z',
        status: 1,
        canReply: true,
        tags: [],
      }

      const filterSpy = vi.spyOn(inboxApi, 'filter').mockImplementation(async (req) => {
        if (req.keyword === 'Tìm Kiếm') {
          return { items: [filteredItem], total: 1 }
        }
        return { items: page1, total: 75 }
      })

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('conv-item-item-1')).toBeInTheDocument()
      })

      // Search keyword
      const searchInput = screen.getByTestId('inbox-search-input')
      fireEvent.change(searchInput, { target: { value: 'Tìm Kiếm' } })
      fireEvent.click(screen.getByTestId('btn-search-inbox'))

      await waitFor(() => {
        expect(filterSpy).toHaveBeenCalledWith(
          expect.objectContaining({ index: 1, size: 30, keyword: 'Tìm Kiếm' }),
        )
        expect(screen.getByTestId('conv-item-filtered-1')).toBeInTheDocument()
        expect(screen.queryByTestId('conv-item-item-1')).not.toBeInTheDocument()
      })
      expect(screen.getByTestId('conv-list-footer')).toHaveTextContent('1 / 1')
    })

    it('(d) background polling refresh when on page 3 only calls index=1, keeps 75 items, and keeps selected conversation', async () => {
      const page1 = createMockItems(1, 30)
      const page2 = createMockItems(31, 30)
      const page3 = createMockItems(61, 15)

      const filterSpy = vi.spyOn(inboxApi, 'filter').mockImplementation(async (req) => {
        if (req.index === 1) return { items: page1, total: 75 }
        if (req.index === 2) return { items: page2, total: 75 }
        if (req.index === 3) return { items: page3, total: 75 }
        return { items: [], total: 75 }
      })

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('conv-item-item-1')).toBeInTheDocument()
      })

      // Load page 2 and page 3
      fireEvent.click(screen.getByTestId('btn-load-more'))
      await waitFor(() => expect(screen.getByTestId('conv-item-item-31')).toBeInTheDocument())

      fireEvent.click(screen.getByTestId('btn-load-more'))
      await waitFor(() => expect(screen.getByTestId('conv-item-item-75')).toBeInTheDocument())

      // Select item on page 2 (item-45)
      fireEvent.click(screen.getByTestId('conv-item-item-45'))
      expect(screen.getByTestId('conv-item-item-45')).toHaveClass('active')

      filterSpy.mockClear()

      // Trigger background polling (e.g. window focus)
      act(() => {
        window.dispatchEvent(new Event('focus'))
      })

      await waitFor(() => {
        expect(filterSpy).toHaveBeenCalledTimes(1)
      })

      // Verified: background poll requested ONLY index=1
      expect(filterSpy).toHaveBeenCalledWith(expect.objectContaining({ index: 1, size: 30 }))

      // Verified: all 75 items are still preserved
      expect(screen.getByTestId('conv-list-footer')).toHaveTextContent('75 / 75')
      expect(screen.getByTestId('conv-item-item-75')).toBeInTheDocument()

      // Verified: selected conversation is still active
      expect(screen.getByTestId('conv-item-item-45')).toHaveClass('active')
    })

    it('(e) error on page 2 keeps 30 existing items, displays retry button, and clicking it calls index=2 again', async () => {
      const page1 = createMockItems(1, 30)
      const page2 = createMockItems(31, 30)
      let shouldFailPage2 = true

      const filterSpy = vi.spyOn(inboxApi, 'filter').mockImplementation(async (req) => {
        if (req.index === 1) return { items: page1, total: 75 }
        if (req.index === 2) {
          if (shouldFailPage2) {
            throw new Error('Lỗi kết nối máy chủ khi tải trang 2')
          }
          return { items: page2, total: 75 }
        }
        return { items: [], total: 75 }
      })

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('conv-item-item-1')).toBeInTheDocument()
      })

      // Click "Tải thêm" to trigger page 2 error
      fireEvent.click(screen.getByTestId('btn-load-more'))

      await waitFor(() => {
        expect(screen.getByTestId('page-error-container')).toBeInTheDocument()
        expect(screen.getByTestId('btn-retry-page')).toBeInTheDocument()
      })

      // Existing 30 items are preserved intact
      expect(screen.getByTestId('conv-item-item-1')).toBeInTheDocument()
      expect(screen.getByTestId('conv-item-item-30')).toBeInTheDocument()
      expect(screen.getByTestId('conv-list-footer')).toHaveTextContent('30 / 75')

      // Now server recovers; click retry
      shouldFailPage2 = false
      fireEvent.click(screen.getByTestId('btn-retry-page'))

      await waitFor(() => {
        expect(screen.getByTestId('conv-item-item-60')).toBeInTheDocument()
        expect(screen.queryByTestId('page-error-container')).not.toBeInTheDocument()
      })

      expect(screen.getByTestId('conv-list-footer')).toHaveTextContent('60 / 75')
    })
  })
})
