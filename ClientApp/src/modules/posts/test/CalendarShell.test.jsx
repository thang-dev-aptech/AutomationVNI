import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PostCalendarPage from '../pages/PostCalendarPage'
import { CALENDAR_STATUS_GROUPS, DEFAULT_STATUS_KEYS } from '../constants/calendarStatus'

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: 'ch-fb', pageName: 'VNi Fanpage', platform: 1, externalPageId: '111' },
      { id: 'ch-tt', pageName: 'TikTok Shop', platform: 4, externalPageId: '222' },
    ],
    isLoading: false,
  }),
}))

vi.mock('@/modules/social-channels/hooks/useChannelGroups', () => ({
  useChannelGroupAll: () => ({
    data: [
      { id: 'g1', name: 'Miền Bắc', channelCount: 2 },
    ],
    isLoading: false,
  }),
}))

vi.mock('../hooks/useCalendarQuery', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    useCalendarFacets: () => ({
      data: {
        authors: [{ userId: 'u1', name: 'alice', count: 2 }],
        categories: [{ categoryId: 'c1', name: 'Thể thao', count: 1 }],
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    }),
    useCalendarPosts: () => ({
      data: [{ id: 'p1', title: 'Demo' }],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    }),
    useCalendarList: () => ({
      data: { items: [], total: 0, index: 1, size: 20 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    }),
  }
})

function renderPage(initialEntry = '/calendar') {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  const router = createMemoryRouter(
    [{ path: '/calendar', element: <PostCalendarPage /> }],
    { initialEntries: [initialEntry] },
  )
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return router
}

describe('Calendar shell — AC calendar-views-ui-test (a)(e)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('(e) sidebar has channel/group radio, channel search, and status labels from mapping', () => {
    renderPage()

    expect(screen.getByRole('heading', { name: 'Bộ lọc bài đăng' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Kênh' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Nhóm kênh' })).toBeInTheDocument()
    expect(screen.getByLabelText('Tìm kênh')).toBeInTheDocument()

    for (const group of CALENDAR_STATUS_GROUPS) {
      expect(screen.getByLabelText(group.label)).toBeInTheDocument()
    }

    for (const key of DEFAULT_STATUS_KEYS) {
      const group = CALENDAR_STATUS_GROUPS.find((g) => g.key === key)
      expect(screen.getByLabelText(group.label)).toBeChecked()
    }
    expect(screen.getByLabelText('Nháp')).not.toBeChecked()
  })

  it('(e) channel search filters channel list', async () => {
    const user = userEvent.setup()
    renderPage()

    expect(screen.getByText('VNi Fanpage')).toBeInTheDocument()
    expect(screen.getByText('TikTok Shop')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Tìm kênh'), 'tiktok')
    expect(screen.queryByText('VNi Fanpage')).not.toBeInTheDocument()
    expect(screen.getByText('TikTok Shop')).toBeInTheDocument()
  })

  it('(a) changing view updates URL and content', async () => {
    const user = userEvent.setup()
    const router = renderPage('/calendar')

    expect(screen.getByRole('tab', { name: 'Lịch' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByText('Chế độ Lịch')).toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Danh sách' }))
    expect(screen.getByText('Chế độ Danh sách')).toBeInTheDocument()
    await waitFor(() => {
      expect(router.state.location.search).toContain('view=list')
    })

    await user.click(screen.getByRole('tab', { name: 'Theo kênh' }))
    expect(screen.getByText('Chế độ Theo kênh')).toBeInTheDocument()
    await waitFor(() => {
      expect(router.state.location.search).toContain('view=by-channel')
    })
  })

  it('(a) view + filters in URL are applied on load', () => {
    renderPage('/calendar?view=by-channel&status=draft,scheduled&channelMode=group')

    expect(screen.getByRole('tab', { name: 'Theo kênh' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByText('Chế độ Theo kênh')).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Nhóm kênh' })).toBeChecked()
    expect(screen.getByLabelText('Nháp')).toBeChecked()
    expect(screen.getByLabelText('Chờ đăng')).toBeChecked()
    expect(screen.getByLabelText('Thành công')).not.toBeChecked()
    expect(screen.getByText('Miền Bắc')).toBeInTheDocument()
  })

  it('(a) toggling filters writes URL; remount from URL keeps filters', async () => {
    const user = userEvent.setup()
    const router = renderPage('/calendar')

    await user.click(screen.getByLabelText('Nháp'))
    await waitFor(() => {
      expect(router.state.location.search).toMatch(/status=.*draft/)
    })

    await user.click(screen.getByRole('radio', { name: 'Nhóm kênh' }))
    await waitFor(() => {
      expect(router.state.location.search).toContain('channelMode=group')
    })

    const search = router.state.location.search
    renderPage(`/calendar${search}`)
    expect(screen.getAllByLabelText('Nháp')[0]).toBeChecked()
    expect(screen.getAllByRole('radio', { name: 'Nhóm kênh' })[0]).toBeChecked()
  })
})
